using System.Text;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.App.Tests.Tray;

/// <summary>再評価がファイル監視の保存ループを作らないことを確かめる。</summary>
public sealed class TrayUpdatePersistenceTests
{
    /// <summary>同じ案内を再評価しても通知と保存を繰り返さず、表示先には毎回届ける。</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void RepeatedResolutionDoesNotWriteUnchangedNotificationState(bool shown, int expectedSaves)
    {
        var store = new Store();
        var balloon = new Balloon(shown);
        var presentations = 0;
        var notifier = new TrayUpdateNotifier(store, balloon, _ => presentations++, new Log());
        var manifest = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes("""
            {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://example.com/notes","files":{"win-arm64":{"url":"https://example.com/app.zip",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
            """)).Manifest!;
        var offer = new UpdateOffer(manifest, manifest.Files["win-arm64"], false);
        notifier.Notify(offer);
        notifier.Notify(offer);
        Assert.Equal(2, presentations);
        Assert.Equal(expectedSaves, store.Saves);
        Assert.Equal(shown ? 1 : 2, balloon.Calls);
    }

    private sealed class Store : ISettingsStore<DeviceState>
    {
        private DeviceState _value = new();
        internal int Saves;
        public SettingsLoadResult<DeviceState> Load() => new(_value);
        public void Save(DeviceState value) { _value = value; Saves++; }
    }

    private sealed class Balloon(bool shown) : ITrayBalloon
    {
        internal int Calls;
        public bool Show(string title, string message) { Calls++; return shown; }
    }

    private sealed class Log : IAppLog
    {
        public void Error(Exception exception) => Assert.Fail(exception.Message);
    }
}
