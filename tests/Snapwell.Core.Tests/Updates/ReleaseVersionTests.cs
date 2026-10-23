using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>SemVer の優先順位と入力の境界を検証する。</summary>
public sealed class ReleaseVersionTests
{
    /// <summary>正式版と SemVer のプレリリースの例を順番どおりに比較する。</summary>
    [Theory]
    [InlineData("1.0.0", "1.0.1", "1.1.0", "2.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
        "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0")]
    public void VersionsFollowSemVerPrecedence(params string[] ordered)
    {
        for (var i = 1; i < ordered.Length; i++)
        {
            var earlier = Parse(ordered[i - 1]);
            var later = Parse(ordered[i]);
            Assert.True(earlier.CompareTo(later) < 0);
            Assert.True(later.CompareTo(earlier) > 0);
        }
    }

    /// <summary>ビルド情報は比較・等価性・ハッシュ値に影響しない。</summary>
    [Fact]
    public void BuildMetadataDoesNotAffectPrecedenceOrEquality()
    {
        var left = Parse("1.0.0+a");
        var right = Parse("1.0.0+b");
        Assert.Equal(0, left.CompareTo(right));
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(Parse("1.0.0+001.000"), Parse("1.0.0"));
    }

    /// <summary>先頭の v を取り除き識別子を保持する。</summary>
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("v1.2.3-rc.1+abc.001", "1.2.3-rc.1+abc.001")]
    [InlineData("9223372036854775807.0.0", "9223372036854775807.0.0")]
    public void ValidVersionsRoundTripWithoutLeadingV(string input, string expected)
        => Assert.Equal(expected, Parse(input).ToString());

    /// <summary>構文違反、数値の先頭ゼロ、桁あふれを拒否する。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+a..b")]
    [InlineData("1.2.3+a+b")]
    [InlineData("1.2.3-あ")]
    [InlineData("1.2.3 ")]
    [InlineData("1.2.3\0")]
    [InlineData(" 1.2.3")]
    [InlineData("V1.2.3")]
    [InlineData("v")]
    [InlineData("9223372036854775808.0.0")]
    [InlineData("1.9223372036854775808.0")]
    [InlineData("1.0.9223372036854775808")]
    [InlineData("1.0.0-9223372036854775808")]
    public void InvalidVersionsReturnFalse(string? input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out var version));
        Assert.Null(version);
    }

    /// <summary>極端に長い識別子や数値も再帰や変換例外を使わず処理する。</summary>
    [Fact]
    public void LongIdentifiersAndOverflowDoNotThrow()
    {
        Assert.False(ReleaseVersion.TryParse(new string('9', 100000) + ".0.0", out _));
        Assert.False(ReleaseVersion.TryParse("1.0.0-" + new string('9', 100000), out _));
        var letters = new string('a', 100000);
        Assert.True(Parse("1.0.0-" + letters).CompareTo(Parse("1.0.0-" + letters + "b")) < 0);
        Assert.True(Parse("1.0.0-9").CompareTo(Parse("1.0.0-9223372036854775807")) < 0);
    }

    /// <summary>0.0.0 系だけを開発用ビルドとして扱う。</summary>
    [Theory]
    [InlineData("0.0.0", true)]
    [InlineData("0.0.0-dev", true)]
    [InlineData("0.0.0+build", true)]
    [InlineData("0.0.0-dev+build", true)]
    [InlineData("0.0.1", false)]
    public void DevelopmentBuildDependsOnCoreVersion(string input, bool expected)
        => Assert.Equal(expected, Parse(input).IsDevelopmentBuild);

    private static ReleaseVersion Parse(string input)
    {
        Assert.True(ReleaseVersion.TryParse(input, out var version));
        return version!;
    }
}
