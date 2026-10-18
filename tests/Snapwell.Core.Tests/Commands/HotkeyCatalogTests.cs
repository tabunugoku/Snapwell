using Snapwell.Core.Commands;

namespace Snapwell.Core.Tests.Commands;

public sealed class HotkeyCatalogTests
{
    [Fact]
    public void EveryCommandHasADisplayNameIncludingOpenLauncher()
    {
        Assert.Equal("ランチャーを開く", HotkeyCatalog.GetDisplayName(WindowCommand.OpenLauncher));
        Assert.Equal("現在のモニターを整列 (グリッド)", HotkeyCatalog.GetDisplayName(WindowCommand.ArrangeGrid));
        Assert.Equal("現在のモニターを横一列に整列", HotkeyCatalog.GetDisplayName(WindowCommand.ArrangeColumns));
        Assert.Equal("現在のモニターを縦一列に整列", HotkeyCatalog.GetDisplayName(WindowCommand.ArrangeRows));
        Assert.Equal("現在のモニターをメインとスタックで整列",
            HotkeyCatalog.GetDisplayName(WindowCommand.ArrangeMainAndStack));
        Assert.Equal("全モニターを整列 (グリッド)", HotkeyCatalog.GetDisplayName(WindowCommand.ArrangeAllMonitors));
        foreach (var command in Enum.GetValues<WindowCommand>())
        {
            Assert.NotEqual(command.ToString(), HotkeyCatalog.GetDisplayName(command));
        }
    }

    [Fact]
    public void DefaultsHaveNoConflicts() => Assert.Empty(HotkeyCatalog.FindConflicts(DefaultHotkeys.Create()));

    [Theory]
    [InlineData(WindowCommand.ArrangeColumns, 0x48u)]
    [InlineData(WindowCommand.ArrangeRows, 0x56u)]
    [InlineData(WindowCommand.ArrangeMainAndStack, 0x4Du)]
    [InlineData(WindowCommand.ArrangeAllMonitors, 0x41u)]
    public void ArrangementDefaultsUseControlAltShiftAndHaveDisplayNames(WindowCommand command, uint key)
    {
        var defaults = DefaultHotkeys.Create();
        Assert.Equal(new(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, key), defaults[command]);
        Assert.Empty(HotkeyCatalog.FindConflicts(defaults));
        Assert.False(string.IsNullOrWhiteSpace(HotkeyCatalog.GetDisplayName(command)));
        Assert.Equal(25, (int)WindowCommand.ArrangeAllMonitors);
    }

    /// <summary>隣接吸着は既存の ID の末尾に追加し、Win なしの三つの修飾キーと日本語名を使う。</summary>
    [Theory]
    [InlineData(WindowCommand.SnapAdjacentLeft, 26, 0x4Au, "左の隣へ寄せる")]
    [InlineData(WindowCommand.SnapAdjacentRight, 27, 0x4Cu, "右の隣へ寄せる")]
    [InlineData(WindowCommand.SnapAdjacentUp, 28, 0x49u, "上の隣へ寄せる")]
    [InlineData(WindowCommand.SnapAdjacentDown, 29, 0x4Bu, "下の隣へ寄せる")]
    public void AdjacentDefaultsAppendStableIdsAndAvoidWindowsModifier(
        WindowCommand command, int id, uint key, string name)
    {
        Assert.Equal(id, (int)command);
        Assert.Equal(new(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift,
            key), DefaultHotkeys.Create()[command]);
        Assert.Equal(name, HotkeyCatalog.GetDisplayName(command));
    }

    [Fact]
    public void ConflictContainsEveryCommandInDisplayOrder()
    {
        var hotkeys = DefaultHotkeys.Create();
        hotkeys[WindowCommand.RightHalf] = hotkeys[WindowCommand.LeftHalf];
        hotkeys[WindowCommand.TopHalf] = hotkeys[WindowCommand.LeftHalf];
        var conflict = Assert.Single(HotkeyCatalog.FindConflicts(hotkeys));
        Assert.Equal(hotkeys[WindowCommand.LeftHalf], conflict.Chord);
        Assert.Equal([WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf], conflict.Commands);
    }

    [Fact]
    public void DifferentModifiersDoNotConflict()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = new(ChordModifiers.Control, 0x25),
            [WindowCommand.RightHalf] = new(ChordModifiers.Control | ChordModifiers.Shift, 0x25),
        };
        Assert.Empty(HotkeyCatalog.FindConflicts(hotkeys));
    }

    [Theory]
    [InlineData(0x25u, "←")]
    [InlineData(0x26u, "↑")]
    [InlineData(0x27u, "→")]
    [InlineData(0x28u, "↓")]
    [InlineData(0x55u, "U")]
    [InlineData(0x30u, "0")]
    [InlineData(0x0Du, "Enter")]
    [InlineData(0x21u, "PageUp")]
    [InlineData(0x22u, "PageDown")]
    [InlineData(0x13u, "Pause")]
    [InlineData(0x5Du, "0x5D")]
    public void FormatsVirtualKeys(uint key, string expected) => Assert.Equal("Ctrl+Alt+" + expected,
        HotkeyCatalog.FormatChord(new(ChordModifiers.Control | ChordModifiers.Alt, key)));

    [Fact]
    public void ModifierOrderIsStable()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+→", HotkeyCatalog.FormatChord(new(
            ChordModifiers.Win | ChordModifiers.Shift | ChordModifiers.Alt | ChordModifiers.Control, 0x27)));
        Assert.Equal("0x5D", HotkeyCatalog.FormatChord(new(ChordModifiers.None, 0x5D)));
    }

    [Fact]
    public void OriginalTwentyCommandsHaveJapaneseNamesAndEnumerationOrder()
    {
        WindowCommand[] commands =
        [
            WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf, WindowCommand.BottomHalf,
            WindowCommand.TopLeftQuarter, WindowCommand.TopRightQuarter,
            WindowCommand.BottomLeftQuarter, WindowCommand.BottomRightQuarter,
            WindowCommand.Maximize, WindowCommand.GrowRight, WindowCommand.GrowDown,
            WindowCommand.ShrinkLeft, WindowCommand.ShrinkUp,
            WindowCommand.MoveCellLeft, WindowCommand.MoveCellRight,
            WindowCommand.MoveCellUp, WindowCommand.MoveCellDown,
            WindowCommand.NextMonitor, WindowCommand.PreviousMonitor, WindowCommand.ToggleEnabled,
        ];
        foreach (var command in commands)
        {
            var name = HotkeyCatalog.GetDisplayName(command);
            Assert.NotEqual(command.ToString(), name);
            Assert.Matches("[ぁ-んァ-ヶ一-龠]", name);
        }

        Assert.Equal(commands, commands.Reverse().OrderBy(HotkeyCatalog.GetDisplayOrder));
        Assert.Equal("左半分", HotkeyCatalog.GetDisplayName(WindowCommand.LeftHalf));
        Assert.Equal("左上の四分割", HotkeyCatalog.GetDisplayName(WindowCommand.TopLeftQuarter));
        Assert.Equal("右へ 1 区画広げる", HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight));
        Assert.Equal("次のモニターへ", HotkeyCatalog.GetDisplayName(WindowCommand.NextMonitor));
        Assert.Equal("一時停止・再開", HotkeyCatalog.GetDisplayName(WindowCommand.ToggleEnabled));
        Assert.Equal("999", HotkeyCatalog.GetDisplayName((WindowCommand)999));
    }
}
