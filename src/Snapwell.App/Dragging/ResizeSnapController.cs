using Snapwell.App.Motion;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Dragging;

/// <summary>リサイズの制約を取得する境界。寸法は見た目の物理ピクセル。</summary>
internal interface IResizeConstraints
{
    bool TryGetMinimum(WindowSnapshot window, out int width, out int height);
}

/// <summary>フックから配送済みの開始・終了通知だけで、手動リサイズを区画へ揃える。</summary>
internal sealed class ResizeSnapController(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, Func<PortableSettings> settings, IResizeConstraints constraints, uint ownProcessId)
{
    private WindowSnapshot? _start;

    internal void Handle(HookEvent value, bool enabled)
    {
        if (!enabled || !settings().SnapAfterResize)
        {
            _start = null;
            return;
        }

        if (value is MoveSizeStarted start)
        {
            _start = inspector.TryInspect(start.Handle, out var window) && Eligible(window) ? window : null;
        }
        else if (value is WindowDestroyed closed && _start?.Handle == closed.Handle)
        {
            _start = null;
        }
        else if (value is MoveSizeEnded end && _start is { } original && original.Handle == end.Handle)
        {
            _start = null;
            if (motion.IsMoving(end.Handle) || !inspector.TryInspect(end.Handle, out var window) ||
                window.ProcessId != original.ProcessId || !Eligible(window) ||
                (original.VisualBounds.Width == window.VisualBounds.Width &&
                    original.VisualBounds.Height == window.VisualBounds.Height) ||
                !constraints.TryGetMinimum(window, out var width, out var height))
            {
                return;
            }

            var options = settings();
            var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
            var plan = ResizeSnapPlanner.Plan(options, enabled, true, original.VisualBounds,
                window.VisualBounds, areas, width, height);
            if (plan is not null)
            {
                motion.Request(new(end.Handle, window.VisualBounds, plan.Target, plan.Walls,
                    options.ToFeelPreset(), Resize: true));
            }
        }
    }

    internal void Cancel() => _start = null;

    private bool Eligible(WindowSnapshot window) => !window.IsMaximized &&
        WindowClassifier.CanResize(window, ownProcessId);
}

/// <summary>公開メッセージで最小寸法を問い合わせ、応答しないアプリは揃えない。</summary>
internal sealed class Win32ResizeConstraints : IResizeConstraints
{
    // アニメーションの長さではなく、他プロセスの応答待ちの上限。
    private const uint TimeoutMilliseconds = 100;

    public unsafe bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
    {
        width = height = 1;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return false;
        }

        var dpi = PInvoke.GetDpiForWindow(new HWND(window.Handle));
        var info = new MINMAXINFO
        {
            ptMinTrackSize = new(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXMINTRACK, dpi),
                PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYMINTRACK, dpi)),
        };
        nuint result;
        // システム定義メッセージのバッファの転送は OS に任せる。実機未確認。
        var success = PInvoke.SendMessageTimeout(new HWND(window.Handle), PInvoke.WM_GETMINMAXINFO, default,
            new LPARAM((nint)(&info)), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
            TimeoutMilliseconds, &result).Value != 0;
        width = Math.Max(1, info.ptMinTrackSize.X - window.Insets.Left - window.Insets.Right);
        height = Math.Max(1, info.ptMinTrackSize.Y - window.Insets.Top - window.Insets.Bottom);
        return success;
    }
}
