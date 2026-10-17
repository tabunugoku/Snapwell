using Snapwell.App.Hotkeys;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Overlay;

/// <summary>配送とオーバーレイの入力を直列化し、対象と範囲選択を所有する。</summary>
internal sealed class LauncherController : IDisposable
{
    private readonly object _gate = new();
    private readonly IWindowInspector _inspector;
    private readonly IMonitorSource _monitors;
    private readonly Func<PortableSettings> _settings;
    private readonly IOverlayPresenter _presenter;
    private readonly Action<nint, PixelRect, PixelRect> _place;
    private readonly Action<IReadOnlyList<HotkeyBinding>> _applyBindings;
    private readonly IReadOnlyList<HotkeyBinding> _normalBindings;
    private readonly IReadOnlyList<HotkeyBinding> _pausedBindings;
    private readonly IReadOnlyList<HotkeyBinding> _launcherBindings;
    private readonly uint _ownProcessId;
    private ZoneSelection? _selection;
    private nint _target;
    private bool _mouseSelecting;
    private bool _enabled = true;
    private bool _disposed;

    internal LauncherController(IWindowInspector inspector, IMonitorSource monitors, Func<PortableSettings> settings,
        IOverlayPresenter presenter, Action<nint, PixelRect, PixelRect> place,
        Action<IReadOnlyList<HotkeyBinding>> applyBindings, IReadOnlyList<HotkeyBinding> normalBindings,
        uint ownProcessId)
    {
        _inspector = inspector;
        _monitors = monitors;
        _settings = settings;
        _presenter = presenter;
        _place = place;
        _applyBindings = applyBindings;
        _normalBindings = normalBindings;
        _pausedBindings = HotkeyBindingsBuilder.Paused(normalBindings);
        _launcherBindings = HotkeyBindingsBuilder.Launcher(normalBindings);
        _ownProcessId = ownProcessId;
        presenter.Pointer += HandlePointer;
        presenter.Faulted += Close;
    }

    internal bool IsOpen
    {
        get { lock (_gate) { return _selection is not null; } }
    }

    internal ZoneSelection? Selection
    {
        get { lock (_gate) { return _selection; } }
    }

    internal void Open() => Open(_inspector.GetForegroundWindow());

