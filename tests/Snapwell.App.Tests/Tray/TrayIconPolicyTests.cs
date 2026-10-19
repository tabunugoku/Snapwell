using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class TrayIconPolicyTests
{
    /// <summary>接続済みのランチャーを状態どおり選択可能にする。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectedLauncherUsesCurrentAvailability(bool enabled)
    {
        var items = TrayIconPolicy.MenuItems(new(enabled, false, LauncherAvailable: true));
        Assert.Equal(enabled, items.Single(item => item.Command == TrayMenuCommand.Launcher).Enabled);
    }

    /// <summary>モックアップの順で並べ、一時停止中と未接続の操作を選択不可にする。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomMenuUsesMockupOrderAndCurrentAvailability(bool enabled)
    {
        var items = TrayIconPolicy.MenuItems(new(enabled, true, "状態"));
        Assert.Equal([TrayMenuCommand.Arrange, TrayMenuCommand.Launcher,
            TrayMenuCommand.Settings, TrayMenuCommand.Exit], items.Select(item => item.Command));
        Assert.Equal("現在のモニターを整列", items[0].Name);
        Assert.Equal(enabled, items[0].Enabled);
        Assert.False(items[1].Enabled);
        Assert.All(items.Skip(2), item => Assert.True(item.Enabled));
    }

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
