using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>ポインター時刻の一周と符号付き差の境界を検証する。</summary>
public sealed class PointerOrderingTests
{
    /// <summary>基準以前の入力だけを選び、一周をまたいでも前後を保つ。</summary>
    [Theory]
    [InlineData(99u, 100u, true)]
    [InlineData(100u, 100u, true)]
    [InlineData(101u, 100u, false)]
    [InlineData(uint.MaxValue - 3, 5u, true)]
    [InlineData(10u, 5u, false)]
    [InlineData(5u, uint.MaxValue - 3, false)]
    [InlineData(2147483646u, 0u, false)]
    [InlineData(2147483647u, 0u, false)]
    [InlineData(2147483648u, 0u, true)]
    [InlineData(2147483649u, 0u, true)]
    [InlineData(0u, 2147483647u, true)]
    [InlineData(0u, 2147483648u, true)]
    [InlineData(0u, 2147483649u, false)]
    public void SignedDifferenceOrdersTicksAcrossWrap(uint eventMs, uint referenceMs, bool expected)
    {
        Assert.Equal(expected, PointerOrdering.IsAtOrBefore(eventMs, referenceMs));
    }
}
