using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Lifecycle;

/// <summary>配送スレッドで対象を調べ、角の変更と復元を行う。</summary>
internal sealed class CornerWindowController(CornerStyler styler, IWindowInspector inspector,
    Func<PortableSettings> settings, uint ownProcessId, IAppLog log, WindowTargetResolver? targets = null)
{
    private readonly WindowTargetResolver _targets = targets ?? new(new Win32ProcessNameSource(), settings, ownProcessId);
    private bool _enabled;

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
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
        try
        {
            var target = _targets.Resolve(window);
            var style = CornerStyle.Default;
            if (!window.IsMaximized && target.OriginalKind != WindowKind.Excluded)
            {
                style = target.Corner switch
                {
                    CornerRule.Round => CornerStyle.Round,
                    CornerRule.RoundSmall => CornerStyle.RoundSmall,
                    CornerRule.DoNotRound => CornerStyle.DoNotRound,
                    CornerRule.Inherit when (window.ExtendedStyle & 0x80) == 0 &&
                        WindowClassifier.HasSystemFrame(window) &&
                        target.OriginalKind is WindowKind.Normal or WindowKind.FixedSize =>
                        (CornerStyle)settings().CornerPreference,
                    _ => CornerStyle.Default,
                };
            }

            // Default は DWM の値を指定せず、退避済みの元の値へ戻す。
            styler.Apply(window.Handle, style);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 退避できなければ DWM を変えず、残りの対象の確認は続ける。
            log.Error(exception);
        }
    }
}
