using Snapwell.Core.Commands;

namespace Snapwell.Core.Tests.Commands;

public sealed class HotkeyEditingTests
{
    private static readonly KeyChordSpec _ctrlAltA = new(ChordModifiers.Control | ChordModifiers.Alt, 0x41);
    private static readonly KeyChordSpec _ctrlAltB = new(ChordModifiers.Control | ChordModifiers.Alt, 0x42);
    private static readonly KeyChordSpec _ctrlAltD = new(ChordModifiers.Control | ChordModifiers.Alt, 0x44);

    /// <summary>修飾キーなし・修飾キーだけ・範囲外の仮想キーは割り当てられず、通常の組だけが割り当てられる。</summary>
    [Theory]
    [InlineData(ChordModifiers.None, 0x41u, false)]
    [InlineData(ChordModifiers.Control, 0x41u, true)]
    [InlineData(ChordModifiers.Win | ChordModifiers.Shift, 0x27u, true)]
    [InlineData(ChordModifiers.Control, 0x10u, false)]
    [InlineData(ChordModifiers.Control, 0x11u, false)]
    [InlineData(ChordModifiers.Control, 0x12u, false)]
    [InlineData(ChordModifiers.Control, 0x5Bu, false)]
    [InlineData(ChordModifiers.Control, 0x5Cu, false)]
    [InlineData(ChordModifiers.Alt, 0xA0u, false)]
    [InlineData(ChordModifiers.Alt, 0xA5u, false)]
    [InlineData(ChordModifiers.Alt, 0xA6u, true)]
    [InlineData(ChordModifiers.Control, 0u, false)]
    [InlineData(ChordModifiers.Control, 1u, true)]
    [InlineData(ChordModifiers.Control, 255u, true)]
    [InlineData(ChordModifiers.Control, 256u, false)]
    [InlineData((ChordModifiers)16, 0x41u, false)]
    public void IsAssignableRequiresModifierAndNonModifierKeyInRange(ChordModifiers modifiers, uint key, bool expected) =>
        Assert.Equal(expected, HotkeyEditing.IsAssignable(new(modifiers, key)));

