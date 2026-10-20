using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>システムのドラッグ終了から投げの速度を求め、Core の吸着先へ移動を要求する。</summary>
internal sealed class DragSnapController(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, Func<PortableSettings> settings, Action<bool> trackMouse, uint ownProcessId,
    Func<uint>? tickCount = null, IZonePreview? preview = null, Func<Vec2?>? cursorPosition = null,
    Func<nint, IReadOnlyList<PixelRect>>? neighbors = null)
{
    // 離したあとは、着地の動きが終わるまでプレビューを残す。動きの開始直後と、止まらない場合の上限を決めておく。
    private const int HoldPollMs = 50;
    private const int HoldMinMs = 150;
    private const int HoldMaxMs = 2000;
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
    private IReadOnlyList<PixelRect> _neighbors = [];
    private Timer? _holdTimer;
    private nint _holdHandle;
    private long _holdStartTick;

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
                        !IsTarget(WindowClassifier.Classify(window, ownProcessId)))
                    {
                        return;
                    }

                    motion.Cancel(start.Handle);
                    _tracked = start.Handle;
                    _neighbors = neighbors?.Invoke(start.Handle).ToArray() ?? [];
                    _startBounds = window.VisualBounds;
                    _velocity = new();
                    _lastTime = _tickCount();
                    _seconds = 0;
                    trackMouse(true);
                    break;
                case MouseMoved move when _velocity is not null:
                    var point = new Vec2(move.X, move.Y);
                    var seconds = Time(move.TimeMs);
                    _pointer = point;
                    _velocity.AddSample(point, seconds);
                    break;
                case MoveSizeEnded end when _velocity is not null && end.Handle == _tracked:
                    // dwmsEventTime と MSLLHOOKSTRUCT.time は同じ GetTickCount の時計とみなす。実機未確認。
                    var velocity = _velocity.Release(Time(end.TimeMs));
                    var pointer = _pointer ?? cursorPosition?.Invoke();
                    var dragNeighbors = _neighbors;
                    var startSize = (_startBounds.Width, _startBounds.Height);
                    // 吸着が決まれば、着地するまで同じプレビューを出し続けるので、ここでは消さない。
                    StopTracking(keepPreview: true);
                    if (!inspector.TryInspect(end.Handle, out var current) || current.IsMaximized ||
                        !IsTarget(WindowClassifier.Classify(current, ownProcessId)))
                    {
                        _preview.Hide();
                        return;
                    }

                    // リサイズの移動ループも同じイベントで届く。利用者が決めたサイズを吸着で上書きしない。
                    if ((current.VisualBounds.Width, current.VisualBounds.Height) != startSize)
                    {
                        _preview.Hide();
                        return;
                    }

                    var options = settings();
                    var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
                    var result = MonitorGridResolver.ResolveSnap(options, current.VisualBounds, velocity, areas,
                        WindowClassifier.CanResize(current, ownProcessId), pointer, dragNeighbors);
                    if (result is { } snap)
                    {
                        var walls = areas.First(area => area.Id == snap.MonitorId).WorkArea;
                        motion.Request(new(end.Handle, current.VisualBounds, snap.Target, walls,
                            options.ToFeelPreset(), velocity, snap.Resized, snap.Stops, snap.ClickRadius));
                        HoldPreview(end.Handle, snap, current.VisualBounds, areas, options.ShowZonePreview);
                    }
                    else
                    {
                        _preview.Hide();
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

    // 設定は呼び出しごとに読み、変更を次のドラッグから反映する。サイズ固定・ポップアップを外すと吸着も予測線も出ない。
    private bool IsTarget(WindowKind kind) => WindowTargetPolicy.IsTarget(kind, settings);

    // 線の終点は慣性だけで進む位置（SnapResult.PredictedPosition）ではなく、着地する区画のタイトルバー。
    // 慣性の距離は速度 × m/c で、既定の感触では 1000 px/s でも 20〜80 px しかなく、線が極端に短くなる。
    private void ShowSnap(SnapResult snap, PixelRect current, IReadOnlyList<MonitorArea> areas)
    {
        var scale = areas.First(area => area.Id == snap.MonitorId).Scale;
        _preview.Show(snap.Target, new Vec2(current.X + current.Width / 2.0, current.Y + 15 * scale),
            new Vec2(snap.Target.X + snap.Target.Width / 2.0, snap.Target.Y + 15 * scale));
    }

    // ドラッグ中は何も出さず、離した瞬間の吸着先を着地の動きが終わるまで出し続ける。
    // 動かしている間の予測は、止めても消えない・投げると一瞬しか出ない、が実機で判断しづらかったため採らない。
    private void HoldPreview(nint handle, SnapResult snap, PixelRect current, IReadOnlyList<MonitorArea> areas,
        bool enabled)
    {
        if (!enabled)
        {
            _preview.Hide();
            return;
        }

        ShowSnap(snap, current, areas);
        _holdHandle = handle;
        _holdStartTick = Environment.TickCount64;
        (_holdTimer ??= new(OnHold)).Change(HoldPollMs, HoldPollMs);
    }

    private void OnHold(object? state)
    {
        lock (_gate)
        {
            if (_holdHandle == 0)
            {
                return;
            }

            var elapsed = Environment.TickCount64 - _holdStartTick;
            if (elapsed >= HoldMaxMs || (elapsed >= HoldMinMs && !motion.IsMoving(_holdHandle)))
            {
                _holdHandle = 0;
                _holdTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _preview.Hide();
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

    private void StopTracking(bool keepPreview = false)
    {
        if (!keepPreview)
        {
            _holdHandle = 0;
            _holdTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _preview.Hide();
        }

        if (_velocity is not null)
        {
            trackMouse(false);
        }

        _tracked = 0;
        _velocity = null;
        _neighbors = [];
    }
}
