using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class TrayIconPolicyTests
{
    [Fact]
    public void EnabledWithoutStatusIsActive() =>
        Assert.Equal(TrayIconKind.Active, TrayIconPolicy.KindOf(new TrayState(true, false)));

    [Fact]
    public void DisabledIsPaused() =>
        Assert.Equal(TrayIconKind.Paused, TrayIconPolicy.KindOf(new TrayState(false, true)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StatusWinsOverEnabled(bool enabled) =>
        Assert.Equal(TrayIconKind.Attention, TrayIconPolicy.KindOf(new TrayState(enabled, false, "復元に失敗した")));

    [Theory]
    [InlineData(TrayIconKind.Active, true, "tray/tray-active-light.ico")]
    [InlineData(TrayIconKind.Paused, false, "tray/tray-paused-dark.ico")]
    [InlineData(TrayIconKind.Attention, true, "tray/tray-attention-light.ico")]
    public void ResourceNameMatchesAssetFiles(TrayIconKind kind, bool light, string expected) =>
        Assert.Equal(expected, TrayIconPolicy.ResourceName(kind, light));

    [Fact]
    public void EveryResourceNameHasAnAssetFile()
    {
        var assets = Path.Combine(FindRepositoryRoot(), "assets", "icons");
        foreach (var kind in Enum.GetValues<TrayIconKind>())
        {
            foreach (var light in new[] { true, false })
            {
                var name = Path.GetFileName(TrayIconPolicy.ResourceName(kind, light));
                Assert.True(File.Exists(Path.Combine(assets, name)), name);
            }
        }
    }

    [Fact]
    public void EveryResourceNameIsEmbeddedInTheApp()
    {
        var embedded = typeof(TrayIcon).Assembly.GetManifestResourceNames();
        foreach (var kind in Enum.GetValues<TrayIconKind>())
        {
            foreach (var light in new[] { true, false })
            {
                Assert.Contains(TrayIconPolicy.ResourceName(kind, light), embedded);
            }
        }
    }

    [Fact]
    public void TipStatesTheStatusInText()
    {
        Assert.Equal("Snapwell", TrayIconPolicy.Tip(new TrayState(true, false)));
        Assert.Equal("Snapwell (一時停止中)", TrayIconPolicy.Tip(new TrayState(false, false)));
        Assert.Equal("Snapwell: 復元に失敗した", TrayIconPolicy.Tip(new TrayState(true, false, "復元に失敗した")));
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(24, 24)]
    [InlineData(32, 32)]
    [InlineData(28, 32)]   // 175%: 24 と 32 の中間。大きいほうを縮める
    [InlineData(22, 24)]
    [InlineData(40, 32)]   // 250% は 32 までしか無い
    [InlineData(8, 16)]
    public void PicksTheNearestFrame(int requested, int expected) =>
        Assert.Equal(expected, TrayIconPolicy.PickFrameSize(requested, [16, 20, 24, 32]));

    [Fact]
    public void PickFrameSizeRejectsEmpty() =>
        Assert.Throws<ArgumentException>(() => TrayIconPolicy.PickFrameSize(16, []));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Snapwell.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つからない。");
    }
}
