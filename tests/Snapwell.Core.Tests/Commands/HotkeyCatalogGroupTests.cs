using Snapwell.Core.Commands;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Tests.Commands;

public sealed class HotkeyCatalogGroupTests
{
    private static readonly WindowCommand[] _expectedOrder =
    [
        WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf, WindowCommand.BottomHalf,
        WindowCommand.TopLeftQuarter, WindowCommand.TopRightQuarter,
        WindowCommand.BottomLeftQuarter, WindowCommand.BottomRightQuarter,
        WindowCommand.Maximize, WindowCommand.CenterWindow, WindowCommand.CenterWindowKeepSize,
        WindowCommand.GrowRight, WindowCommand.GrowDown, WindowCommand.ShrinkLeft, WindowCommand.ShrinkUp,
        WindowCommand.MoveCellLeft, WindowCommand.MoveCellRight, WindowCommand.MoveCellUp, WindowCommand.MoveCellDown,
        WindowCommand.NextMonitor, WindowCommand.PreviousMonitor,
        WindowCommand.ArrangeGrid, WindowCommand.ArrangeColumns, WindowCommand.ArrangeRows,
        WindowCommand.ArrangeMainAndStack, WindowCommand.ArrangeAllMonitors,
        WindowCommand.SnapAdjacentLeft, WindowCommand.SnapAdjacentRight, WindowCommand.SnapAdjacentUp,
        WindowCommand.SnapAdjacentDown,
        WindowCommand.OpenLauncher, WindowCommand.ToggleEnabled,
    ];

    /// <summary>新しい 2 操作の ID は既存の末尾に足し、既存の値は変えない。</summary>
    [Fact]
    public void CenterCommandsAreAppendedAfterSnapAdjacentDown()
    {
        Assert.Equal(29, (int)WindowCommand.SnapAdjacentDown);
        Assert.Equal(30, (int)WindowCommand.CenterWindow);
        Assert.Equal(31, (int)WindowCommand.CenterWindowKeepSize);
    }

    /// <summary>新しい 2 操作の表示名は日本語で、固定する位置に依存しない。</summary>
    [Theory]
    [InlineData(ResizeAnchor.TopLeft)]
    [InlineData(ResizeAnchor.Center)]
    [InlineData(ResizeAnchor.NearEdge)]
    public void CenterCommandsHaveFixedDisplayNames(ResizeAnchor anchor)
    {
        Assert.Equal("中央に配置", HotkeyCatalog.GetDisplayName(WindowCommand.CenterWindow, 2, anchor));
        Assert.Equal("中央に配置（サイズ維持）", HotkeyCatalog.GetDisplayName(WindowCommand.CenterWindowKeepSize, 2, anchor));
    }

