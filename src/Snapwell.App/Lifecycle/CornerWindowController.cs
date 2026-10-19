using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Lifecycle;

/// <summary>配送スレッドで対象を調べ、角の変更と復元を行う。</summary>
internal sealed class CornerWindowController(CornerStyler styler, IWindowInspector inspector,
    Func<PortableSettings> settings, uint ownProcessId, IAppLog log)
{
    private bool _enabled;

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled || settings().CornerPreference == WindowCornerPreference.Default)
        {
            styler.RestoreAll();
            return;
        }

        foreach (var handle in inspector.GetTopLevelWindows())
        {
            if (inspector.TryInspect(handle, out var window))
            {
                Apply(window);
            }
        }
    }

    internal void Handle(HookEvent value)
    {
        if (value is WindowDestroyed closed)
        {
            styler.Forget(closed.Handle);
        }
        else if (_enabled && value is WindowShown shown && inspector.TryInspect(shown.Handle, out var window))
        {
            Apply(window);
        }
    }

    private void Apply(WindowSnapshot window)
    {
        if (window.IsMaximized || (window.ExtendedStyle & 0x80) != 0 ||
            WindowClassifier.Classify(window, ownProcessId) is not (WindowKind.Normal or WindowKind.FixedSize))
        {
            return;
        }

        try
        {
            styler.Apply(window.Handle, (CornerStyle)settings().CornerPreference);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 退避できなければ DWM を変えず、残りの対象の確認は続ける。
            log.Error(exception);
        }
    }
}