    /// <summary>自分以外で同じキーを持つ操作だけを、挿入順ではなく表示順で返す。</summary>
    [Fact]
    public void FindOthersExcludesTheCommandItselfAndFollowsDisplayOrder()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.SnapAdjacentDown] = _ctrlAltA,
            [WindowCommand.ToggleEnabled] = _ctrlAltA,
            [WindowCommand.LeftHalf] = _ctrlAltA,
            [WindowCommand.Maximize] = _ctrlAltB,
        };
        Assert.Equal([WindowCommand.SnapAdjacentDown, WindowCommand.ToggleEnabled],
            HotkeyEditing.FindOthers(hotkeys, WindowCommand.LeftHalf, _ctrlAltA));
        Assert.Equal([WindowCommand.LeftHalf, WindowCommand.SnapAdjacentDown, WindowCommand.ToggleEnabled],
            HotkeyEditing.FindOthers(hotkeys, WindowCommand.RightHalf, _ctrlAltA));
        Assert.Empty(HotkeyEditing.FindOthers(hotkeys, WindowCommand.RightHalf, _ctrlAltD));
        Assert.Empty(HotkeyEditing.FindOthers(hotkeys, WindowCommand.Maximize, _ctrlAltB));
    }

    /// <summary>重複を残す割り当ては、ほかの操作に触らず、元の辞書も変えない。</summary>
    [Fact]
    public void AssignKeepLeavesDuplicateAndDoesNotChangeOriginal()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = _ctrlAltA,
            [WindowCommand.RightHalf] = _ctrlAltB,
        };
        var result = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltB, AssignMode.Keep);
        Assert.Equal(_ctrlAltB, result[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltB, result[WindowCommand.RightHalf]);
        Assert.Equal(_ctrlAltA, hotkeys[WindowCommand.LeftHalf]);
        Assert.NotSame(hotkeys, result);
    }

    /// <summary>入れ替えでは、同じキーを持っていた操作へ元のキーが渡り、重複が残らない。</summary>
    [Fact]
    public void AssignSwapGivesOriginalChordToTheOtherCommand()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = _ctrlAltA,
            [WindowCommand.RightHalf] = _ctrlAltB,
        };
        var result = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltB, AssignMode.Swap);
        Assert.Equal(_ctrlAltB, result[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltA, result[WindowCommand.RightHalf]);
        Assert.Empty(HotkeyCatalog.FindConflicts(result));
        Assert.Equal(_ctrlAltA, hotkeys[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltB, hotkeys[WindowCommand.RightHalf]);
    }

    /// <summary>元のキーが無い操作を入れ替えると、相手は未割り当てになる。</summary>
    [Fact]
    public void AssignSwapFromUnassignedCommandClearsTheOtherCommand()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec> { [WindowCommand.RightHalf] = _ctrlAltB };
        var result = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltB, AssignMode.Swap);
        Assert.Equal(_ctrlAltB, result[WindowCommand.LeftHalf]);
        Assert.DoesNotContain(WindowCommand.RightHalf, result.Keys);
        Assert.Equal(_ctrlAltB, hotkeys[WindowCommand.RightHalf]);
        Assert.DoesNotContain(WindowCommand.LeftHalf, hotkeys.Keys);
    }

    /// <summary>未割り当ての操作へ、重複の無いキーを割り当てられる。</summary>
    [Theory]
    [InlineData(AssignMode.Keep)]
    [InlineData(AssignMode.Swap)]
    public void AssignToUnassignedCommandAddsOnlyThatCommand(AssignMode mode)
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec> { [WindowCommand.RightHalf] = _ctrlAltB };
        var result = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltA, mode);
        Assert.Equal(2, result.Count);
        Assert.Equal(_ctrlAltA, result[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltB, result[WindowCommand.RightHalf]);
        Assert.Single(hotkeys);
    }

    /// <summary>同じキーを 3 つ以上が持つ設定で入れ替えると、全員が元のキーを受け取り、操作したものは新しいキーを一つだけ持つ。</summary>
    [Fact]
    public void AssignSwapWithThreeSharingCommandsMovesEveryOtherToOriginalChord()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = _ctrlAltA,
            [WindowCommand.RightHalf] = _ctrlAltB,
            [WindowCommand.TopHalf] = _ctrlAltB,
            [WindowCommand.BottomHalf] = _ctrlAltB,
        };
        var result = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltB, AssignMode.Swap);
        Assert.Equal(_ctrlAltB, result[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltA, result[WindowCommand.RightHalf]);
        Assert.Equal(_ctrlAltA, result[WindowCommand.TopHalf]);
        Assert.Equal(_ctrlAltA, result[WindowCommand.BottomHalf]);
        Assert.Empty(HotkeyEditing.FindOthers(result, WindowCommand.LeftHalf, _ctrlAltB));

        var kept = HotkeyEditing.Assign(hotkeys, WindowCommand.LeftHalf, _ctrlAltB, AssignMode.Keep);
        Assert.Equal(3, HotkeyEditing.FindOthers(kept, WindowCommand.LeftHalf, _ctrlAltB).Count);
    }

    /// <summary>キーを外すと未割り当てになり、元の辞書や外していない操作は変わらない。</summary>
    [Fact]
    public void ClearRemovesOnlyThatCommandAndKeepsOriginal()
    {
        var hotkeys = DefaultHotkeys.Create();
        var result = HotkeyEditing.Clear(hotkeys, WindowCommand.LeftHalf);
        Assert.DoesNotContain(WindowCommand.LeftHalf, result.Keys);
        Assert.Equal(hotkeys.Count - 1, result.Count);
        Assert.Contains(WindowCommand.LeftHalf, hotkeys.Keys);
        Assert.Equal(hotkeys.Count, HotkeyEditing.Clear(hotkeys, WindowCommand.MoveCellLeft).Count);
    }

    /// <summary>既定のある操作は既定のキーへ戻り、既定の無い操作は外れ、元の辞書は変わらない。</summary>
    [Fact]
    public void ResetRestoresDefaultOrRemovesCommandWithoutDefault()
    {
        var hotkeys = new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = _ctrlAltD,
            [WindowCommand.MoveCellLeft] = _ctrlAltD,
        };
        var restored = HotkeyEditing.Reset(hotkeys, WindowCommand.LeftHalf);
        Assert.Equal(DefaultHotkeys.Create()[WindowCommand.LeftHalf], restored[WindowCommand.LeftHalf]);
        Assert.Equal(_ctrlAltD, hotkeys[WindowCommand.LeftHalf]);

        var removed = HotkeyEditing.Reset(hotkeys, WindowCommand.MoveCellLeft);
        Assert.DoesNotContain(WindowCommand.MoveCellLeft, removed.Keys);
        Assert.Contains(WindowCommand.MoveCellLeft, hotkeys.Keys);
    }

    /// <summary>利用者が外した（既定が空でない）操作でも、既定のキーへ戻せる。</summary>
    [Fact]
    public void ResetRestoresDefaultForCommandThatWasCleared()
    {
        var hotkeys = HotkeyEditing.Clear(DefaultHotkeys.Create(), WindowCommand.CenterWindow);
        var restored = HotkeyEditing.Reset(hotkeys, WindowCommand.CenterWindow);
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt, 0x43),
            restored[WindowCommand.CenterWindow]);
    }

    /// <summary>既定との一致は、既定のある操作ではキーの一致、既定の無い操作では未割り当てで決まる。</summary>
    [Fact]
    public void IsDefaultComparesWithDefaultOrUnassignedForCommandsWithoutDefault()
    {
        var hotkeys = DefaultHotkeys.Create();
        Assert.True(HotkeyEditing.IsDefault(hotkeys, WindowCommand.LeftHalf));
        Assert.True(HotkeyEditing.IsDefault(hotkeys, WindowCommand.MoveCellLeft));

        hotkeys[WindowCommand.LeftHalf] = _ctrlAltD;
        hotkeys[WindowCommand.MoveCellLeft] = _ctrlAltA;
        Assert.False(HotkeyEditing.IsDefault(hotkeys, WindowCommand.LeftHalf));
        Assert.False(HotkeyEditing.IsDefault(hotkeys, WindowCommand.MoveCellLeft));

        hotkeys.Remove(WindowCommand.LeftHalf);
        Assert.False(HotkeyEditing.IsDefault(hotkeys, WindowCommand.LeftHalf));
    }
}
