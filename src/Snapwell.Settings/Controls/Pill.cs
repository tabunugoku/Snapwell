using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Snapwell.Settings.Controls;

/// <summary>高さの半分を角丸の半径にするカプセル型を Border に適用する添付プロパティ。</summary>
public static class Pill
{
    /// <summary>AutoRadius 添付プロパティ。</summary>
    public static readonly DependencyProperty AutoRadiusProperty =
        DependencyProperty.RegisterAttached(
            "AutoRadius",
            typeof(bool),
            typeof(Pill),
            new PropertyMetadata(false, OnAutoRadiusChanged));

    /// <summary>AutoRadius 添付プロパティの値を取得する。</summary>
    public static bool GetAutoRadius(DependencyObject element) =>
        (bool)element.GetValue(AutoRadiusProperty);

    /// <summary>AutoRadius 添付プロパティの値を設定する。</summary>
    public static void SetAutoRadius(DependencyObject element, bool value) =>
        element.SetValue(AutoRadiusProperty, value);

    private static void OnAutoRadiusChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Border border)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            border.SizeChanged += Border_SizeChanged;
            UpdateRadius(border);
        }
        else
        {
            border.SizeChanged -= Border_SizeChanged;
        }
    }

    private static void Border_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border)
        {
            UpdateRadius(border);
        }
    }

    private static void UpdateRadius(Border border)
    {
        // WinUI の Border は大きすぎる半径で楕円になるため、CSS と同じく高さの半分で切り詰める。
        var radius = border.ActualHeight / 2.0;
        border.CornerRadius = new CornerRadius(radius);
    }
}
