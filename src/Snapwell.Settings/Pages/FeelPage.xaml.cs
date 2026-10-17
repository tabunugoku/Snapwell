using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Snapping;

namespace Snapwell.Settings.Pages;

/// <summary>物理モデルと吸着の方式を編集する。</summary>
public sealed partial class FeelPage : Page
{
    private readonly SettingsSession _session;
    private readonly bool _refreshing = true;

    /// <summary>現在の値を標準コントロールへ設定する。</summary>
    public FeelPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        var value = session.Current;
        IsEnabled = !session.IsReadOnly;
        FeelBox.SelectedIndex = value.Feel switch { "Snappy" => 1, "Heavy" => 2, "Bouncy" => 3, _ => 0 };
        ModeBox.SelectedIndex = value.SnapMode == SnapMode.Corners ? 1 : 0;
        MotionSwitch.IsOn = value.ReduceMotion;
        _refreshing = false;
    }

    private void Feel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && FeelBox.SelectedItem is ComboBoxItem { Tag: string feel })
        {
            _session.Update(_session.Current with { Feel = feel });
        }
    }

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
        {
            var mode = ModeBox.SelectedIndex == 1 ? SnapMode.Corners : SnapMode.Zones;
            _session.Update(_session.Current with { SnapMode = mode });
        }
    }

    private void Motion_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with { ReduceMotion = MotionSwitch.IsOn });
        }
    }
}
