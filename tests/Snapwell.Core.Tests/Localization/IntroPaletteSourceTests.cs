using System.Text.RegularExpressions;

namespace Snapwell.Core.Tests.Localization;

/// <summary>WinUI を起動せず、導入のアクセントを区画の青と照合する。</summary>
public sealed class IntroPaletteSourceTests
{
    /// <summary>導入の点と窓のアクセントが、ライト・ダークとも共通の青を使う。</summary>
    [Theory]
    [InlineData(false, "255, 31, 108, 159")]
    [InlineData(true, "255, 140, 203, 242")]
    public void IntroAccentMatchesSharedZoneAccent(bool dark, string expectedArgb)
    {
        var source = File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(),
            "src/Snapwell.Settings/Setup/IntroPalette.cs"));
        var accent = Regex.Match(source,
            @"internal Color Accent => Dark \? Color.FromArgb\(([^)]+)\) : Color.FromArgb\(([^)]+)\)");
        Assert.True(accent.Success);
        Assert.Equal(expectedArgb, accent.Groups[dark ? 1 : 2].Value);
        Assert.DoesNotContain("106, 168, 255", source, StringComparison.Ordinal);
        Assert.DoesNotContain("37, 99, 235", source, StringComparison.Ordinal);
    }
}