    /// <summary>指定したウィンドウを対象に区画を選ぶ。</summary>
    internal void Open(nint handle)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || _selection is not null)
            {
                return;
            }

            if (handle == 0 || !_inspector.TryInspect(handle, out var window) ||
                WindowClassifier.Classify(window, _ownProcessId) == WindowKind.Excluded)
            {
                return;
            }

            var areas = _monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
            if (areas.Length == 0)
            {
                return;
            }

            var monitor = CommandPlanner.SelectMonitor(window.VisualBounds, areas);
            var options = _settings();
            try
            {
                _selection = new(monitor.WorkArea, options.ToGridPlan(), options.ToSpacing().ToPhysical(monitor.Scale));
            }
            catch (ArgumentException)
            {
                // 間隔と区画数に対して小さすぎる作業領域では開かない。
                return;
            }

            _target = handle;
            _mouseSelecting = false;
            _applyBindings(_launcherBindings);
            Draw();
        }
    }

    internal bool Handle(KeyChord chord)
    {
        if (!HotkeyBindingsBuilder.IsLauncherInput(chord.Id))
        {
            return false;
        }

        (nint Handle, PixelRect Bounds, PixelRect Area)? placement = null;
        lock (_gate)
        {
            if (!ValidateTargetCore())
            {
                return true;
            }

            var selection = _selection!;
            var input = (LauncherInput)chord.Id;
            switch (input)
            {
                case LauncherInput.Accept:
                    placement = (_target, selection.Bounds, selection.WorkArea);
                    CloseCore();
                    break;
                case LauncherInput.Cancel:
                    CloseCore();
                    break;
                default:
                    var dx = input is LauncherInput.Left or LauncherInput.ExtendLeft ? -1 :
                        input is LauncherInput.Right or LauncherInput.ExtendRight ? 1 : 0;
                    var dy = input is LauncherInput.Up or LauncherInput.ExtendUp ? -1 :
                        input is LauncherInput.Down or LauncherInput.ExtendDown ? 1 : 0;
                    var extend = input is >= LauncherInput.ExtendLeft and <= LauncherInput.ExtendDown;
                    _selection = selection.Move(dx, dy, extend);
                    _mouseSelecting = false;
                    Draw();
                    break;
            }
        }

        if (placement is { } request)
        {
            // 復元と移動はこのロックを保持せず、ホットキーと同じ実行経路へ渡す。
            _place(request.Handle, request.Bounds, request.Area);
        }

        return true;
    }

    internal void HandleGlobalPointer(MouseButton button)
    {
        lock (_gate)
        {
            if (ValidateTargetCore() && button.IsDown && (button.Button == MouseButtonKind.Right ||
                !_selection!.WorkArea.Contains(new Vec2(button.X, button.Y))))
            {
                CloseCore();
            }
        }
    }

    internal void ValidateTarget()
    {
        lock (_gate)
        {
            ValidateTargetCore();
        }
    }

    internal void HandleForegroundChanged(ForegroundChanged foreground)
    {
        lock (_gate)
        {
            if (_disposed || _selection is null || foreground.Handle == _target)
            {
                return;
            }

            // NOACTIVATE の前提が崩れても自分の表示では閉じない（実機未確認）。
            if (foreground.Handle != 0 && _inspector.TryInspect(foreground.Handle, out var window) &&
                window.ProcessId == _ownProcessId)
            {
                return;
            }

            CloseCore();
        }
    }

    internal void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = enabled;
            if (!enabled)
            {
                CloseCore();
            }

            ApplyCurrentBindings();
        }
    }

    internal void Close()
    {
        lock (_gate)
        {
            CloseCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CloseCore();
            _disposed = true;
            _presenter.Pointer -= HandlePointer;
            _presenter.Faulted -= Close;
        }
    }

    private void HandlePointer(OverlayPointer pointer)
    {
        lock (_gate)
        {
            if (!ValidateTargetCore())
            {
                return;
            }

            var selection = _selection!;
            var cell = selection.HitTest(pointer.Position);
            if (pointer.Kind == OverlayPointerKind.RightDown ||
                (pointer.Kind == OverlayPointerKind.Down && cell is null))
            {
                CloseCore();
            }
            else if (pointer.Kind == OverlayPointerKind.Down && cell is { } start)
            {
                _selection = selection.Select(start, start);
                _mouseSelecting = true;
                Draw();
            }
            else if (_mouseSelecting && pointer.Kind is OverlayPointerKind.Move or OverlayPointerKind.Up)
            {
                // capture 中の領域外は最寄りの端に留める。
                var area = selection.WorkArea;
                cell ??= selection.HitTest(new(Math.Clamp(pointer.Position.X, area.X, area.Right - 1),
                    Math.Clamp(pointer.Position.Y, area.Y, area.Bottom - 1)));
                _selection = selection.Select(selection.Anchor, cell!.Value);
                _mouseSelecting = pointer.Kind != OverlayPointerKind.Up;
                Draw();
            }
        }
    }

    private bool ValidateTargetCore()
    {
        if (_disposed || _selection is null)
        {
            return false;
        }

        if (!_inspector.TryInspect(_target, out var window) ||
            WindowClassifier.Classify(window, _ownProcessId) == WindowKind.Excluded)
        {
            CloseCore();
            return false;
        }

        return true;
    }

    private void Draw()
    {
        var selection = _selection!;
        _presenter.Request(selection.WorkArea, () => OverlayPainter.Launcher(selection));
    }

    private void CloseCore()
    {
        if (_selection is null)
        {
            return;
        }

        _selection = null;
        _target = 0;
        _mouseSelecting = false;
        _presenter.Hide();
        ApplyCurrentBindings();
    }

    private void ApplyCurrentBindings() =>
        _applyBindings(_selection is not null ? _launcherBindings : _enabled ? _normalBindings : _pausedBindings);
}
