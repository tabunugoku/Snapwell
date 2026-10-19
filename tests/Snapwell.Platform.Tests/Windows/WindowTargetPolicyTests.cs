using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Tests.Windows;

/// <summary>分類と設定から、ホットキーとドラッグの対象かを決める判断の検証。</summary>
public sealed class WindowTargetPolicyTests
{
    /// <summary>通常は常に対象、除外は常に対象外。サイズ固定とポップアップは自分の設定だけに従う。</summary>
    [Theory]
    [InlineData(WindowKind.Normal, true, true, true)]
    [InlineData(WindowKind.Normal, false, false, true)]
    [InlineData(WindowKind.FixedSize, true, false, true)]
    [InlineData(WindowKind.FixedSize, false, true, false)]
    [InlineData(WindowKind.Popup, false, true, true)]
    [InlineData(WindowKind.Popup, true, false, false)]
    [InlineData(WindowKind.Excluded, true, true, false)]
    public void IsTargetFollowsOnlyTheMatchingSetting(WindowKind kind, bool fixedSize, bool popup, bool expected)
    {
        var settings = new PortableSettings { MoveFixedSizeWindows = fixedSize, MovePopupWindows = popup };
        Assert.Equal(expected, WindowTargetPolicy.IsTarget(kind, () => settings));
    }
}
