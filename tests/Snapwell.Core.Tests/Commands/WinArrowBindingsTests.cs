using Snapwell.Core.Commands;

namespace Snapwell.Core.Tests.Commands;

/// <summary>固定の半分操作と利用者の割り当ての優先を確かめる。</summary>
public sealed class WinArrowBindingsTests
{
    /// <summary>Win だけの修飾で四方向の半分を追加する。</summary>
    [Fact]
    public void FixedBindingsUseOnlyWinAndTheFourHalves()
    {
        var bindings = WinArrowBindings.WithoutConflicts([]).ToDictionary();
        Assert.Equal(4, bindings.Count);
        Assert.Equal(new(ChordModifiers.Win, 0x25), bindings[WindowCommand.LeftHalf]);
        Assert.Equal(new(ChordModifiers.Win, 0x27), bindings[WindowCommand.RightHalf]);
        Assert.Equal(new(ChordModifiers.Win, 0x26), bindings[WindowCommand.TopHalf]);
        Assert.Equal(new(ChordModifiers.Win, 0x28), bindings[WindowCommand.BottomHalf]);
    }

    /// <summary>一つの競合を除外しても、次の呼び出しの固定一覧は変わらない。</summary>
    [Fact]
    public void UserWinLeftRemovesOnlyThatReplacement()
    {
        var bindings = WinArrowBindings.WithoutConflicts([new(ChordModifiers.Win, 0x25)]);
        Assert.Equal(3, bindings.Count);
        Assert.DoesNotContain(bindings, binding => binding.Key == WindowCommand.LeftHalf);
        Assert.Equal(4, WinArrowBindings.WithoutConflicts([]).Count);
    }

    /// <summary>ほかの修飾キーを足した利用者の指定とは重ならない。</summary>
    [Theory]
    [InlineData(ChordModifiers.Control)]
    [InlineData(ChordModifiers.Alt)]
    [InlineData(ChordModifiers.Shift)]
    public void AdditionalModifiersDoNotConflict(ChordModifiers extra)
    {
        Assert.Equal(4, WinArrowBindings.WithoutConflicts([new(ChordModifiers.Win | extra, 0x25)]).Count);
    }
}
