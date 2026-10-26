using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Snapwell.Settings.Setup;

/// <summary>アプリのテーマから描画用のブラシを取り、青だけをモックアップの指定色にする。</summary>
internal sealed class IntroPalette(FrameworkElement owner)
{
    internal bool Dark => owner.ActualTheme == ElementTheme.Dark;
    internal Color Accent => Dark ? Color.FromArgb(255, 106, 168, 255) : Color.FromArgb(255, 37, 99, 235);
    internal SolidColorBrush AccentBrush => new(Accent);
    internal SolidColorBrush Background => Brush("SnapwellBackgroundBrush");
    internal SolidColorBrush Surface => Brush("SnapwellSurfaceBrush");
    internal SolidColorBrush Surface2 => Brush("SnapwellSurface2Brush");
    internal SolidColorBrush Border => Brush("SnapwellBorderStrongBrush");
    internal SolidColorBrush Skeleton => Brush("SnapwellSkeletonBrush");
    internal SolidColorBrush Ink => Brush("SnapwellInkBrush");
    internal SolidColorBrush Text => Brush("SnapwellTextBrush");

    private SolidColorBrush Brush(string key) => (SolidColorBrush)Find(Application.Current.Resources, key)!;

    private object? Find(ResourceDictionary dictionary, string key)
    {
        var theme = Dark ? "Dark" : "Light";
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed) &&
            themed is ResourceDictionary resources && resources.TryGetValue(key, out var value))
        {
            return value;
        }
        foreach (var merged in dictionary.MergedDictionaries.Reverse())
        {
            if (Find(merged, key) is { } found) { return found; }
        }
        if (dictionary.TryGetValue(key, out var direct)) { return direct; }
        return null;
    }
}
