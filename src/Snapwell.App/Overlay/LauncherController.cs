using System.Globalization;
using Snapwell.App.Hotkeys;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
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
    private readonly LauncherArrangement _arrangement;
    private readonly WindowSelection _selectedWindows = new();
    private IReadOnlyList<LauncherWindow> _windows = [];
    private LauncherWindowListLayout? _windowList;
    private LauncherWindowListLayout? _displayedWindowList;
    private long _displayGeneration;
    private long _displayedRevision;
    private MonitorArea? _destination;
    private int _focusedWindow;
    private SelectedArrangementPlan? _lastArrangement;
    private double _scale = 1;
    private PhysicalSpacing _spacing;
    private string _targetTitle = "";
    private LauncherPanelLayout? _panelLayout;
    private bool _planMenuOpen;
    private long _drawRevision;
    private ZoneSelection? _selection;
    private nint _target;
    private uint? _ignorePointerUpToMs;
    private bool _ignoreRetargetRelease;
    private bool _mouseSelecting;
    private bool _leftButtonDown;
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
        _launcherBindings =
            [.. LauncherWindowBindings.WithoutConflicts(normalBindings), .. HotkeyBindingsBuilder.Launcher(normalBindings)];
        _ownProcessId = ownProcessId;
        _theme = theme ?? OverlayThemeSource.Read;
        _arrangement = new(inspector, monitors, ownProcessId, place);
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

    internal nint[] SelectedWindows
    {
        get { lock (_gate) { return _selectedWindows.Snapshot(); } }
    }

    internal LauncherWindowListLayout? WindowList
    {
        get { lock (_gate) { return _windowList; } }
    }

    internal SelectedArrangementPlan? LastArrangement
    {
        get { lock (_gate) { return _lastArrangement; } }
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

            if (_selection is not null && handle != _target)
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
            _destination = monitor;
            _selectedWindows.Clear();
            _lastArrangement = null;
            _windows = _arrangement.Snapshot().Select(item =>
                new LauncherWindow(item.Handle, DisplayTitle(item.Title))).ToArray();
            _focusedWindow = Math.Max(0, Array.FindIndex(_windows.ToArray(), window => window.Handle == handle));
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
        if (LauncherWindowBindings.Contains(chord.Id))
        {
            HandleWindowKey((LauncherWindowInput)chord.Id);
            return true;
        }

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

            if (button.Button == MouseButtonKind.Left)
            {
                // オーバーレイ側の遅配で解放を巻き戻さないよう、グローバル通知だけで更新する。
                _leftButtonDown = button.IsDown;
                if (_ignoreRetargetRelease)
                {
                    _ignoreRetargetRelease = false;
                    if (!button.IsDown) { return; }
                }
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
        Action? complete = null;
        var handled = false;
        lock (_gate)
        {
            if (_displayedWindowList?.Panel.Contains(pointer.Position) == true)
            {
                if (!ValidateTargetCore())
                {
                    return;
                }

                handled = HandleWindowPointerCore(pointer, out complete);
            }
        }

        if (handled)
        {
            complete?.Invoke();
            return;
        }

        HandleGridPointer(pointer);
    }

    private bool HandleWindowPointerCore(OverlayPointer pointer, out Action? complete)
    {
        complete = null;
        if (_displayedWindowList is not { } list || !list.Panel.Contains(pointer.Position))
        {
            return false;
        }

        if (pointer.Kind == OverlayPointerKind.RightDown)
        {
            CloseCore();
            return true;
        }

        if (pointer.Kind != OverlayPointerKind.Down)
        {
            if (pointer.Kind == OverlayPointerKind.Up) { _mouseSelecting = false; }
            return true;
        }

        _mouseSelecting = false;
        // 分割案のメニューは一覧より前に操作できるようにする。
        if (_planMenuOpen)
        {
            return false;
        }

        if (list.Rows.FirstOrDefault(row => row.Bounds.Contains(pointer.Position)) is { } row)
        {
            _focusedWindow = Array.FindIndex(_windows.ToArray(), window => window.Handle == row.Window.Handle);
            if (pointer.Control)
            {
                _selectedWindows.Toggle(row.Window.Handle);
                Draw();
            }
            else
            {
                var handle = row.Window.Handle;
                var timeMs = pointer.TimeMs ?? unchecked((uint)Environment.TickCount);
                CloseCore();
                complete = () =>
                {
                    lock (_gate)
                    {
                        // 同じクリックの遅配をトレイと同じ時計で除外し、解放まで新しい選択を始めない。
                        Open(handle, timeMs);
                        _ignoreRetargetRelease = _selection is not null;
                    }
                };
            }
        }
        else if (list.Buttons.FirstOrDefault(button => button.Bounds.Contains(pointer.Position)) is { } button)
        {
            // 選択解除の表示が遅れても、旧ボタンからモニター全体の整列へ切り替えない。
            if (_selectedWindows.Count > 0)
            {
                complete = PrepareArrangementCore(button.Layout);
            }
        }
        else if (list.Previous.Contains(pointer.Position) || list.Next.Contains(pointer.Position))
        {
            var direction = list.Previous.Contains(pointer.Position) ? -1 : 1;
            _focusedWindow = Math.Clamp(_focusedWindow + direction * Math.Max(1, list.Rows.Count),
                0, Math.Max(0, _windows.Count - 1));
            Draw();
        }

        return true;
    }

    private void HandleWindowKey(LauncherWindowInput input)
    {
        Action? complete = null;
        lock (_gate)
        {
            if (!ValidateTargetCore())
            {
                return;
            }

            _mouseSelecting = false;
            if (input is >= LauncherWindowInput.Grid and <= LauncherWindowInput.MainAndStack)
            {
                // 長押しで開いた元の押下が続く間は、移動ループと整列を競合させない。
                if (!_leftButtonDown)
                {
                    complete = PrepareArrangementCore((ArrangeLayout)(input - LauncherWindowInput.Grid));
                }
            }
            else if (_windows.Count > 0)
            {
                if (input == LauncherWindowInput.Toggle)
                {
                    _selectedWindows.Toggle(_windows[_focusedWindow].Handle);
                }
                else
                {
                    var delta = input == LauncherWindowInput.Previous ? -1 : 1;
                    _focusedWindow = (_focusedWindow + delta + _windows.Count) % _windows.Count;
                }

                Draw();
            }
        }

        complete?.Invoke();
    }

    private Action PrepareArrangementCore(ArrangeLayout layout)
    {
        var selected = _selectedWindows.Snapshot();
        var destination = _destination!;
        var spacing = _settings().ToSpacing();
        var target = _target;
        CloseCore();
        return () =>
        {
            var result = _arrangement.Execute(layout, selected, destination, spacing, target);
            lock (_gate) { _lastArrangement = result; }
        };
    }

    private void HandleGridPointer(OverlayPointer pointer)
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
                if (panel.Panel.Contains(pointer.Position) || _displayedWindowList?.Panel.Contains(pointer.Position) == true ||
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
            WindowClassifier.Classify(window, _ownProcessId) == WindowKind.Excluded ||
            !_monitors.GetMonitors().Any(monitor => monitor.ToMonitorArea() == _destination))
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
        var windows = _windows;
        var selected = _selectedWindows.Snapshot();
        var focused = _focusedWindow;
        _panelLayout ??= LauncherPanelLayout.Create(selection, scale, title, menuOpen);
        _windowList = LauncherWindowListLayout.Create(selection.WorkArea, _panelLayout.Panel, scale,
            windows, selected, focused);
        var generation = _displayGeneration;
        var renderedPanel = _panelLayout;
        var renderedList = _windowList;
        _presenter.Request(selection.WorkArea, () =>
            LauncherPanelRenderer.Current.Render(selection, title, scale, menuOpen, _theme(), layout =>
            {
                renderedPanel = layout;
                renderedList = LauncherWindowListLayout.Create(selection.WorkArea, layout.Panel, scale,
                    windows, selected, focused);
            }, layout => LauncherWindowListLayout.Create(selection.WorkArea, layout.Panel, scale,
                windows, selected, focused)), () =>
            {
                lock (_gate)
                {
                    // 計算だけで破棄されたフレームは反映せず、実際に表示した版でヒットさせる。
                    if (_selection is not null && generation == _displayGeneration && revision >= _displayedRevision)
                    {
                        _displayedRevision = revision;
                        _displayedWindowList = renderedList;
                        if (revision == _drawRevision)
                        {
                            _panelLayout = renderedPanel;
                            _windowList = renderedList;
                        }
                    }
                }
            });
    }

    private void CloseCore()
    {
        if (_selection is null)
        {
            return;
        }

        _selection = null;
        _ignorePointerUpToMs = null;
        _ignoreRetargetRelease = false;
        _target = 0;
        _selectedWindows.Clear();
        _windows = [];
        _windowList = null;
        _displayedWindowList = null;
        _displayGeneration++;
        _destination = null;
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
