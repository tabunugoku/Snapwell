using System.Diagnostics;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.App.Tray;

/// <summary>通知領域の吹き出しだけを表示する Win32 の境界。</summary>
internal interface ITrayBalloon
{
    /// <summary>Shell に表示を要求できた場合だけ true を返す。</summary>
    bool Show(string title, string message);
}

/// <summary>メニューの案内を更新し、同じ版の吹き出しを繰り返さない。</summary>
internal sealed class TrayUpdateNotifier(ISettingsStore<DeviceState> store, ITrayBalloon balloon,
    Action<UpdateOffer> setOffer, IAppLog log) : IUpdateNotifier
{
    /// <summary>初めて知らせる版かを、表記の違いではなく SemVer の優先順位で判断する。</summary>
    internal static bool ShouldNotify(UpdateOffer? offer, string? lastNotified)
    {
        if (offer is null) { return false; }
        ReleaseVersion.TryParse(lastNotified, out var last);
        return !offer.Manifest.Version.Equals(last);
    }

    /// <summary>表示できた吹き出しの版だけを最新の端末状態へ保存する。</summary>
    public void Notify(UpdateOffer offer)
    {
        setOffer(offer);
        try
        {
            DeviceStateChanges.Update(store, current =>
            {
                if (!ShouldNotify(offer, current.LastNotifiedVersion)) { return current; }
                var shown = balloon.Show(UiText.Current.Get("Update.NotificationTitle"),
                    UiText.Current.Format("Tray.UpdateAvailable", ("version", offer.Manifest.Version.ToString())));
                return shown ? current with { LastNotifiedVersion = offer.Manifest.Version.ToString() } : current;
            });
        }
        catch (Exception exception)
        {
            try { log.Error(exception); }
            catch (Exception) { }
        }
    }
}

/// <summary>HTTPS のリリースノートだけを既定のブラウザーで開く。</summary>
internal sealed class ReleaseNotesLauncher(IAppLog log, Action<ProcessStartInfo>? start = null)
{
    /// <summary>不正な URL は開かず、起動失敗は診断にだけ記録する。</summary>
    internal bool Open(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0
            || uri.UserInfo.Length != 0) { return false; }
        try
        {
            var info = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
            if (start is not null) { start(info); }
            else { using var process = Process.Start(info); }
            return true;
        }
        catch (Exception exception)
        {
            try { log.Error(exception); }
            catch (Exception) { }
            return false;
        }
    }
}