    /// <summary>広げる・縮めるの名前は、固定する位置ごとに実際の動きに合わせて変わる。</summary>
    [Theory]
    [InlineData(ResizeAnchor.TopLeft, 2, "右へ 2 区画広げる", "下へ 2 区画広げる", "左へ 2 区画縮める", "上へ 2 区画縮める")]
    [InlineData(ResizeAnchor.Center, 3, "幅を左右へ 3 区画広げる", "高さを上下へ 3 区画広げる",
        "幅を左右から 3 区画縮める", "高さを上下から 3 区画縮める")]
    [InlineData(ResizeAnchor.NearEdge, 4, "幅を 4 区画広げる", "高さを 4 区画広げる", "幅を 4 区画縮める", "高さを 4 区画縮める")]
    public void ResizeNamesFollowAnchor(ResizeAnchor anchor, int step, string growRight, string growDown,
        string shrinkLeft, string shrinkUp)
    {
        Assert.Equal(growRight, HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight, step, anchor));
        Assert.Equal(growDown, HotkeyCatalog.GetDisplayName(WindowCommand.GrowDown, step, anchor));
        Assert.Equal(shrinkLeft, HotkeyCatalog.GetDisplayName(WindowCommand.ShrinkLeft, step, anchor));
        Assert.Equal(shrinkUp, HotkeyCatalog.GetDisplayName(WindowCommand.ShrinkUp, step, anchor));
    }

    /// <summary>固定する位置を省くと左上の名前になり、未知の値でも左上と同じ名前になる。</summary>
    [Fact]
    public void ResizeNamesDefaultToTopLeftAndClampStep()
    {
        Assert.Equal("右へ 1 区画広げる", HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight));
        Assert.Equal("上へ 1 区画縮める", HotkeyCatalog.GetDisplayName(WindowCommand.ShrinkUp, 1, (ResizeAnchor)99));
        Assert.Equal("幅を 4 区画広げる", HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight, 9, ResizeAnchor.NearEdge));
        Assert.Equal("左へ 1 区画移動", HotkeyCatalog.GetDisplayName(WindowCommand.MoveCellLeft, 3, ResizeAnchor.Center));
    }

    /// <summary>グループ名は、見出しに使う 7 つのどれかで、操作ごとに決まっている。</summary>
    [Theory]
    [InlineData(WindowCommand.LeftHalf, "配置")]
    [InlineData(WindowCommand.Maximize, "配置")]
    [InlineData(WindowCommand.CenterWindow, "配置")]
    [InlineData(WindowCommand.CenterWindowKeepSize, "配置")]
    [InlineData(WindowCommand.GrowRight, "広げる・縮める")]
    [InlineData(WindowCommand.ShrinkUp, "広げる・縮める")]
    [InlineData(WindowCommand.MoveCellLeft, "区画の移動")]
    [InlineData(WindowCommand.MoveCellDown, "区画の移動")]
    [InlineData(WindowCommand.NextMonitor, "モニター")]
    [InlineData(WindowCommand.PreviousMonitor, "モニター")]
    [InlineData(WindowCommand.ArrangeGrid, "整列")]
    [InlineData(WindowCommand.ArrangeAllMonitors, "整列")]
    [InlineData(WindowCommand.SnapAdjacentLeft, "隣へ寄せる")]
    [InlineData(WindowCommand.SnapAdjacentDown, "隣へ寄せる")]
    [InlineData(WindowCommand.OpenLauncher, "そのほか")]
    [InlineData(WindowCommand.ToggleEnabled, "そのほか")]
    public void GroupNamesMatchTheSettingsPage(WindowCommand command, string group) =>
        Assert.Equal(group, HotkeyCatalog.GetGroup(command));

    /// <summary>表示順はグループ順、グループ内の並びで、列挙の値ではなく一覧の位置で決まる。</summary>
    [Fact]
    public void DisplayOrderFollowsGroupsInsteadOfEnumerationValues()
    {
        var all = Enum.GetValues<WindowCommand>();
        Assert.Equal(all.Length, _expectedOrder.Length);
        Assert.Equal(_expectedOrder, all.OrderByDescending(command => (int)command).OrderBy(HotkeyCatalog.GetDisplayOrder));
        Assert.Equal(_expectedOrder.Length, _expectedOrder.Select(HotkeyCatalog.GetDisplayOrder).Distinct().Count());
        Assert.True(HotkeyCatalog.GetDisplayOrder(WindowCommand.MoveCellLeft) <
            HotkeyCatalog.GetDisplayOrder(WindowCommand.ToggleEnabled));
        Assert.True(HotkeyCatalog.GetDisplayOrder(WindowCommand.CenterWindow) <
            HotkeyCatalog.GetDisplayOrder(WindowCommand.GrowRight));
    }

    /// <summary>一覧に無い値は、最後に並べ、グループは「そのほか」にする。</summary>
    [Fact]
    public void UnknownCommandSortsLastInOtherGroup()
    {
        var unknown = (WindowCommand)999;
        Assert.Equal("そのほか", HotkeyCatalog.GetGroup(unknown));
        Assert.All(_expectedOrder, command =>
            Assert.True(HotkeyCatalog.GetDisplayOrder(command) < HotkeyCatalog.GetDisplayOrder(unknown)));
    }

    /// <summary>競合の一覧も、グループ順の表示順で操作を並べる。</summary>
    [Fact]
    public void ConflictsListCommandsInGroupOrder()
    {
        var hotkeys = DefaultHotkeys.Create();
        hotkeys[WindowCommand.ToggleEnabled] = hotkeys[WindowCommand.MoveCellLeft] =
            hotkeys[WindowCommand.CenterWindow];
        var conflict = Assert.Single(HotkeyCatalog.FindConflicts(hotkeys));
        Assert.Equal([WindowCommand.CenterWindow, WindowCommand.MoveCellLeft, WindowCommand.ToggleEnabled],
            conflict.Commands);
    }

    /// <summary>新しい既定のキーは Ctrl+Alt+C と Ctrl+Alt+Shift+C で、既存の既定と重ならない。</summary>
    [Fact]
    public void CenterDefaultsAreNewAndDoNotOverlapExistingDefaults()
    {
        var defaults = DefaultHotkeys.Create();
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt, 0x43),
            defaults[WindowCommand.CenterWindow]);
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, 0x43),
            defaults[WindowCommand.CenterWindowKeepSize]);
        Assert.Empty(HotkeyCatalog.FindConflicts(defaults));
        Assert.Equal(defaults.Count, defaults.Values.Distinct().Count());
    }
}
