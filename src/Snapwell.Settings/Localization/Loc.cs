using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Localization;

namespace Snapwell.Settings.Localization;

/// <summary>リフレクションを使わず、Uid に属する文言を XAML 要素へ適用する。</summary>
public static class Loc
{
    /// <summary>文言のキーの接頭辞。</summary>
    public static readonly DependencyProperty UidProperty = DependencyProperty.RegisterAttached(
        "Uid", typeof(string), typeof(Loc), new PropertyMetadata(null, OnUidChanged));

    /// <summary>要素に設定された Uid を返す。</summary>
    public static string? GetUid(DependencyObject element) => (string?)element.GetValue(UidProperty);

    /// <summary>要素の Uid を設定する。</summary>
    public static void SetUid(DependencyObject element, string value) => element.SetValue(UidProperty, value);

    private static void OnUidChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not string uid || uid.Length == 0) { return; }
        if (element is NavigationViewItem navigation)
        {
            // 属性を読む時点ではアイコンとラベルの子要素がまだないため、読み込み後に適用する。
            navigation.Loaded -= Navigation_Loaded;
            navigation.Loaded += Navigation_Loaded;
        }
        else { Apply(element, uid); }
    }

    private static void Navigation_Loaded(object sender, RoutedEventArgs args)
    {
        var element = (NavigationViewItem)sender;
        if (GetUid(element) is { } uid) { Apply(element, uid); }
    }

    private static void Apply(DependencyObject element, string uid)
    {
        var catalog = UiText.Current;
        foreach (var key in catalog.Keys.Where(key => key.StartsWith(uid + ".", StringComparison.Ordinal)))
        {
            var value = catalog.Get(key);
            switch (key[(uid.Length + 1)..], element)
            {
                case ("Text", TextBlock text): text.Text = value; break;
                case ("Content", Button button): button.Content = value; break;
                case ("Content", ComboBoxItem item): item.Content = value; break;
                case ("Content", RadioButton radio): radio.Content = value; break;
                case ("Content", CheckBox check): check.Content = value; break;
                case ("Content", ToolTip tip): tip.Content = value; break;
                case ("Content", Expander expander): expander.Content = value; break;
                case ("Content", NavigationViewItem { Content: Panel panel }):
                    // 既存の行のアイコンを保ち、内容のラベルだけを置き換える。
                    var label = panel.Children.OfType<TextBlock>().Single();
                    label.Text = value;
                    break;
                case ("Content", NavigationViewItem navigation): navigation.Content = value; break;
                case ("Header", ToggleSwitch toggle): toggle.Header = value; break;
                case ("Header", ComboBox combo): combo.Header = value; break;
                case ("Header", Slider slider): slider.Header = value; break;
                case ("Header", TextBox box): box.Header = value; break;
                case ("PlaceholderText", TextBox box): box.PlaceholderText = value; break;
                case ("Header", NumberBox box): box.Header = value; break;
                case ("PlaceholderText", NumberBox box): box.PlaceholderText = value; break;
                case ("Header", Expander expander): expander.Header = value; break;
                case ("PlaceholderText", ComboBox combo): combo.PlaceholderText = value; break;
                case ("Title", InfoBar info): info.Title = value; break;
                case ("Title", ContentDialog dialog): dialog.Title = value; break;
                case ("PrimaryButtonText", ContentDialog dialog): dialog.PrimaryButtonText = value; break;
                case ("SecondaryButtonText", ContentDialog dialog): dialog.SecondaryButtonText = value; break;
                case ("CloseButtonText", ContentDialog dialog): dialog.CloseButtonText = value; break;
                case ("Message", InfoBar info): info.Message = value; break;
                case ("ToolTip", FrameworkElement framework): ToolTipService.SetToolTip(framework, value); break;
                case ("AutomationName", _): AutomationProperties.SetName(element, value); break;
                default:
#if DEBUG
                    throw new InvalidOperationException($"対応していない文言のプロパティ: {key}");
#else
                    break;
#endif
            }
        }
    }
}
