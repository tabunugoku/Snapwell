using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Lifecycle;

/// <summary>配送中の入力と停止を直列化し、停止後に移動要求が残ることを防ぐ。</summary>
internal sealed class ResidentRuntime : IAppRuntime
{
    private readonly object _gate = new();
    private readonly DragSnapController _drag;
    private readonly HotkeyCommandExecutor _hotkeys;
    private readonly IMotionEngine _motion;
    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly IReadOnlyList<HotkeyBinding> _paused;
    private readonly Action<IReadOnlyList<HotkeyBinding>> _applyBindings;
    private readonly LauncherController? _launcher;
    private readonly ForegroundTargetTracker? _foregroundTargets;
    private readonly IZonePreview? _preview;
    private readonly Action<bool>? _setOverlayEnabled;
    private readonly IReadOnlyList<IDisposable>? _overlays;
    private readonly CornerWindowController? _corners;
    private readonly ResizeSnapController? _resize;
    private bool _disposed;
    private bool _enabled = true;

    internal ResidentRuntime(DragSnapController drag, HotkeyCommandExecutor hotkeys,
        IMotionEngine motion, IReadOnlyList<HotkeyBinding> bindings,
        Action<IReadOnlyList<HotkeyBinding>> applyBindings, LauncherController? launcher = null,
        IZonePreview? preview = null, Action<bool>? setOverlayEnabled = null,
        IReadOnlyList<IDisposable>? overlays = null,
        ForegroundTargetTracker? foregroundTargets = null, CornerWindowController? corners = null,
        ResizeSnapController? resize = null)
    {
        _drag = drag;
        _hotkeys = hotkeys;
        _motion = motion;
        _bindings = bindings;
        _paused = HotkeyBindingsBuilder.Paused(bindings);
        _applyBindings = applyBindings;
        _launcher = launcher;
        _foregroundTargets = launcher is null ? null : foregroundTargets ??
            new ForegroundTargetTracker(new Win32WindowInspector(), (uint)Environment.ProcessId);
        _preview = preview;
        _setOverlayEnabled = setOverlayEnabled;
        _overlays = overlays;
        _corners = corners;
        _resize = resize;
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = enabled;
            _corners?.SetEnabled(enabled);
            _launcher?.SetEnabled(enabled);
            _drag.Enabled = enabled && _launcher?.IsOpen != true;
            if (!enabled)
            {
                _resize?.Cancel();
                _preview?.Hide();
            }

            _setOverlayEnabled?.Invoke(enabled);
            // 一時停止中にキーを握りつぶすと、ほかのアプリの操作を奪う。再開のキーだけを残す。
            if (_launcher is null)
            {
                _applyBindings(enabled ? _bindings : _paused);
            }
        }
    }

    public void Handle(HookEvent value)
    {
        // 他プロセスの調査は遅いことがあるため、入力の配送を止めるロックの外で記録する。
        if (value is ForegroundChanged recorded)
        {
            _foregroundTargets?.Record(recorded.Handle);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _launcher?.ValidateTarget();
            _corners?.Handle(value);
            if (value is KeyChord chord)
            {
                if (chord.Id == (int)WindowCommand.ToggleEnabled)
                {
                    _launcher?.Close();
                }

                if (_launcher?.Handle(chord) != true &&
                    ((_enabled && _launcher?.IsOpen != true) || chord.Id == (int)WindowCommand.ToggleEnabled))
                {
                    _hotkeys.Handle(chord);
                }
            }
            else
            {
                if (value is MouseButton button)
                {
                    _launcher?.HandleGlobalPointer(button);
                }
                else if (value is MoveSizeStarted)
                {
                    // 別のドラッグへ操作を返し、ランチャーとプレビューを同時に表示しない。
                    _launcher?.Close();
                }
                else if (value is ForegroundChanged foreground)
                {
                    _launcher?.HandleForegroundChanged(foreground);
                }

                _drag.Enabled = _enabled && _launcher?.IsOpen != true;
                _drag.Handle(value);

                _resize?.Handle(value, _enabled && _launcher?.IsOpen != true);
            }

            _drag.Enabled = _enabled && _launcher?.IsOpen != true;
            if (_launcher?.IsOpen == true)
            {
                _resize?.Cancel();
                _preview?.Hide();
            }
        }
    }

    /// <summary>入力配送と停止に対して直列化し、カーソルのモニターを整列する。</summary>
    public void ArrangeCurrentMonitor()
    {
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            _launcher?.Close();
            _preview?.Hide();
            _hotkeys.ArrangeCurrentMonitor();
        }
    }

    /// <summary>入力配送と停止に対して直列化し、指定した対象でランチャーを開く。</summary>
    public void OpenLauncher(nint target, uint ignorePointerUpToMs)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || _launcher is null || _launcher.IsOpen)
            {
                return;
            }

            _preview?.Hide();
            _launcher.Open(_foregroundTargets!.Resolve(target), ignorePointerUpToMs);
            if (_launcher.IsOpen)
            {
                _resize?.Cancel();
                _drag.Enabled = false;
            }
        }
    }

    public void CancelAll() => _motion.CancelAll();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _resize?.Cancel();
            _corners?.SetEnabled(false);
            _hotkeys.Stop();
            _applyBindings([]);
            _launcher?.Dispose();
            _drag.Enabled = false;
            _preview?.Hide();
            // Close による割り当ての復帰も解除してから、描画と移動のスレッドを止める。
            _applyBindings([]);
            _setOverlayEnabled?.Invoke(false);
            if (_overlays is not null)
            {
                foreach (var overlay in _overlays)
                {
                    overlay.Dispose();
                }
            }

            _motion.Dispose();
        }
    }
}
