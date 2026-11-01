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

    /// <summary>舞台の塗りと点線は共通の RGB と、用途ごとの透明度を組み合わせる。</summary>
    [Theory]
    [InlineData(false, "31, 108, 159, 46", "31, 108, 159, 153")]
    [InlineData(true, "140, 203, 242, 41", "140, 203, 242, 166")]
    public void WelcomeZonesUsePaletteAccentWithApprovedFillAndLineAlpha(bool dark, string fill, string line)
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/Setup");
        var stage = File.ReadAllText(Path.Combine(folder, "WelcomeStage.cs"));
        var palette = File.ReadAllText(Path.Combine(folder, "IntroPalette.cs"));
        Assert.Contains("var accent = _palette.Accent;", stage, StringComparison.Ordinal);
        var accent = Regex.Match(palette,
            @"internal Color Accent => Dark \? Color.FromArgb\(([^)]+)\) : Color.FromArgb\(([^)]+)\)");
        Assert.True(accent.Success);
        var rgb = accent.Groups[dark ? 1 : 2].Value.Split(", ").Skip(1);
        foreach (var (name, expected) in new[] { ("zoneFill", fill), ("accentLine", line) })
        {
            var brush = Regex.Match(stage, $@"var {name} = new SolidColorBrush\(Color.FromArgb\(\s*" +
                @"_palette.Dark \? \(byte\)(\d+) : \(byte\)(\d+), accent.R, accent.G, accent.B\)\)");
            Assert.True(brush.Success, $"{name}: IntroPalette の RGB を参照する。");
            Assert.Equal(expected, string.Join(", ", rgb.Append(brush.Groups[dark ? 1 : 2].Value)));
        }
        Assert.Contains("shape.Stroke = selected ? accentLine : _palette.Border;", stage, StringComparison.Ordinal);
        Assert.Contains("shape.Fill = selected ? zoneFill : _clear;", stage, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray = [4, 4]", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity = _palette.Dark", stage, StringComparison.Ordinal);
    }
}
