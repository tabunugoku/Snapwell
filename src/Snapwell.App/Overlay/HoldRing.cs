using System.Diagnostics;
using Snapwell.App.Dragging;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Overlay;

/// <summary>押下通知を描画ワーカーへ渡し、非対話の表示先へリングを送る。</summary>
internal sealed class HoldRing : IDisposable
{
    private readonly LongPressDetector _detector;
    private readonly IOverlayPresenter _presenter;
    private readonly IMonitorSource _monitors;
    private readonly Func<OverlayTheme> _theme;
    private readonly Func<double> _milliseconds;
    private readonly IDisposable _ticker;
    private readonly object _renderGate = new();
    private Request? _request;
    private Request? _rendered;
    private (PixelRect Bounds, double Scale, OverlayTheme Theme)? _scene;
    private int _disposed;

    internal HoldRing(LongPressDetector detector, IOverlayPresenter presenter, IMonitorSource monitors,
        Func<OverlayTheme>? theme = null, Func<double>? milliseconds = null,
        Func<Action, IDisposable>? startTicker = null)
    {
        _detector = detector;
        _presenter = presenter;
        _monitors = monitors;
        _theme = theme ?? OverlayThemeSource.Read;
        _milliseconds = milliseconds ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        // 60 Hz は描画の周期だけを決める。進み具合は実際の経過時間から求める。
        _ticker = startTicker?.Invoke(Render) ?? new Timer(_ => Render(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60));
        detector.HoldStarted += Start;
        detector.HoldEnded += Hide;
    }

    private void Start(LongPressCandidate candidate, int holdMilliseconds)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Interlocked.Exchange(ref _request, new(candidate, _milliseconds(), holdMilliseconds));
        }
    }

    private void Hide() => Interlocked.Exchange(ref _request, null);

    private void Render()
    {
        // タイマーの再入は描画側だけで直列化し、入力通知はこのロックを取らない。
        lock (_renderGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var request = Volatile.Read(ref _request);
            try
            {
                if (!ReferenceEquals(request, _rendered))
                {
                    _presenter.Hide();
                    _rendered = request;
                    _scene = null;
                    if (request is not null && _detector.CheckTarget(request.Candidate))
                    {
                        _scene = CreateScene(request.Candidate.Position);
                    }
                }

                if (request is not null && _scene is { } scene &&
                    ReferenceEquals(request, Volatile.Read(ref _request)))
                {
                    var progress = Math.Clamp((_milliseconds() - request.Started) / request.Duration, 0, 1);
                    _presenter.Request(scene.Bounds, () => HoldRingPainter.Paint(scene.Bounds,
                        request.Candidate.Position, progress, scene.Theme, scene.Scale));
                }
            }
            catch (Exception)
            {
                // モニターや表示先の失敗で入力配送と終了時の復元を止めない。
                _scene = null;
                _presenter.Hide();
            }
        }
    }

    private (PixelRect Bounds, double Scale, OverlayTheme Theme)? CreateScene(Vec2 center)
    {
        var monitor = _monitors.GetMonitors().FirstOrDefault(value => value.Bounds.Contains(center));
        if (monitor is null)
        {
            return null;
        }

        var scale = monitor.DpiX / 96.0;
        var half = HoldRingPainter.LogicalSize * scale / 2;
        var area = monitor.WorkArea;
        var left = Math.Max(area.X, (int)Math.Floor(center.X - half));
        var top = Math.Max(area.Y, (int)Math.Floor(center.Y - half));
        var right = Math.Min(area.Right, (int)Math.Ceiling(center.X + half));
        var bottom = Math.Min(area.Bottom, (int)Math.Ceiling(center.Y + half));
        return right > left && bottom > top
            ? (new(left, top, right - left, bottom - top), scale, _theme()) : null;
    }

    /// <summary>通知を外し、描画の完了後に表示を消す。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _detector.HoldStarted -= Start;
        _detector.HoldEnded -= Hide;
        _ticker.Dispose();
        lock (_renderGate)
        {
            _request = null;
            _scene = null;
            _presenter.Hide();
        }
    }

    private sealed record Request(LongPressCandidate Candidate, double Started, int Duration);
}
