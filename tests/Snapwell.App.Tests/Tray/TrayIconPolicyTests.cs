using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class TrayIconPolicyTests
{
    /// <summary>整列は設定の直後に置き、一時停止中だけ選択できなくする。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrangementMenuFollowsSettingsAndIsEnabledOnlyWhenActive(bool enabled)
    {
        var items = TrayIconPolicy.MenuItems(new(enabled, true, "状態"));
        Assert.Equal([5u, 6u, 1u, 2u, 3u, 0u, 4u], items.Select(item => item.Id));
        Assert.Equal("設定を開く", items[0].Text);
        Assert.Equal(new TrayMenuItem(6, "現在のモニターを整列", enabled), items[1]);
        Assert.True(items[3].Checked);
        Assert.False(items[5].Enabled);
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
