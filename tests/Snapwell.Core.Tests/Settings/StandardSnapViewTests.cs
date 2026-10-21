using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>標準スナップの表示は一時停止では変わらず、選択と退避値で決まる。</summary>
public sealed class StandardSnapViewTests
{
    /// <summary>停止中は退避値の有無によらず復元できる。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void DisabledView(bool enabled, bool? saved)
    {
        var view = StandardSnapView.Create(new() { Enabled = enabled, DisableStandardSnap = true }, saved);

        Assert.True(view.ToggleOn);
        Assert.Equal("連動して停止", view.SubTagText);
        Assert.True(view.RestoreEnabled);
    }

    /// <summary>停止していない場合も、退避値があれば復元を要求できる。</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, null, false)]
    [InlineData(false, null, false)]
    public void StandardView(bool enabled, bool? saved, bool restoreEnabled)
    {
        var view = StandardSnapView.Create(new() { Enabled = enabled }, saved);

        Assert.False(view.ToggleOn);
        Assert.Equal("標準のまま", view.SubTagText);
        Assert.Equal(restoreEnabled, view.RestoreEnabled);
    }

    /// <summary>Win と矢印だけを置き換えている場合も、復元できることと現在の選択を表示する。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void ReplacementOnlyView(bool enabled, bool? saved)
    {
        var view = StandardSnapView.Create(new() { Enabled = enabled, ReplaceWinArrow = true }, saved);

        Assert.False(view.ToggleOn);
        Assert.Equal("標準のまま", view.SubTagText);
        Assert.True(view.RestoreEnabled);
    }
}
