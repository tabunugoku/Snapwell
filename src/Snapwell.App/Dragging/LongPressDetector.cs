using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Input;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>押下の世代と物理座標を持つ長押し候補。</summary>
internal sealed record LongPressCandidate(nint Handle, Vec2 Position, long Generation, double Radius,
    bool InTopBand = false);

/// <summary>左ボタンの長押しと、その押下から通常のドラッグへの切り替えを判定する。</summary>
internal sealed class LongPressDetector(IWindowInspector inspector, IMonitorSource monitors,
    Func<nint, Vec2, bool> isCaption, Func<Vec2?> cursorPosition, Action<bool> trackMouse,
    Func<TimeSpan, Action, IDisposable>? schedule = null,
    Func<PortableSettings>? settings = null) : IDisposable
{
    // クリックと区別しつつ操作を待たせすぎない判定時間。アニメーションの継続時間ではない。
    internal const int HoldMilliseconds = 450;
    // 手ぶれを許し、通常のドラッグと区別する論理距離。モニターの DPI で換算する。
    internal const double MovementLogicalPixels = 6;
    private readonly object _gate = new();
    private readonly List<Action> _notifications = [];
    private LongPressCandidate? _press;
    private LongPressCandidate? _launcherOwner;
    private nint? _consumedMoveSizeHandle;
    private IDisposable? _timer;
    private long _generation;
    private bool _elapsed;
    private bool _moveSizeEnded;
    private bool _disposed;

    internal event Action<LongPressCandidate>? CandidateReady;
    internal event Action<LongPressCandidate, int>? HoldStarted;
    internal event Action? HoldEnded;

    internal bool HasOpenedLauncher
    {
        get { lock (_gate) { return _launcherOwner is not null; } }
    }

    /// <summary>表示を開いた世代の左ボタンがまだ解放されていないかを返す。</summary>
    internal bool IsHoldingLauncher
    {
        get { lock (_gate) { return _launcherOwner is { } owner && _press?.Generation == owner.Generation; } }
    }

    /// <summary>同じ押下から遅れて届いた移動ループ開始を区別する。</summary>
    internal bool OwnsLauncherFor(nint handle)
    {
        lock (_gate)
        {
            return _launcherOwner?.Handle == handle;
        }
    }

    /// <summary>表示を閉じたあとも、長押しが使った移動ループの通知を区別する。</summary>
    internal bool OwnsMoveSizeFor(nint handle)
    {
        lock (_gate)
        {
            return _consumedMoveSizeHandle == handle;
        }
    }

    /// <summary>移動への切り替えで、長押しが開いたランチャーだけを閉じるよう通知する。</summary>
    internal bool Handle(HookEvent value, bool canStart)
    {
        var close = HandleCore(value, canStart);
        Flush();
        return close;
    }

    private bool HandleCore(HookEvent value, bool canStart)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (value is MouseButton { Button: MouseButtonKind.Left } button)
            {
                if (!button.IsDown)
                {
                    // 解放後に移動開始が遅配される可能性があるため、移動終了までは所有を残す（実機未確認）。
                    ResetPress();
                    if (_moveSizeEnded)
                    {
                        ClearOwnership();
                    }

                    return false;
                }

                Reset();
                if (canStart)
                {
                    var handle = inspector.GetRootWindowAt(button.X, button.Y);
                    var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
                    if (handle != 0 && areas.Length > 0 && inspector.TryInspect(handle, out var window))
                    {
                        var current = settings?.Invoke();
                        var holdMilliseconds = current?.LongPressMilliseconds is >= 150 and <= 2000
                            ? current.LongPressMilliseconds
                            : HoldMilliseconds;
                        var movementLogicalPixels = current is { LongPressDistance: var distance } &&
                            double.IsFinite(distance) && distance is >= 2 and <= 32
                            ? distance
                            : MovementLogicalPixels;
                        var monitor = CommandPlanner.SelectMonitor(window.VisualBounds, areas);
                        var candidate = new LongPressCandidate(handle, new(button.X, button.Y),
                            _generation, movementLogicalPixels * monitor.Scale,
                            TopBandRule.Contains(window.VisualBounds, monitor.WorkArea,
                                new(button.X, button.Y), monitor.Scale));
                        _press = candidate;
                        _notifications.Add(() => HoldStarted?.Invoke(candidate, holdMilliseconds));
                        trackMouse(true);
                        _timer = (schedule ?? Schedule)(TimeSpan.FromMilliseconds(holdMilliseconds),
                            () => Elapsed(candidate));
                    }
                }
            }
            else if (value is MouseMoved move && _press is { } press &&
                (new Vec2(move.X, move.Y) - press.Position).Length > press.Radius)
            {
                var close = _launcherOwner is not null;
                ResetLauncher();
                return close;
            }
            else if (value is MoveSizeEnded end)
            {
                if (_consumedMoveSizeHandle == end.Handle)
                {
                    _consumedMoveSizeHandle = null;
                }

                if (_launcherOwner?.Handle == end.Handle)
                {
                    _moveSizeEnded = true;
                    if (_press is null)
                    {
                        ClearOwnership();
                    }
                }
            }

            return false;
        }
    }

    /// <summary>応答待ちと現在位置の取得を、状態ロックを持たずに行う。</summary>
    internal bool CheckTarget(LongPressCandidate candidate) =>
        (isCaption(candidate.Handle, candidate.Position) || candidate.InTopBand) &&
        cursorPosition() is { } position &&
        (position - candidate.Position).Length <= candidate.Radius;

    /// <summary>検査中に押下が変わっていなければ、一度だけ発火を確定する。</summary>
    internal bool TryActivate(LongPressCandidate candidate)
    {
        lock (_gate)
        {
            if (_disposed || _press != candidate || !_elapsed || _launcherOwner is not null)
            {
                return false;
            }

            _launcherOwner = candidate;
            _consumedMoveSizeHandle = candidate.Handle;
            return true;
        }
    }

    /// <summary>表示の所有だけを破棄し、確定した配置を守るため移動ループの記録を残す。</summary>
    internal void CancelLauncher()
    {
        lock (_gate)
        {
            ResetLauncher();
        }

        Flush();
    }

    /// <summary>一時停止や別操作への切り替えで、配送済みのタイマーも無効にする。</summary>
    internal void Cancel()
    {
        lock (_gate)
        {
            Reset();
        }

        Flush();
    }

    /// <summary>タイマーを止め、あとから到着する候補を無効にする。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Reset();
        }

        Flush();
    }

    private void Elapsed(LongPressCandidate candidate)
    {
        lock (_gate)
        {
            if (_disposed || _press != candidate || _elapsed)
            {
                return;
            }

            _elapsed = true;
            _notifications.Add(() => HoldEnded?.Invoke());
            _timer?.Dispose();
            _timer = null;
        }

        // 受け手は ResidentRuntime のロックを取るため、判定側のロックから外へ通知する。
        Flush();
        CandidateReady?.Invoke(candidate);
    }

    private void Reset()
    {
        ResetLauncher();
        _consumedMoveSizeHandle = null;
    }

    private void ResetLauncher()
    {
        ResetPress();
        ClearOwnership();
    }

    private void ClearOwnership()
    {
        _launcherOwner = null;
        _moveSizeEnded = false;
    }

    // 購読者が別のロックを取っても逆順にならないよう、通知は状態ロックを離してから届ける。
    private void Flush()
    {
        Action[] pending;
        lock (_gate)
        {
            pending = [.. _notifications];
            _notifications.Clear();
        }

        foreach (var notify in pending)
        {
            notify();
        }
    }

    private void ResetPress()
    {
        _notifications.Add(() => HoldEnded?.Invoke());
        _generation++;
        _timer?.Dispose();
        _timer = null;
        if (_press is not null)
        {
            trackMouse(false);
        }

        _press = null;
        _elapsed = false;
    }

    private static IDisposable Schedule(TimeSpan due, Action callback) =>
        new Timer(_ => callback(), null, due, Timeout.InfiniteTimeSpan);
}

/// <summary>WM_NCHITTEST の符号付き画面座標を組み立てる。</summary>
internal static class CaptionHitTestCoordinates
{
    /// <summary>負の座標を含む各成分の下位 16 bit を lParam に詰める。</summary>
    internal static nint Pack(Vec2 point) =>
        unchecked((nint)(int)((uint)(ushort)(int)point.X | ((uint)(ushort)(int)point.Y << 16)));
}
