using System.Globalization;
using Snapwell.App.Hotkeys;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Core.Tray;
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
    private readonly Func<OverlayTheme> _theme;
    private double _scale = 1;
    private PhysicalSpacing _spacing;
    private string _targetTitle = "";
    private LauncherPanelLayout? _panelLayout;
    private bool _planMenuOpen;
    private long _drawRevision;
    private ZoneSelection? _selection;
    private nint _target;
    private uint? _ignorePointerUpToMs;
    private bool _mouseSelecting;
    private bool _enabled = true;
    private bool _disposed;

    internal LauncherController(IWindowInspector inspector, IMonitorSource monitors, Func<PortableSettings> settings,
        IOverlayPresenter presenter, Action<nint, PixelRect, PixelRect> place,
        Action<IReadOnlyList<HotkeyBinding>> applyBindings, IReadOnlyList<HotkeyBinding> normalBindings,
        uint ownProcessId, Func<OverlayTheme>? theme = null)
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
        _theme = theme ?? OverlayThemeSource.Read;
        presenter.Pointer += HandlePointer;
        presenter.Faulted += CloseIfUnavailable;
    }

    internal bool IsOpen
    {
        get { lock (_gate) { return _selection is not null; } }
    }

    internal ZoneSelection? Selection
    {
        get { lock (_gate) { return _selection; } }
    }

    internal LauncherPanelLayout? PanelLayout
    {
        get { lock (_gate) { return _panelLayout; } }
    }

    internal void Open() => Open(_inspector.GetForegroundWindow());

    /// <summary>指定したウィンドウを対象に区画を選ぶ。</summary>
    internal void Open(nint handle, uint? ignorePointerUpToMs = null)
    {
        lock (_gate)
        {
            if (_selection is not null && !_presenter.IsAvailable)
            {
                CloseCore();
            }

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
                _spacing = options.ToSpacing().ToPhysical(monitor.Scale);
                _selection = new(monitor.WorkArea, options.ToGridPlan(), _spacing);
            }
            catch (ArgumentException)
            {
                // 間隔と区画数に対して小さすぎる作業領域では開かない。
                return;
            }

            _target = handle;
            _ignorePointerUpToMs = ignorePointerUpToMs;
            _targetTitle = DisplayTitle(window.Title);
            _scale = monitor.Scale;
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
            if (_ignorePointerUpToMs is { } referenceMs && PointerOrdering.IsAtOrBefore(button.TimeMs, referenceMs))
            {
                return;
            }

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
            _presenter.Faulted -= CloseIfUnavailable;
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
            if (pointer.Kind == OverlayPointerKind.RightDown)
            {
                CloseCore();
                return;
            }

            var panel = _panelLayout!;
            if (pointer.Kind == OverlayPointerKind.Down)
            {
                _mouseSelecting = false;
                if (panel.HitOption(pointer.Position) is { } option)
                {
                    ChangePlan(LauncherPanelLayout.Plans[option]);
                    return;
                }

                if (panel.HitPlan(pointer.Position))
                {
                    _planMenuOpen = !_planMenuOpen;
                    _panelLayout = null;
                    Draw();
                    return;
                }

                if (_planMenuOpen)
                {
                    _planMenuOpen = false;
                    _panelLayout = null;
                    Draw();
                    return;
                }

                if (panel.HitPreset(pointer.Position) is { } preset)
                {
                    if (LauncherPreset.All[preset].RangeFor(selection.Grid.Plan) is { } range)
                    {
                        _selection = selection.Select(new(range.Column, range.Row),
                            new(range.Column + range.Columns - 1, range.Row + range.Rows - 1));
                        Draw();
                    }

                    return;
                }

                if (panel.Panel.Contains(pointer.Position))
                {
                    return;
                }

                if (cell is null)
                {
                    CloseCore();
                    return;
                }
            }

            if (pointer.Kind == OverlayPointerKind.Down && cell is { } start)
            {
                _selection = selection.Select(start, start);
                _mouseSelecting = true;
                Draw();
            }
            else if (_mouseSelecting && pointer.Kind is OverlayPointerKind.Move or OverlayPointerKind.Up)
            {
                if (panel.Panel.Contains(pointer.Position) ||
                    panel.Options.Any(rect => rect.Contains(pointer.Position)))
                {
                    _mouseSelecting = pointer.Kind != OverlayPointerKind.Up;
                    return;
                }

                // capture 中の領域外は最寄りの端に留める。
                var area = selection.WorkArea;
                cell ??= selection.HitTest(new(Math.Clamp(pointer.Position.X, area.X, area.Right - 1),
                    Math.Clamp(pointer.Position.Y, area.Y, area.Bottom - 1)));
                var updated = selection.Select(selection.Anchor, cell!.Value);
                _mouseSelecting = pointer.Kind != OverlayPointerKind.Up;
                if (updated != selection)
                {
                    _selection = updated;
                    Draw();
                }
            }
        }
    }

    private void ChangePlan(GridPlan plan)
    {
        var old = _selection!;
        try
        {
            var selection = new ZoneSelection(old.WorkArea, plan, _spacing);
            var snapped = selection.Grid.SnapToGrid(old.Bounds);
            var anchor = selection.HitTest(new(snapped.X, snapped.Y))
                ?? throw new ArgumentException("区画に当たらない。");
            var cursor = selection.HitTest(new(snapped.Right - 1, snapped.Bottom - 1))
                ?? throw new ArgumentException("区画に当たらない。");
            _selection = selection.Select(anchor, cursor);
        }
        catch (ArgumentException)
        {
            // 分割数と間隔が収まらない候補では、現在のグリッドを保つ。
        }

        _planMenuOpen = false;
        _panelLayout = null;
        Draw();
    }

    private bool ValidateTargetCore()
    {
        if (_disposed || _selection is null)
        {
            return false;
        }

        if (!_presenter.IsAvailable || !_inspector.TryInspect(_target, out var window) ||
            WindowClassifier.Classify(window, _ownProcessId) == WindowKind.Excluded)
        {
            CloseCore();
            return false;
        }

        _targetTitle = DisplayTitle(window.Title);
        return true;
    }

    private void Draw()
    {
        var selection = _selection!;
        var scale = _scale;
        var title = _targetTitle;
        var menuOpen = _planMenuOpen;
        var revision = ++_drawRevision;
        _panelLayout ??= LauncherPanelLayout.Create(selection, scale, title, menuOpen);
        _presenter.Request(selection.WorkArea, () =>
            LauncherPanelRenderer.Current.Render(selection, title, scale, menuOpen, _theme(), layout =>
            {
                lock (_gate)
                {
                    // 閉じた後や新しい要求の後に完了した描画で、当たり判定を巻き戻さない。
                    if (_selection is not null && revision == _drawRevision)
                    {
                        _panelLayout = layout;
                    }
                }
            }));
    }

    private void CloseCore()
    {
        if (_selection is null)
        {
            return;
        }

        _selection = null;
        _ignorePointerUpToMs = null;
        _target = 0;
        _targetTitle = "";
        _panelLayout = null;
        _planMenuOpen = false;
        _drawRevision++;
        _mouseSelecting = false;
        try
        {
            _presenter.Hide();
        }
        finally
        {
            // 選択を消した後は失敗通知で閉じ直せないため、ここで必ず割り当てを戻す。
            ApplyCurrentBindings();
        }
    }

    private void CloseIfUnavailable()
    {
        lock (_gate)
        {
            // 旧世代の通知がロックを待つ間に開いた、新しい表示は閉じない。
            if (!_presenter.IsAvailable)
            {
                CloseCore();
            }
        }
    }

    private void ApplyCurrentBindings() =>
        _applyBindings(_selection is not null ? _launcherBindings : _enabled ? _normalBindings : _pausedBindings);

    private static string DisplayTitle(string title)
    {
        var clean = string.Concat(title.Select(character => char.IsControl(character) ? ' ' : character));
        if (clean.Length <= 512)
        {
            return clean;
        }

        var end = StringInfo.ParseCombiningCharacters(clean).Last(index => index <= 512);
        return clean[..end] + "…";
    }
}
