using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Hooks;

/// <summary>専用メッセージスレッドでフックを所有する、一回の起動と停止の単位。</summary>
public sealed class HookThread : IDisposable
{
    [ThreadStatic]
    private static HookThread? _current;

    private readonly object _lifecycle = new();
    private readonly IHookApi _api;
    private readonly HotkeyMatcher _matcher;
    private readonly HookInputBuffer _buffer;
    private readonly HookEventDelivery _events;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private Thread? _pump;
    private uint _threadId;
    private int _mouseTracking;
    private int _completed;
    private bool _started;
    private bool _cleanupSucceeded = true;
    private bool _disposed;
    private bool _stopped;

    /// <summary>API は Start まで呼ばず、表示・破棄通知に上限を付けて入力を配送する。</summary>
    public HookThread(HotkeyMatcher matcher, int capacity = 1024)
        : this(matcher, capacity, new Win32HookApi())
    {
    }

    internal HookThread(HotkeyMatcher matcher, int capacity, IHookApi api)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _matcher = matcher;
        _api = api;
        _buffer = new HookInputBuffer(capacity);
        _events = new HookEventDelivery(capacity);
    }

    /// <summary>コールバックとは別の配送スレッドから届くイベント。</summary>
    public ChannelReader<HookEvent> Events => _events;

    /// <summary>移動イベントを流すかどうか。ドラッグ中だけ有効にする。</summary>
    public bool MouseTrackingEnabled
    {
        get => Volatile.Read(ref _mouseTracking) != 0;
        set => Volatile.Write(ref _mouseTracking, value ? 1 : 0);
    }

    /// <summary>起動やメッセージループが例外で失敗した場合の診断。コールバックからは更新しない。</summary>
    public Exception? Failure { get; private set; }

    /// <summary>登録が完了するまで待ち、登録に失敗した場合は解除して false を返す。</summary>
    public bool Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped)
            {
                return false;
            }

            if (_thread is not null)
            {
                return _started && _thread.IsAlive;
            }

            _pump = new Thread(Pump) { IsBackground = true, Name = "Snapwell hook delivery" };
            _thread = new Thread(Run) { IsBackground = true, Name = "Snapwell hooks" };
            _pump.Start();
            _thread.Start();
            _ready.Wait();
            if (!_started)
            {
                _thread.Join();
                _pump.Join();
            }

            return _started;
        }
    }

    /// <summary>別スレッドから WM_QUIT を送り、解除と配送の完了を待つ。二重停止も許可する。</summary>
    public bool Stop()
    {
        lock (_lifecycle)
        {
            return StopCore();
        }
    }

    /// <summary>フックが停止できた場合だけ待機ハンドルを破棄する。</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            if (!StopCore() && _thread?.IsAlive == true)
            {
                return;
            }

            _disposed = true;
            _wake.Dispose();
            _ready.Dispose();
        }
    }

    private bool StopCore()
    {
        if (_thread is null)
        {
            _stopped = true;
            _events.Complete();
            return true;
        }

        if (_thread.IsAlive && Volatile.Read(ref _completed) == 0 && !_api.PostQuit(_threadId))
        {
            // 生きているコールバックの状態や待機ハンドルを破棄しない。呼び出し元が再試行する。
            return false;
        }

        _thread.Join();
        _pump!.Join();
        _started = false;
        _stopped = true;
        return _cleanupSucceeded;
    }

    private unsafe void Run()
    {
        var winEvents = new nint[5];
        nint mouse = 0;
        nint keyboard = 0;
        try
        {
            // OUTOFCONTEXT と低レベルフックの配送先は登録したこのスレッド。実機未確認。
            _current = this;
            _matcher.Reset();
            _api.CreateMessageQueue();
            _threadId = _api.GetThreadId();
            winEvents[0] = _api.RegisterEvent(PInvoke.EVENT_SYSTEM_MOVESIZESTART,
                (nint)(delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)&OnWinEvent);
            winEvents[1] = _api.RegisterEvent(PInvoke.EVENT_SYSTEM_MOVESIZEEND,
                (nint)(delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)&OnWinEvent);
            winEvents[2] = _api.RegisterEvent(PInvoke.EVENT_SYSTEM_FOREGROUND,
                (nint)(delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)&OnWinEvent);
            winEvents[3] = _api.RegisterEvent(PInvoke.EVENT_OBJECT_SHOW,
                (nint)(delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)&OnWinEvent);
            winEvents[4] = _api.RegisterEvent(PInvoke.EVENT_OBJECT_DESTROY,
                (nint)(delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)&OnWinEvent);
            mouse = _api.RegisterMouse((nint)(delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)&OnMouse);
            keyboard = _api.RegisterKeyboard(
                (nint)(delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)&OnKeyboard);
            if (winEvents.Any(hook => hook == 0) || mouse == 0 || keyboard == 0)
            {
                Failure = new InvalidOperationException("フックの登録に失敗した。");
                return;
            }

            _started = true;
            _ready.Set();
            int result;
            while ((result = _api.ReadAndDispatchMessage()) > 0)
            {
            }

            if (result < 0)
            {
                Failure = new InvalidOperationException("フックのメッセージを取得できない。");
            }
        }
        catch (Exception error)
        {
            Failure = error;
        }
        finally
        {
            foreach (var hook in winEvents)
            {
                if (hook != 0)
                {
                    _cleanupSucceeded &= _api.UnregisterEvent(hook);
                }
            }

            if (mouse != 0)
            {
                _cleanupSucceeded &= _api.UnregisterLowLevel(mouse);
            }

            if (keyboard != 0)
            {
                _cleanupSucceeded &= _api.UnregisterLowLevel(keyboard);
            }

            // static 関数ポインターは回収されない。解除後のコールバックも null を見て終了する。
            _current = null;
            Volatile.Write(ref _completed, 1);
            _wake.Set();
            _ready.Set();
        }
    }

    private void Pump()
    {
        while (true)
        {
            while (_buffer.TryRead(out var raw))
            {
                Publish(raw);
            }

            if (Volatile.Read(ref _completed) != 0)
            {
                while (_buffer.TryRead(out var remaining))
                {
                    Publish(remaining);
                }

                _events.Complete();
                return;
            }

            _wake.WaitOne();
        }
    }

    internal void Publish(RawHookEvent raw)
    {
        HookEvent value = raw.Kind switch
        {
            RawHookKind.MoveStart => new MoveSizeStarted(raw.Handle),
            RawHookKind.MoveEnd => new MoveSizeEnded(raw.Handle, raw.Time),
            RawHookKind.Foreground => new ForegroundChanged(raw.Handle),
            RawHookKind.WindowShown => new WindowShown(raw.Handle),
            RawHookKind.WindowDestroyed => new WindowDestroyed(raw.Handle),
            RawHookKind.MouseButton => new MouseButton((MouseButtonKind)raw.Detail, raw.IsDown, raw.X, raw.Y, raw.Time),
            RawHookKind.MouseMove => new MouseMoved(raw.X, raw.Y, raw.Time),
            _ => new KeyChord(raw.Detail),
        };
        _events.Write(value);
    }

    internal void Enqueue(RawHookEvent value)
    {
        if (_buffer.Write(value))
        {
            _wake.Set();
        }
    }

    internal bool ProcessKey(uint virtualKey, bool isDown)
    {
        var match = _matcher.ProcessKey(virtualKey, isDown);
        if (match.Matched)
        {
            Enqueue(new RawHookEvent(RawHookKind.Key, Detail: match.Id));
        }
        return match.Suppress;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnWinEvent(HWINEVENTHOOK hook, uint eventId, HWND window,
        int objectId, int childId, uint eventThreadId, uint time)
    {
        var owner = _current;
        if (owner is null || window.IsNull || objectId != 0 || childId != 0)
        {
            return;
        }

        var kind = eventId switch
        {
            PInvoke.EVENT_SYSTEM_MOVESIZESTART => RawHookKind.MoveStart,
            PInvoke.EVENT_SYSTEM_MOVESIZEEND => RawHookKind.MoveEnd,
            PInvoke.EVENT_OBJECT_SHOW => RawHookKind.WindowShown,
            PInvoke.EVENT_OBJECT_DESTROY => RawHookKind.WindowDestroyed,
            _ => RawHookKind.Foreground,
        };
        owner.Enqueue(new RawHookEvent(kind, (nint)window.Value, Time: time));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT OnMouse(int code, WPARAM message, LPARAM data)
    {
        var owner = _current;
        if (code >= 0 && owner is not null && data.Value != 0)
        {
            var mouse = (MSLLHOOKSTRUCT*)data.Value;
            var messageId = (uint)message.Value;
            if (messageId == PInvoke.WM_MOUSEMOVE)
            {
                if (Volatile.Read(ref owner._mouseTracking) != 0)
                {
                    owner.Enqueue(new RawHookEvent(RawHookKind.MouseMove,
                        X: mouse->pt.X, Y: mouse->pt.Y, Time: mouse->time));
                }
            }
            else
            {
                var button = messageId switch
                {
                    PInvoke.WM_LBUTTONDOWN or PInvoke.WM_LBUTTONUP => (int)MouseButtonKind.Left,
                    PInvoke.WM_RBUTTONDOWN or PInvoke.WM_RBUTTONUP => (int)MouseButtonKind.Right,
                    PInvoke.WM_MBUTTONDOWN or PInvoke.WM_MBUTTONUP => (int)MouseButtonKind.Middle,
                    PInvoke.WM_XBUTTONDOWN or PInvoke.WM_XBUTTONUP =>
                        (mouse->mouseData >> 16) == 1 ? (int)MouseButtonKind.X1 : (int)MouseButtonKind.X2,
                    _ => -1,
                };
                if (button >= 0)
                {
                    var isDown = messageId is PInvoke.WM_LBUTTONDOWN or PInvoke.WM_RBUTTONDOWN or
                        PInvoke.WM_MBUTTONDOWN or PInvoke.WM_XBUTTONDOWN;
                    owner.Enqueue(new RawHookEvent(RawHookKind.MouseButton,
                        X: mouse->pt.X, Y: mouse->pt.Y, Time: mouse->time, Detail: button, IsDown: isDown));
                }
            }
        }

        return PInvoke.CallNextHookEx(default(HHOOK), code, message, data);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT OnKeyboard(int code, WPARAM message, LPARAM data)
    {
        var owner = _current;
        if (code >= 0 && owner is not null && data.Value != 0)
        {
            var key = (KBDLLHOOKSTRUCT*)data.Value;
            var messageId = (uint)message.Value;
            if (messageId is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN or PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP)
            {
                if (owner.ProcessKey(key->vkCode, messageId is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN))
                {
                    return new LRESULT(1);
                }
            }
        }

        return PInvoke.CallNextHookEx(default(HHOOK), code, message, data);
    }
}
