using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Lifecycle;

/// <summary>配送中の入力と停止を直列化し、停止後に移動要求が残ることを防ぐ。</summary>
internal sealed class ResidentRuntime(DragSnapController drag, HotkeyCommandExecutor hotkeys,
    IMotionEngine motion, IReadOnlyList<HotkeyBinding> bindings,
    Action<IReadOnlyList<HotkeyBinding>> applyBindings) : IAppRuntime
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<HotkeyBinding> _paused = HotkeyBindingsBuilder.Paused(bindings);
    private bool _disposed;

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            drag.Enabled = enabled;
            // 一時停止中にキーを握りつぶすと、ほかのアプリの操作を奪う。再開のキーだけを残す。
            applyBindings(enabled ? bindings : _paused);
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

            if (value is KeyChord chord)
            {
                hotkeys.Handle(chord);
            }
            else
            {
                drag.Handle(value);
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
            drag.Enabled = false;
            motion.Dispose();
        }
    }
}
