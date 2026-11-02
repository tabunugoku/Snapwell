using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>表示する版からビルド情報だけを取り除くことを確かめる。</summary>
public sealed class DisplayVersionTests
{
    /// <summary>空白と最初の + 以降を除き、版の接頭辞とプレリリースは残す。</summary>
    [Theory]
    [InlineData("1.0.3", "1.0.3")]
    [InlineData("1.0.3+abc123", "1.0.3")]
    [InlineData("1.1.0-beta.1+abc", "1.1.0-beta.1")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("+abc", null)]
    [InlineData("1.0.3+abc+def", "1.0.3")]
    [InlineData("  v1.1.0-beta.1 +abc  ", "v1.1.0-beta.1")]
    [InlineData("  1.0.3  ", "1.0.3")]
    [InlineData("  +abc  ", null)]
    public void FormatRemovesBuildMetadataAndWhitespaceWithoutChangingVersion(string? input, string? expected)
    {
        Assert.Equal(expected, DisplayVersion.Format(input));
    }
}
