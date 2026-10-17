using Snapwell.Core.Commands;

namespace Snapwell.Core.Tests.Commands;

public sealed class HotkeyCatalogTests
{
    [Fact]
    public void EveryCommandHasADisplayNameIncludingOpenLauncher()
    {
        Assert.Equal("ランチャーを開く", HotkeyCatalog.GetDisplayName(WindowCommand.OpenLauncher));
        foreach (var command in Enum.GetValues<WindowCommand>())
        {
            Assert.NotEqual(command.ToString(), HotkeyCatalog.GetDisplayName(command));
        }
    }

    [Fact]
    public void DefaultsHaveNoConflicts() => Assert.Empty(HotkeyCatalog.FindConflicts(DefaultHotkeys.Create()));

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
