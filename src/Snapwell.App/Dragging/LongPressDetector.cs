using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>押下の世代と物理座標を持つ長押し候補。</summary>
internal sealed record LongPressCandidate(nint Handle, Vec2 Position, long Generation, double Radius);

/// <summary>左ボタンの長押しと、その押下から通常のドラッグへの切り替えを判定する。</summary>
internal sealed class LongPressDetector(IWindowInspector inspector, IMonitorSource monitors,
    Func<nint, Vec2, bool> isCaption, Func<Vec2?> cursorPosition, Action<bool> trackMouse,
    Func<TimeSpan, Action, IDisposable>? schedule = null) : IDisposable
{
    // クリックと区別しつつ操作を待たせすぎない判定時間。アニメーションの継続時間ではない。
    internal const int HoldMilliseconds = 450;
    // 手ぶれを許し、通常のドラッグと区別する論理距離。モニターの DPI で換算する。
    internal const double MovementLogicalPixels = 6;
    private readonly object _gate = new();
    private LongPressCandidate? _press;
    private IDisposable? _timer;
    private long _generation;
    private bool _elapsed;
    private bool _opened;
    private bool _disposed;

    internal event Action<LongPressCandidate>? CandidateReady;

    internal bool HasOpenedLauncher
    {
        get { lock (_gate) { return _opened; } }
    }

    /// <summary>同じ押下から遅れて届いた移動ループ開始を区別する。</summary>
    internal bool OwnsLauncherFor(nint handle)
    {
        lock (_gate)
        {
            return _opened && _press?.Handle == handle;
        }
    }

    /// <summary>移動への切り替えで、長押しが開いたランチャーだけを閉じるよう通知する。</summary>
    internal bool Handle(HookEvent value, bool canStart)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (value is MouseButton { Button: MouseButtonKind.Left } button)
            {
                Reset();
                if (button.IsDown && canStart)
                {
                    var handle = inspector.GetRootWindowAt(button.X, button.Y);
                    var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
                    if (handle != 0 && areas.Length > 0 && inspector.TryInspect(handle, out var window))
                    {
                        var monitor = CommandPlanner.SelectMonitor(window.VisualBounds, areas);
                        var candidate = new LongPressCandidate(handle, new(button.X, button.Y),
                            _generation, MovementLogicalPixels * monitor.Scale);
                        _press = candidate;
                        trackMouse(true);
                        _timer = (schedule ?? Schedule)(TimeSpan.FromMilliseconds(HoldMilliseconds),
                            () => Elapsed(candidate));
                    }
                }
            }
            else if (value is MouseMoved move && _press is { } press &&
                (new Vec2(move.X, move.Y) - press.Position).Length > press.Radius)
            {
                var close = _opened;
                Reset();
                return close;
            }

            return false;
        }
    }

    /// <summary>応答待ちと現在位置の取得を、状態ロックを持たずに行う。</summary>
    internal bool CheckTarget(LongPressCandidate candidate) =>
        isCaption(candidate.Handle, candidate.Position) && cursorPosition() is { } position &&
        (position - candidate.Position).Length <= candidate.Radius;

    /// <summary>検査中に押下が変わっていなければ、一度だけ発火を確定する。</summary>
    internal bool TryActivate(LongPressCandidate candidate)
    {
        lock (_gate)
        {
            if (_disposed || _press != candidate || !_elapsed || _opened)
            {
                return false;
            }

            _opened = true;
            return true;
        }
    }

    /// <summary>一時停止や別操作への切り替えで、配送済みのタイマーも無効にする。</summary>
    internal void Cancel()
    {
        lock (_gate)
        {
            Reset();
        }
    }

    /// <summary>タイマーを止め、あとから到着する候補を無効にする。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Reset();
        }
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
            _timer?.Dispose();
            _timer = null;
        }

        // 受け手は ResidentRuntime のロックを取るため、判定側のロックから外へ通知する。
        CandidateReady?.Invoke(candidate);
    }

    private void Reset()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        if (_press is not null)
        {
            trackMouse(false);
        }

        _press = null;
        _elapsed = false;
        _opened = false;
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
