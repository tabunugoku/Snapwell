using System.Globalization;
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
        var accent = Regex.Match(palette,
            @"internal Color Accent => Dark \? Color.FromArgb\(([^)]+)\) : Color.FromArgb\(([^)]+)\)");
        Assert.True(accent.Success);
        var rgb = accent.Groups[dark ? 1 : 2].Value.Split(", ").Skip(1);
        foreach (var (name, expected) in new[] { ("ZoneFill", fill), ("AccentLine", line) })
        {
            var brush = Regex.Match(palette, $@"internal SolidColorBrush {name} => Cached\(""\w+"", " +
                @"\(\) => new\(Color.FromArgb\(\s*Dark \? \(byte\)(\d+) : \(byte\)(\d+), " +
                @"Accent.R, Accent.G, Accent.B\)\)\)");
            Assert.True(brush.Success, $"{name}: IntroPalette の RGB を参照する。");
            Assert.Equal(expected, string.Join(", ", rgb.Append(brush.Groups[dark ? 1 : 2].Value)));
        }
        Assert.Contains("shape.Stroke = selected ? _palette.AccentLine : _palette.Border;", stage, StringComparison.Ordinal);
        Assert.Contains("shape.Fill = selected ? _palette.ZoneFill : _clear;", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity = _palette.Dark", stage, StringComparison.Ordinal);
    }

    /// <summary>点線の倍率を線幅に掛けた長さが 4/4 になり、舞台の面の角は 8 になる。</summary>
    [Fact]
    public void WelcomeStageUsesFourPixelDashesAndEightPixelSurfaceCorners()
    {
        var stage = File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(),
            "src/Snapwell.Settings/Setup/WelcomeStage.cs"));
        var stroke = Regex.Match(stage, @"StrokeThickness = ([\d.]+)");
        var dashes = Regex.Match(stage, @"StrokeDashArray = \[([\d.]+), ([\d.]+)\]");
        Assert.True(stroke.Success);
        Assert.True(dashes.Success);
        var thickness = double.Parse(stroke.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(1.5, thickness);
        foreach (var index in new[] { 1, 2 })
        {
            Assert.Equal(4, thickness * double.Parse(dashes.Groups[index].Value, CultureInfo.InvariantCulture), 5);
        }
        Assert.Contains("CornerRadius = new(8)", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius = new(10)", stage, StringComparison.Ordinal);
    }

    /// <summary>毎回の描画でブラシを作らず、テーマが変わるまで同じものを使う。</summary>
    [Fact]
    public void WelcomeZoneBrushesUseThemeInvalidatedPaletteCache()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/Setup");
        var stage = File.ReadAllText(Path.Combine(folder, "WelcomeStage.cs"));
        var palette = File.ReadAllText(Path.Combine(folder, "IntroPalette.cs"));
        var paint = stage[stage.IndexOf("private void Paint()", StringComparison.Ordinal)..];
        Assert.DoesNotContain("new SolidColorBrush", paint, StringComparison.Ordinal);
        Assert.Matches(@"internal SolidColorBrush ZoneFill => Cached\(""zoneFill""", palette);
        Assert.Matches(@"internal SolidColorBrush AccentLine => Cached\(""accentLine""", palette);
        Assert.Contains("if (_cachedDark != Dark) { _cache.Clear(); _cachedDark = Dark; }", palette,
            StringComparison.Ordinal);
        Assert.Contains("if (!_cache.TryGetValue(key, out var brush)) { _cache[key] = brush = create(); }", palette,
            StringComparison.Ordinal);
        Assert.Contains("ActualThemeChanged += OnThemeChanged;", stage, StringComparison.Ordinal);
        Assert.Contains("private void OnThemeChanged(FrameworkElement sender, object args) => Paint();", stage,
            StringComparison.Ordinal);
    }
}
