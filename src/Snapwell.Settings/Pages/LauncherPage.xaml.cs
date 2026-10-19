using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Windows.Foundation;

namespace Snapwell.Settings.Pages;

/// <summary>表示するプリセットとカタログ順のプレビューを編集する。</summary>
public sealed partial class LauncherPage : Page
{
    private readonly SettingsSession _session;
    private readonly Dictionary<string, CheckBox> _choices = [];
    private readonly PresetWrapPanel _preview = new();
    private bool _refreshing;

    /// <summary>既存の設定セッションへ変更を渡す。</summary>
    public LauncherPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        Preview.Children.Add(_preview);
        IsEnabled = !session.IsReadOnly;
        foreach (var group in LauncherPreset.All.GroupBy(preset => preset.Group))
        {
            var section = new Grid { ColumnSpacing = 12, Padding = new(0, 8, 0, 0) };
            section.ColumnDefinitions.Add(new() { Width = new(110) });
            section.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            section.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)Application.Current.Resources["SnapwellHintTextStyle"],
            });
            var options = new PresetWrapPanel();
            Grid.SetColumn(options, 1);
            foreach (var preset in group)
            {
                var choice = new CheckBox
                {
                    Content = ContentFor(preset),
                    Tag = preset.Id,
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new(0),
                    FontSize = 13,
                };
                choice.Checked += Choice_Changed;
                choice.Unchecked += Choice_Changed;
                _choices.Add(preset.Id, choice);
                options.Children.Add(new Border { Child = choice, Style = (Style)Resources["PresetOptionBorder"] });
            }

            section.Children.Add(options);
            Groups.Children.Add(section);
        }

        Refresh();
    }

    private static StackPanel ContentFor(LauncherPreset preset)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        // ThemeResource は XAML の式として保持し、開いたままのテーマ変更にも追従する。
        var fill = preset.KeepSize
            ? "Fill='Transparent' Stroke='{ThemeResource SnapwellMutedBrush}' StrokeDashArray='1.5,1'"
            : "Fill='{ThemeResource SnapwellMutedBrush}'";
        var icon = (Canvas)Microsoft.UI.Xaml.Markup.XamlReader.Load(FormattableString.Invariant($$"""
            <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="18" Height="12">
                <Rectangle Canvas.Left="0.5" Canvas.Top="0.5" Width="17" Height="11" RadiusX="1.5" RadiusY="1.5"
                           Stroke="{ThemeResource SnapwellMutedBrush}" StrokeThickness="1" />
                <Rectangle Canvas.Left="{{1.5 + preset.X0 * 15}}" Canvas.Top="{{1.5 + preset.Y0 * 9}}"
                           Width="{{(preset.X1 - preset.X0) * 15}}" Height="{{(preset.Y1 - preset.Y0) * 9}}"
                           RadiusX="0.5" RadiusY="0.5" {{fill}} />
            </Canvas>
            """));
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = preset.Name });
        return content;
    }

    private void Refresh()
    {
        _refreshing = true;
        var ids = _session.Current.LauncherPresets;
        foreach (var (id, choice) in _choices)
        {
            choice.IsChecked = ids.Contains(id);
            ((Border)choice.Parent).Style = (Style)Resources[
                choice.IsChecked == true ? "PresetOptionSelectedBorder" : "PresetOptionBorder"];
        }
        Count.Text = $"{ids.Length} / {LauncherPreset.All.Count} 件を表示";
        _preview.Children.Clear();
        foreach (var preset in LauncherPreset.All.Where(preset => ids.Contains(preset.Id)))
        {
            _preview.Children.Add(new Button
            {
                Content = ContentFor(preset),
                Style = (Style)Application.Current.Resources["SnapwellButtonStyle"],
                Padding = new(8, 4, 8, 4),
                FontSize = 12,
                Margin = new(0, 0, 6, 6),
                IsEnabled = preset.KeepSize || preset.RangeFor(GridPlan.EightByEight) is not null,
            });
        }

        EmptyHint.Visibility = ids.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _refreshing = false;
    }

    private void SetPresets(IEnumerable<string> ids)
    {
        _session.Update(_session.Current with { LauncherPresets = LauncherPreset.NormalizeIds(ids) });
        Refresh();
    }

    private void Choice_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) { SetPresets(_choices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key)); }
    }

    private void All_Click(object sender, RoutedEventArgs e) => SetPresets(LauncherPreset.All.Select(preset => preset.Id));
    private void None_Click(object sender, RoutedEventArgs e) => SetPresets([]);
    private void Default_Click(object sender, RoutedEventArgs e) => SetPresets(LauncherPreset.DefaultIds);
}

/// <summary>モックアップの flex-wrap と同じく、項目ごとの幅で折り返す。</summary>
internal sealed partial class PresetWrapPanel : Panel
{
    /// <summary>幅をそろえずに各項目を測り、行の高さを合計する。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) { child.Measure(new(availableSize.Width, double.PositiveInfinity)); }
        return ArrangeItems(availableSize.Width, false);
    }

    /// <summary>測定した項目を左から並べ、幅を超える直前で折り返す。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        ArrangeItems(finalSize.Width, true);
        return finalSize;
    }

    private Size ArrangeItems(double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }

            if (arrange) { child.Arrange(new(x, y, size.Width, size.Height)); }
            x += size.Width;
            usedWidth = Math.Max(usedWidth, x);
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return new(usedWidth, y + rowHeight);
    }
}
