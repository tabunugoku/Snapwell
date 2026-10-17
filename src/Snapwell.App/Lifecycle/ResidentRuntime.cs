using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Lifecycle;

/// <summary>配送中の入力と停止を直列化し、停止後に移動要求が残ることを防ぐ。</summary>
internal sealed class ResidentRuntime(DragSnapController drag, HotkeyCommandExecutor hotkeys,
    IMotionEngine motion, IReadOnlyList<HotkeyBinding> bindings,
    Action<IReadOnlyList<HotkeyBinding>> applyBindings, LauncherController? launcher = null,
    IZonePreview? preview = null, Action<bool>? setOverlayEnabled = null,
    IReadOnlyList<IDisposable>? overlays = null) : IAppRuntime
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<HotkeyBinding> _paused = HotkeyBindingsBuilder.Paused(bindings);
    private bool _disposed;
    private bool _enabled = true;

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = enabled;
            launcher?.SetEnabled(enabled);
            drag.Enabled = enabled && launcher?.IsOpen != true;
            if (!enabled)
            {
                preview?.Hide();
            }

            setOverlayEnabled?.Invoke(enabled);
            // 一時停止中にキーを握りつぶすと、ほかのアプリの操作を奪う。再開のキーだけを残す。
            if (launcher is null)
            {
                applyBindings(enabled ? bindings : _paused);
            }
        }
    }

    public void Handle(HookEvent value)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            launcher?.ValidateTarget();
            if (value is KeyChord chord)
            {
                if (chord.Id == (int)WindowCommand.ToggleEnabled)
                {
                    launcher?.Close();
                }

                if (launcher?.Handle(chord) != true &&
                    ((_enabled && launcher?.IsOpen != true) || chord.Id == (int)WindowCommand.ToggleEnabled))
                {
                    hotkeys.Handle(chord);
                }
            }
            else
            {
                if (value is MouseButton button)
                {
                    launcher?.HandleGlobalPointer(button);
                }
                else if (value is MoveSizeStarted)
                {
                    // 別のドラッグへ操作を返し、ランチャーとプレビューを同時に表示しない。
                    launcher?.Close();
                }

                drag.Enabled = _enabled && launcher?.IsOpen != true;
                drag.Handle(value);
            }

            drag.Enabled = _enabled && launcher?.IsOpen != true;
            if (launcher?.IsOpen == true)
            {
                preview?.Hide();
            }
        }
    }

    public void CancelAll() => motion.CancelAll();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            hotkeys.Stop();
            applyBindings([]);
            launcher?.Dispose();
            drag.Enabled = false;
            preview?.Hide();
            // Close による割り当ての復帰も解除してから、描画と移動のスレッドを止める。
            applyBindings([]);
            setOverlayEnabled?.Invoke(false);
            if (overlays is not null)
            {
                foreach (var overlay in overlays)
                {
                    overlay.Dispose();
                }
            }

            motion.Dispose();
        }
    }
}
