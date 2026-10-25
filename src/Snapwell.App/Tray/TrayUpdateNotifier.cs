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

/// <summary>直接通知とキャッシュの再評価で、表示の世代を共有する境界。</summary>
internal interface ITrayUpdateGeneration
{
    /// <summary>非同期の検証や配送を始める前に表示の世代を確保する。</summary>
    long BeginUpdateOffer();

    /// <summary>確保した世代が最新の場合だけトレイのスレッドで反映する。</summary>
    void SetUpdateOffer(UpdateOffer? offer, long generation);
}

/// <summary>メニューの案内を更新し、同じ版の吹き出しを繰り返さない。</summary>
internal sealed class TrayUpdateNotifier(ISettingsStore<DeviceState> store, ITrayBalloon balloon,
    Action<UpdateOffer> setOffer, IAppLog log, Action? clearOffer = null) : IUpdateNotifier
{
    private long _generation;
    private UpdateOffer? _presentedOffer;

    /// <summary>検証と配送の開始順を、案内の有無にかかわらず共有する。</summary>
    internal long BeginUpdateOffer() => Interlocked.Increment(ref _generation);

    /// <summary>初めて知らせる版かを、表記の違いではなく SemVer の優先順位で判断する。</summary>
    internal static bool ShouldNotify(UpdateOffer? offer, string? lastNotified)
    {
        if (offer is null) { return false; }
        ReleaseVersion.TryParse(lastNotified, out var last);
        return !offer.Manifest.Version.Equals(last);
    }

    /// <summary>表示できた吹き出しの版だけを最新の端末状態へ保存する。</summary>
    public void Notify(UpdateOffer offer) => SetUpdateOffer(offer, BeginUpdateOffer());

    /// <summary>署名を再検証せず、最新のスキップと最高受理版で表示直前に絞り込む。</summary>
    internal void SetUpdateOffer(UpdateOffer? offer, long generation)
    {
        if (generation != Volatile.Read(ref _generation)) { return; }
        if (offer is null)
        {
            clearOffer?.Invoke();
            _presentedOffer = null;
            return;
        }
        try
        {
            // 自分の保存による watcher の再通知で、同じファイルを書き続けない。
            var loaded = store.TryReload() ?? store.Load();
            if (generation != Volatile.Read(ref _generation)) { return; }
            if (loaded.IsReadOnly) { return; }
            if (!CanPresent(offer, loaded.Value))
            {
                // 無効な直接通知が配送待ちのクリアを追い越しても、スキップ済みの表示を残さない。
                if (_presentedOffer is not null && !CanPresent(_presentedOffer, loaded.Value))
                {
                    clearOffer?.Invoke();
                    _presentedOffer = null;
                }
                return;
            }
            setOffer(offer);
            _presentedOffer = offer;
            if (!ShouldNotify(offer, loaded.Value.LastNotifiedVersion)) { return; }
            DeviceStateChanges.Update(store, current =>
            {
                if (generation != Volatile.Read(ref _generation) || !CanPresent(offer, current)
                    || !ShouldNotify(offer, current.LastNotifiedVersion))
                {
                    throw new NotificationUnchangedException();
                }
                var shown = balloon.Show(UiText.Current.Get("Update.NotificationTitle"),
                    UiText.Current.Format("Tray.UpdateAvailable", ("version", offer.Manifest.Version.ToString())));
                // 保存不要なら Update を抜け、ファイル監視による無限の再評価を避ける。
                if (!shown) { throw new NotificationUnchangedException(); }
                return current with { LastNotifiedVersion = offer.Manifest.Version.ToString() };
            });
        }
        catch (NotificationUnchangedException) { }
        catch (Exception exception)
        {
            try { log.Error(exception); }
            catch (Exception) { }
        }
    }

    private static bool CanPresent(UpdateOffer offer, DeviceState state)
    {
        ReleaseVersion.TryParse(state.SkippedVersion, out var skipped);
        ReleaseVersion.TryParse(state.HighestSeenVersion, out var highest);
        return !offer.Manifest.Version.Equals(skipped) && UpdateReplayGuard.Accept(offer.Manifest, highest);
    }

    private sealed class NotificationUnchangedException : Exception;
}

/// <summary>HTTPS のリリースノートだけを既定のブラウザーで開く。</summary>
internal sealed class ReleaseNotesLauncher(IAppLog log, Action<ProcessStartInfo>? start = null)
{
    /// <summary>不正な URL は開かず、起動失敗は診断にだけ記録する。</summary>
    internal bool Open(Uri uri)
    {
        if (!UpdateLinks.IsAllowed(uri)) { return false; }
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
