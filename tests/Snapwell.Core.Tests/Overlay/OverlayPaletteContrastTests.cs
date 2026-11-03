using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>明暗の補助文字とホバー面を背景から区別できることを検証する。</summary>
public sealed class OverlayPaletteContrastTests
{
    /// <summary>補助文字は 4.5、ホバー面は 1.1 以上のコントラストを確保する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void MutedTextMeetsFourPointFiveAndHoverMeetsOnePointOneAgainstSurface(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        Assert.True(Contrast(palette.Muted, palette.Surface) >= 4.5);
        Assert.True(Contrast(palette.Hover, palette.Surface) >= 1.1);
    }

    /// <summary>共有の Surface2 とダークの補助文字を保ち、トレイ専用のホバー色を分ける。</summary>
    [Fact]
    public void LightMutedIs6B6A67AndDedicatedHoverPreservesSharedSurface2AndDarkMuted()
    {
        var light = OverlayPalette.For(OverlayTheme.Light);
        var dark = OverlayPalette.For(OverlayTheme.Dark);
        Assert.Equal(new(107, 106, 103, 255), light.Muted);
        Assert.Equal(new(155, 154, 151, 255), dark.Muted);
        Assert.Equal(new(240, 240, 238, 255), light.Hover);
        Assert.Equal(new(45, 45, 44, 255), dark.Hover);
        Assert.Equal(new(249, 249, 248, 255), light.Surface2);
        Assert.Equal(new(37, 37, 36, 255), dark.Surface2);
    }

    private static double Contrast(OverlayColor first, OverlayColor second)
    {
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + .05)
            / (Math.Min(firstLuminance, secondLuminance) + .05);
    }

    private static double Luminance(OverlayColor color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.Red) + .7152 * Linear(color.Green) + .0722 * Linear(color.Blue);
    }
}
