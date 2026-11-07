using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Snapwell.Core.Hooks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Hooks;

/// <summary>張り直しを疑った証拠の種類。入力の中身（キー・座標）は含めない。</summary>
[Flags]
public enum HookRehookEvidence
{
    /// <summary>環境の変化（ロック解除・復帰など）による。疑いの証拠はない。</summary>
    None = 0,

    /// <summary>キーが押されているのに、キーボードのコールバックが来ない。</summary>
    KeyboardSilent = 1,

    /// <summary>カーソルが動き、OS も入力を見たのに、マウスのコールバックが来ない。</summary>
    MouseSilent = 2,
}

/// <summary>
/// 低レベルフックを張り直したときの記録。原因の調査用に、証拠の種類と各時刻からの経過（ミリ秒）を持つ。
/// </summary>
/// <param name="Reason">張り直した理由。</param>
/// <param name="Succeeded">登録し直しが成功したか。</param>
/// <param name="Count">何回目か。</param>
/// <param name="Evidence">疑いの証拠の種類。</param>
/// <param name="GapMs">OS の最後の入力から、フックの最後のコールバックまでの差（正なら OS のほうが新しい）。参考値。</param>
/// <param name="SinceKeyboardMs">キーボードのコールバックが最後に来てからの経過。</param>
/// <param name="SinceMouseMs">マウスのコールバックが最後に来てからの経過。</param>
/// <param name="SinceInputMs">OS の最後の入力からの経過。</param>
/// <param name="BackoffMs">判断のあとの、疑いによる張り直しのバックオフ。</param>
public readonly record struct HookRehookReport(
    HookRehookReason Reason,
    bool Succeeded,
    int Count,
    HookRehookEvidence Evidence = HookRehookEvidence.None,
    int GapMs = 0,
    int SinceKeyboardMs = 0,
    int SinceMouseMs = 0,
    int SinceInputMs = 0,
    uint BackoffMs = 0);

/// <summary>専用メッセージスレッドでフックを所有する、一回の起動と停止の単位。</summary>
public sealed class HookThread : IDisposable
{
    [ThreadStatic]
    private static HookThread? _current;

    private readonly object _lifecycle = new();
    private readonly IHookApi _api;
    private readonly HotkeyMatcher _matcher;
    private readonly IDummyKeySender _dummyKeySender;
    private readonly HookInputBuffer _buffer;
    private readonly HookEventDelivery _events;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private DummyKeyDispatcher? _dummyKeys;
    private Thread? _thread;
    private Thread? _pump;
    private uint _threadId;
    private readonly HookRehookPolicy _policy = new();
    private readonly int _healthPollMs;
    private Timer? _healthTimer;
    private nint _mouseHook;
    private nint _keyboardHook;
    private readonly object _healthGate = new();
    private int _lastKeyboardTick = Environment.TickCount;
    private int _lastMouseTick = Environment.TickCount;
    private bool _hasCursor;
    private int _cursorX;
    private int _cursorY;
    private uint _previousPollTick;
    private int _mouseSilentStreak;

    /// <summary>マウスが黙っている証拠が、この回数だけ続けて確認の間隔ごとに成り立ったら疑う。</summary>
    internal const int MouseSilentPolls = 3;
    private HookRehookReport _rehookDetail;
    private int _rehookCount;
    private int _mouseTracking;
    private int _completed;
    private bool _started;
    private bool _cleanupSucceeded = true;
    private bool _disposed;
    private bool _stopped;

    /// <summary>API は Start まで呼ばず、表示・破棄通知に上限を付けて入力を配送する。</summary>
    public HookThread(HotkeyMatcher matcher, int capacity = 1024)
        : this(matcher, capacity, new Win32HookApi(), null, DefaultHealthPollMs)
    {
    }

    internal const int DefaultHealthPollMs = 2000;

