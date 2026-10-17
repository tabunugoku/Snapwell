using Snapwell.App.Motion;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>システムのドラッグ終了から投げの速度を求め、Core の吸着先へ移動を要求する。</summary>
internal sealed class DragSnapController(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, Func<PortableSettings> settings, Action<bool> trackMouse, uint ownProcessId,
    Func<uint>? tickCount = null)
{
    private readonly object _gate = new();
    private readonly Func<uint> _tickCount = tickCount ?? (() => unchecked((uint)Environment.TickCount64));
    private bool _enabled = true;
    private nint _tracked;
    private VelocityTracker? _velocity;
    private uint _lastTime;
    private double _seconds;

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
                    if (!inspector.TryInspect(start.Handle, out var window) || window.IsMaximized ||
                        WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
                    {
                        return;
                    }

                    motion.Cancel(start.Handle);
                    StopTracking();
                    _tracked = start.Handle;
                    _velocity = new();
                    _lastTime = _tickCount();
                    _seconds = 0;
                    trackMouse(true);
                    break;
                case MouseMoved move when _velocity is not null:
                    _velocity.AddSample(new(move.X, move.Y), Time(move.TimeMs));
                    break;
                case MoveSizeEnded end when _velocity is not null && end.Handle == _tracked:
                    var velocity = _velocity.Release(Time(_tickCount()));
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
                        WindowClassifier.CanResize(current, ownProcessId));
                    if (result is { } snap)
                    {
                        var walls = areas.First(area => area.Id == snap.MonitorId).WorkArea;
                        motion.Request(new(end.Handle, current.VisualBounds, snap.Target, walls,
                            options.ToFeelPreset(), velocity, snap.Resized));
                    }

                    break;
                case MouseButton { IsDown: true } button:
                    var handle = inspector.GetRootWindowAt(button.X, button.Y);
                    if (motion.IsMoving(handle))
                    {
                        motion.Cancel(handle);
                    }

                    break;
            }
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
        if (_velocity is not null)
        {
            trackMouse(false);
        }

        _tracked = 0;
        _velocity = null;
    }
}
