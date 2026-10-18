using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>システムのドラッグ終了から投げの速度を求め、Core の吸着先へ移動を要求する。</summary>
internal sealed class DragSnapController(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, Func<PortableSettings> settings, Action<bool> trackMouse, uint ownProcessId,
    Func<uint>? tickCount = null, IZonePreview? preview = null, Func<Vec2>? cursorPosition = null,
    Func<nint, IReadOnlyList<PixelRect>>? neighbors = null)
{
    // 120 Hz の全イベントで予測せず、16 px または約二フレームごとに更新する。
    private const double PredictionDistance = 16;
    private const double PredictionInterval = 0.032;
    private readonly object _gate = new();
    private readonly IZonePreview _preview = preview ?? NullZonePreview.Instance;
    private readonly Func<uint> _tickCount = tickCount ?? (() => unchecked((uint)Environment.TickCount64));
    private bool _enabled = true;
    private nint _tracked;
    private VelocityTracker? _velocity;
    private uint _lastTime;
    private double _seconds;
    private PixelRect _startBounds;
    private Vec2? _pointer;
    private Vec2? _dragOrigin;
    private Vec2? _lastPredictionPosition;
    private double _lastPredictionTime;
    private bool _canResize;
    private IReadOnlyList<PixelRect> _neighbors = [];

    internal bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value)
                {
                    StopTracking();
                }
            }
        }
    }

    internal void Handle(HookEvent value)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            switch (value)
            {
                case MoveSizeStarted start:
                    StopTracking();
                    if (!inspector.TryInspect(start.Handle, out var window) || window.IsMaximized ||
                        WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
                    {
                        return;
                    }

                    motion.Cancel(start.Handle);
                    _tracked = start.Handle;
                    _neighbors = neighbors?.Invoke(start.Handle).ToArray() ?? [];
                    _startBounds = window.VisualBounds;
                    _canResize = WindowClassifier.CanResize(window, ownProcessId);
                    _dragOrigin = cursorPosition?.Invoke() ?? _pointer;
                    _lastPredictionPosition = null;
                    _velocity = new();
                    _lastTime = _tickCount();
                    _seconds = 0;
                    trackMouse(true);
                    break;
                case MouseMoved move when _velocity is not null:
                    var point = new Vec2(move.X, move.Y);
                    var seconds = Time(move.TimeMs);
                    _dragOrigin ??= point;
                    _pointer = point;
                    _velocity.AddSample(point, seconds);
                    UpdatePreview(point, seconds);
                    break;
                case MoveSizeEnded end when _velocity is not null && end.Handle == _tracked:
                    // dwmsEventTime と MSLLHOOKSTRUCT.time は同じ GetTickCount の時計とみなす。実機未確認。
                    var velocity = _velocity.Release(Time(end.TimeMs));
                    var pointer = _pointer ?? cursorPosition?.Invoke();
                    var dragNeighbors = _neighbors;
                    StopTracking();
                    if (!inspector.TryInspect(end.Handle, out var current) || current.IsMaximized ||
                        WindowClassifier.Classify(current, ownProcessId) == WindowKind.Excluded)
                    {
                        return;
                    }

                    var options = settings();
                    var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
                    var result = SnapResolver.Resolve(current.VisualBounds, velocity, options.ToFeelPreset(),
                        options.SnapMode, options.ToSpacing(), areas,
                        WindowClassifier.CanResize(current, ownProcessId), pointer, dragNeighbors, options.SnapRadius);
                    if (result is { } snap)
                    {
                        var walls = areas.First(area => area.Id == snap.MonitorId).WorkArea;
                        motion.Request(new(end.Handle, current.VisualBounds, snap.Target, walls,
                            options.ToFeelPreset(), velocity, snap.Resized, snap.Stops, snap.ClickRadius));
                    }

                    break;
                case MouseButton { IsDown: true } button:
                    _pointer = new(button.X, button.Y);
                    var handle = inspector.GetRootWindowAt(button.X, button.Y);
                    if (motion.IsMoving(handle))
                    {
                        motion.Cancel(handle);
                    }

                    break;
            }
        }
    }

    private void UpdatePreview(Vec2 point, double seconds)
    {
        var options = settings();
        if (!options.ShowZonePreview)
        {
            _preview.Hide();
            return;
        }

        if (_lastPredictionPosition is { } last && (point - last).Length < PredictionDistance &&
            seconds - _lastPredictionTime < PredictionInterval)
        {
            return;
        }

        _lastPredictionPosition = point;
        _lastPredictionTime = seconds;
        var offset = point - _dragOrigin!.Value;
        var current = new PixelRect(checked(_startBounds.X + (int)offset.X),
            checked(_startBounds.Y + (int)offset.Y), _startBounds.Width, _startBounds.Height);
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        var result = SnapResolver.Resolve(current, _velocity!.Release(seconds), options.ToFeelPreset(),
            options.SnapMode, options.ToSpacing(), areas, _canResize, point, _neighbors, options.SnapRadius);
        if (result is { } snap)
        {
            _preview.Show(snap.Target);
        }
        else
        {
            _preview.Hide();
        }
    }

    private double Time(uint raw)
    {
        var delta = unchecked(raw - _lastTime);
        // uint の周回は正の差分にし、遅れて届いた古いイベントは時刻を戻さない。
        if (delta <= int.MaxValue)
        {
            _seconds += delta / 1000.0;
            _lastTime = raw;
        }

        return _seconds;
    }

    private void StopTracking()
    {
        _preview.Hide();
        if (_velocity is not null)
        {
            trackMouse(false);
        }

        _tracked = 0;
        _velocity = null;
        _dragOrigin = null;
        _lastPredictionPosition = null;
        _neighbors = [];
    }
}