    internal HookThread(HotkeyMatcher matcher, int capacity, IHookApi api, IDummyKeySender? dummyKeySender = null,
        int healthPollMs = Timeout.Infinite)
    {
        _healthPollMs = healthPollMs;
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _matcher = matcher;
        _dummyKeySender = dummyKeySender ?? new Win32DummyKeySender();
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

    /// <summary>
    /// 低レベルフックを張り直したあとに、フックのスレッドとは別のスレッドで呼ぶ。記録用で、重い処理をしてよい。
    /// </summary>
    public event Action<HookRehookReport>? Rehooked;

    /// <summary>ロック・復帰などを伝える。復帰は張り直しの予約になり、間隔を空けて実行される。</summary>
    public void Notify(HookEnvironmentChange change)
    {
        // 起動前の通知で予約を残さない。起動直後に不要な張り直しが走り、起動直後の挙動の調査を濁すため。
        if (!_started)
        {
            return;
        }

        _policy.Notify(change, _api.GetTickCount());
        // 通知はトレイのウィンドウのスレッドから来る。全キーの走査やロック待ちで止めないよう、判断は別のスレッドで行う。
        ThreadPool.UnsafeQueueUserWorkItem(static self => self.CheckHealth(), this, preferLocal: false);
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
            else if (_healthPollMs > 0)
            {
                _healthTimer = new Timer(_ => CheckHealth(), null, _healthPollMs, _healthPollMs);
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
        _healthTimer?.Dispose();
        _healthTimer = null;
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
        try
        {
            // OUTOFCONTEXT と低レベルフックの配送先は登録したこのスレッド。実機未確認。
            _current = this;
            _matcher.Reset();
            _dummyKeys = new DummyKeyDispatcher(_dummyKeySender);
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
            _mouseHook = RegisterMouse(_api);
            _keyboardHook = RegisterKeyboard(_api);
            if (winEvents.Any(hook => hook == 0) || _mouseHook == 0 || _keyboardHook == 0)
            {
                Failure = new InvalidOperationException("フックの登録に失敗した。");
                return;
            }

            // 起動が遅れた場合に、構築からの入力を「届かなかった入力」と誤認しない。基準は登録の完了時に取る。
            ResetBaseline(_api.GetTickCount());
            _previousPollTick = _api.GetTickCount();
            _started = true;
            _ready.Set();
            int result;
            while ((result = _api.ReadAndDispatchMessage(out var messageId)) > 0)
            {
                if (messageId == HookMessages.Rehook)
                {
                    try
                    {
                        Rehook();
                    }
                    catch (Exception)
                    {
                        // 稀な経路の失敗でメッセージループを止めない。止めると全フックが外れて気づかれない。
                    }
                }
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

            if (_mouseHook != 0)
            {
                _cleanupSucceeded &= _api.UnregisterLowLevel(_mouseHook);
            }

            if (_keyboardHook != 0)
            {
                _cleanupSucceeded &= _api.UnregisterLowLevel(_keyboardHook);
            }

            // static 関数ポインターは回収されない。解除後のコールバックも null を見て終了する。
            _current = null;
            _dummyKeys?.Dispose();
            Volatile.Write(ref _completed, 1);
            _wake.Set();
            _ready.Set();
        }
    }

    private static unsafe nint RegisterMouse(IHookApi api) =>
        api.RegisterMouse((nint)(delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)&OnMouse);

    private static unsafe nint RegisterKeyboard(IHookApi api) =>
        api.RegisterKeyboard((nint)(delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)&OnKeyboard);

    /// <summary>
    /// 張り直すかを判断し、必要なら自分のスレッドへ依頼する。タイマーと通知の呼び出し元（フックのスレッド以外）で動く。
    /// </summary>
    internal void CheckHealth()
    {
        if (!_started || Volatile.Read(ref _completed) != 0)
        {
            return;
        }

        // タイマーと通知（別のスレッド）の両方から来る。前回の観測を持つので直列にする。
        lock (_healthGate)
        {
            var now = _api.GetTickCount();
            var keyboard = unchecked((uint)Volatile.Read(ref _lastKeyboardTick));
            var mouse = unchecked((uint)Volatile.Read(ref _lastMouseTick));
            var lastHook = HookTicks.Diff(keyboard, mouse) > 0 ? keyboard : mouse;
            // 最後の入力が分からないときは、疑わない。
            var lastInput = _api.TryGetLastInputTick(out var tick) ? tick : lastHook;

            // 疑いの根拠は、種類ごとの証拠だけにする。OS の最後の入力とコールバックの時刻の食い違いは使わない:
            // 通常の操作中に、OS の入力だけが進んでフックが呼ばれない場面があり、誤検知した（実機の app.log）。
            // キーボード: 繰り返しのあるキーが猶予より長く押されているのに、コールバックが来ない。
            // OS の入力がキーボードのコールバックより新しいときだけ、全キーを調べる（静かな間の無駄なシステムコールを避ける）。
            var keyboardSilent = HookTicks.Diff(now, keyboard) > HookRehookPolicy.InputGraceMs &&
                HookTicks.Diff(lastInput, keyboard) > HookRehookPolicy.InputGraceMs &&
                _api.IsNonModifierKeyDown();
            // マウス: 前回の観測からカーソルが動き、OS も入力を見たのに、その間にコールバックが一度も来ない。
            // SetCursorPos はカーソルを動かしても OS の最後の入力にならないので、それだけでは疑わない。
            var cursorMoved = false;
            if (_api.TryGetCursorPosition(out var x, out var y))
            {
                cursorMoved = _hasCursor && (x != _cursorX || y != _cursorY);
                (_hasCursor, _cursorX, _cursorY) = (true, x, y);
            }

            var mouseSilentNow = cursorMoved && HookTicks.Diff(mouse, _previousPollTick) < 0 &&
                HookTicks.Diff(lastInput, _previousPollTick) > 0;
            // タッチ・ペンがカーソルを動かし OS の入力を進めてもフックを呼ばない、と推測する (実機未確認。実機で 1 回誤検知した)。
            // 本物のマウスが一度でも届けば途切れるので、連続して成り立つときだけ疑う。
            _mouseSilentStreak = mouseSilentNow ? _mouseSilentStreak + 1 : 0;
            var mouseSilent = _mouseSilentStreak >= MouseSilentPolls;
            _previousPollTick = now;

            var gap = HookTicks.Diff(lastInput, lastHook);
            var reason = _policy.Evaluate(now, keyboardSilent || mouseSilent);
            if (reason == HookRehookReason.None)
            {
                return;
            }

            // 調査用。種類ごとの証拠と、各時刻からの経過だけを残す（入力の中身は取らない）。
            var evidence = reason == HookRehookReason.InputMismatch
                ? (keyboardSilent ? HookRehookEvidence.KeyboardSilent : HookRehookEvidence.None) |
                    (mouseSilent ? HookRehookEvidence.MouseSilent : HookRehookEvidence.None)
                : HookRehookEvidence.None;
            _rehookDetail = new HookRehookReport(
                reason, false, 0, evidence, gap, HookTicks.Diff(now, keyboard), HookTicks.Diff(now, mouse),
                HookTicks.Diff(now, lastInput), _policy.BackoffMs);

            // 張り直し後の基準。ここから先にコールバックが来れば、フックは生きている。
            ResetBaseline(now);
            _api.PostRehook(_threadId);
        }
    }

    private void ResetBaseline(uint now)
    {
        Volatile.Write(ref _lastKeyboardTick, unchecked((int)now));
        Volatile.Write(ref _lastMouseTick, unchecked((int)now));
    }

    /// <summary>
    /// 新しいフックを先に登録してから古いものを外す。登録に失敗したら古いものを残す。
    /// 間にメッセージを処理しないので、二つのフックから同じキーが二重に届くことはない（実機未確認）。
    /// </summary>
    private void Rehook()
    {
        // 依頼した側（_healthGate の中）が書いた判断の詳細。構造体なので同じ錠で読む。
        HookRehookReport detail;
        lock (_healthGate)
        {
            detail = _rehookDetail;
        }

        var reason = detail.Reason;
        var mouse = RegisterMouse(_api);
        var keyboard = RegisterKeyboard(_api);
        var succeeded = mouse != 0 && keyboard != 0;
        if (succeeded)
        {
            // OS に外されていた古いハンドルの解除は失敗しうる。結果は見ない（Stop の成否に混ぜない）。
            _api.UnregisterLowLevel(_mouseHook);
            _api.UnregisterLowLevel(_keyboardHook);
            _mouseHook = mouse;
            _keyboardHook = keyboard;
            // 外されていた間の解放を取り逃している。握りつぶしの記録や押下状態を捨てる。
            _matcher.Reset();
        }
        else
        {
            // 片方だけ成功したときは、そのまま残すと二重に届く。成功した分を戻す。
            if (mouse != 0) { _api.UnregisterLowLevel(mouse); }
            if (keyboard != 0) { _api.UnregisterLowLevel(keyboard); }
        }

        var report = detail with { Succeeded = succeeded, Count = Interlocked.Increment(ref _rehookCount) };
        var handler = Rehooked;
        if (handler is not null)
        {
            // 記録でフックのスレッドを止めない。コールバックはこのスレッドで動く。
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { handler(report); }
                catch (Exception)
                {
                    // 記録の失敗でアプリを止めない。
                }
            });
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

    /// <summary>コールバックが呼ばれた時刻を残す。生きているかの判断に使う。書き込みは一度だけで軽い。</summary>
    internal void TouchKeyboard() => Volatile.Write(ref _lastKeyboardTick, unchecked((int)_api.GetTickCount()));

    internal void TouchMouse() => Volatile.Write(ref _lastMouseTick, unchecked((int)_api.GetTickCount()));

    internal void Enqueue(RawHookEvent value)
    {
        if (_buffer.Write(value))
        {
            _wake.Set();
        }
    }

    internal bool ProcessKey(uint virtualKey, bool isDown, bool injected = false)
    {
        if (injected)
        {
            return false;
        }
        var match = _matcher.ProcessKey(virtualKey, isDown);
        if (match.SendDummyKey)
        {
            // SendInput は他の低レベルフックを待ちうるため、ここでは合図だけを渡す。
            _dummyKeys?.Request();
        }
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
        try
        {
            owner.Enqueue(new RawHookEvent(kind, (nint)window.Value, Time: time));
        }
        catch
        {
            // アンマネージドの呼び出し元へ例外を出さない。イベントを一つ落とすだけにとどめる。
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT OnMouse(int code, WPARAM message, LPARAM data)
    {
        var owner = _current;
        owner?.TouchMouse();
        if (code >= 0 && owner is not null && data.Value != 0)
        {
            try
            {
                OnMouseCore(owner, message, data);
            }
            catch
            {
                // アンマネージドの呼び出し元へ例外を出さず、次のフックへ渡す。
            }
        }

        return PInvoke.CallNextHookEx(default(HHOOK), code, message, data);
    }

    private static unsafe void OnMouseCore(HookThread owner, WPARAM message, LPARAM data)
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT OnKeyboard(int code, WPARAM message, LPARAM data)
    {
        var owner = _current;
        owner?.TouchKeyboard();
        if (code >= 0 && owner is not null && data.Value != 0)
        {
            var key = (KBDLLHOOKSTRUCT*)data.Value;
            var messageId = (uint)message.Value;
            if (messageId is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN or PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP)
            {
                try
                {
                    // 自分のダミーキーだけを除く。他のツールが注入したキーは通常の入力として扱う。
                    var own = (key->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0 &&
                        key->dwExtraInfo == Win32DummyKeySender.ExtraInfo;
                    if (owner.ProcessKey(key->vkCode, messageId is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN, own))
                    {
                        return new LRESULT(1);
                    }
                }
                catch
                {
                    // アンマネージドの呼び出し元へ例外を出さず、キーは握りつぶさずに通す。
                }
            }
        }

        return PInvoke.CallNextHookEx(default(HHOOK), code, message, data);
    }
}
