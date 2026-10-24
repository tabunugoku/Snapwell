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
            "files":{"win-arm64":{"url":"https://example.com/arm64.zip","sha256":"{{{{new string('a', 64)}}}}","size":123}}}
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
