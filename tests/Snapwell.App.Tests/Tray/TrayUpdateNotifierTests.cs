using System.Diagnostics;
using System.Text;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.App.Tests.Tray;

/// <summary>Win32 に触れず、吹き出しの重複抑止とブラウザー起動を検証する。</summary>
public sealed class TrayUpdateNotifierTests
{
    /// <summary>スキップの反映後に古い直接通知が届いても、同じ版の案内を復活させない。</summary>
    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("v1.2.0+build", "1.2.0")]
    [InlineData(null, "1.3.0")]
    public void LateNotificationDoesNotRestoreSkippedOrSupersededOffer(string? skipped, string highest)
    {
        var store = new Store();
        var balloon = new Balloon();
        UpdateOffer? visible = null;
        var notifier = new TrayUpdateNotifier(store, balloon, offer => visible = offer, new Log());
        var delayed = Offer("1.2.0");
        notifier.Notify(delayed);
        store.Value = store.Value with { SkippedVersion = skipped, HighestSeenVersion = highest };
        visible = null;

        notifier.Notify(delayed);

        Assert.Null(visible);
        Assert.Equal(1, balloon.Calls);
    }

    /// <summary>再評価で案内を消した後は、配送待ちだった通知の世代を受け付けない。</summary>
    [Fact]
    public void QueuedNotificationCannotOutliveLaterClear()
    {
        var store = new Store();
        var balloon = new Balloon();
        UpdateOffer? visible = Offer("1.2.0");
        var notifier = new TrayUpdateNotifier(store, balloon, offer => visible = offer, new Log(), () => visible = null);
        var delayedGeneration = notifier.BeginUpdateOffer();
        store.Value = store.Value with { SkippedVersion = "1.2.0" };
        notifier.SetUpdateOffer(null, notifier.BeginUpdateOffer());

        notifier.SetUpdateOffer(Offer("1.2.0"), delayedGeneration);

        Assert.Null(visible);
        Assert.Equal(0, balloon.Calls);
    }

    /// <summary>無効な通知がクリアを追い越しても、既に表示中のスキップ済み案内を残さない。</summary>
    [Fact]
    public void InvalidNotificationOvertakingPendingClearRemovesSkippedPresentation()
    {
        var store = new Store();
        UpdateOffer? visible = null;
        var notifier = new TrayUpdateNotifier(store, new Balloon(), offer => visible = offer,
            new Log(), () => visible = null);
        notifier.Notify(Offer("1.2.0"));
        var pendingClear = notifier.BeginUpdateOffer();
        store.Value = store.Value with { SkippedVersion = "1.2.0" };

        notifier.Notify(Offer("1.2.0"));
        notifier.SetUpdateOffer(null, pendingClear);

        Assert.Null(visible);
    }

    /// <summary>古い通知を破棄する際も、既に表示した新しい有効な案内は消さない。</summary>
    [Fact]
    public void SupersededNotificationPreservesNewerValidPresentation()
    {
        var store = new Store { Value = new() { HighestSeenVersion = "1.3.0" } };
        UpdateOffer? visible = null;
        var notifier = new TrayUpdateNotifier(store, new Balloon(), offer => visible = offer,
            new Log(), () => visible = null);
        notifier.Notify(Offer("1.3.0"));

        notifier.Notify(Offer("1.2.0"));

        Assert.Equal("1.3.0", visible?.Manifest.Version.ToString());
    }

    /// <summary>遅れた案内なしの再評価も、後から届いた新しい通知を消さない。</summary>
    [Fact]
    public void DelayedClearCannotEraseNewerNotification()
    {
        var store = new Store();
        UpdateOffer? visible = null;
        var notifier = new TrayUpdateNotifier(store, new Balloon(), offer => visible = offer,
            new Log(), () => visible = null);
        var delayedGeneration = notifier.BeginUpdateOffer();
        notifier.Notify(Offer("1.3.0"));

        notifier.SetUpdateOffer(null, delayedGeneration);

        Assert.Equal("1.3.0", visible?.Manifest.Version.ToString());
    }

    /// <summary>初めての版と異なる版だけを知らせ、案内なしでは表示しない。</summary>
    [Fact]
    public void NotificationDecisionUsesVersionPrecedence()
    {
        var offer = Offer("1.2.0");
        Assert.True(TrayUpdateNotifier.ShouldNotify(offer, null));
        Assert.True(TrayUpdateNotifier.ShouldNotify(offer, "bad"));
        Assert.True(TrayUpdateNotifier.ShouldNotify(offer, "1.1.0"));
        Assert.False(TrayUpdateNotifier.ShouldNotify(offer, "1.2.0"));
        Assert.False(TrayUpdateNotifier.ShouldNotify(offer, "v1.2.0+build"));
        Assert.False(TrayUpdateNotifier.ShouldNotify(null, null));
    }

    /// <summary>吹き出しが成功した版を保存し、同じ版でもメニューの案内は渡す。</summary>
    [Fact]
    public void SuccessfulBalloonIsSavedOnceAndOtherSettingsSurvive()
    {
        var store = new Store();
        var balloon = new Balloon();
        var offers = new List<UpdateOffer>();
        var notifier = new TrayUpdateNotifier(store, balloon, offers.Add, new Log());
        notifier.Notify(Offer("1.2.0"));
        notifier.Notify(Offer("1.2.0"));
        Assert.Equal(1, balloon.Calls);
        Assert.Equal("1.2.0", store.Value.LastNotifiedVersion);
        Assert.Equal("en", store.Value.Language);
        Assert.Equal("chosen", store.Value.SyncFolder);
        Assert.Equal(2, offers.Count);
        notifier.Notify(Offer("1.3.0"));
        Assert.Equal(2, balloon.Calls);
        Assert.Equal("1.3.0", store.Value.LastNotifiedVersion);
    }

    /// <summary>Shell が要求を受け付けなければ通知済みにせず、次の案内で再試行する。</summary>
    [Fact]
    public void FailedBalloonDoesNotMarkVersionAsNotified()
    {
        var store = new Store();
        var balloon = new Balloon { Succeeds = false };
        var notifier = new TrayUpdateNotifier(store, balloon, _ => { }, new Log());
        notifier.Notify(Offer("1.2.0"));
        Assert.Null(store.Value.LastNotifiedVersion);
        balloon.Succeeds = true;
        notifier.Notify(Offer("1.2.0"));
        Assert.Equal("1.2.0", store.Value.LastNotifiedVersion);
        Assert.Equal(2, balloon.Calls);
    }

    /// <summary>HTTPS 以外と資格情報付き URL をシェルへ渡さない。</summary>
    [Theory]
    [InlineData("http://example.com/notes", false)]
    [InlineData("file:///C:/notes.txt", false)]
    [InlineData("https://user@example.com/notes", false)]
    [InlineData("notes", false)]
    [InlineData("https://example.com/notes", true)]
    public void ReleaseNotesRequireAnonymousHttps(string text, bool allowed)
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new ReleaseNotesLauncher(new Log(), starts.Add);
        Assert.Equal(allowed, launcher.Open(new(text, UriKind.RelativeOrAbsolute)));
        Assert.Equal(allowed ? 1 : 0, starts.Count);
        if (allowed)
        {
            Assert.True(starts[0].UseShellExecute);
            Assert.Equal(text, starts[0].FileName);
        }
    }

    private static UpdateOffer Offer(string version)
    {
        var json = $$$$"""
            {"version":"{{{{version}}}}","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://example.com/notes",
            "files":{"win-arm64":{"url":"https://github.com/arm64.zip","sha256":"{{{{new string('a', 64)}}}}","size":123}}}
            """;
        var manifest = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json)).Manifest!;
        return new(manifest, manifest.Files["win-arm64"], false);
    }

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new() { Language = "en", SyncFolder = "chosen" };
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public void Save(DeviceState value) => Value = value;
    }

    private sealed class Balloon : ITrayBalloon
    {
        internal int Calls;
        internal bool Succeeds = true;
        public bool Show(string title, string message)
        {
            Assert.Equal("Snapwell", title);
            Assert.Contains("1.", message);
            Calls++;
            return Succeeds;
        }
    }

    private sealed class Log : IAppLog
    {
        public void Error(Exception exception) { }
    }
}
