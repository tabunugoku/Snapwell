using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;
using Windows.Foundation;

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
        BuildCurve(AiryCurve, FeelPreset.Airy);
        BuildCurve(SnappyCurve, FeelPreset.Snappy);
        BuildCurve(HeavyCurve, FeelPreset.Heavy);
        BuildCurve(BouncyCurve, FeelPreset.Bouncy);

        var value = session.Current;
        IsEnabled = !session.IsReadOnly;
        FeelBox.SelectedIndex = value.Feel switch { "Snappy" => 1, "Heavy" => 2, "Bouncy" => 3, _ => 0 };
        ModeBox.SelectedIndex = value.SnapMode == SnapMode.Corners ? 1 : 0;
        MotionSwitch.IsOn = value.ReduceMotion;
        RefreshFeelDetails();
        _refreshing = false;
    }

    private static void BuildCurve(Polyline polyline, FeelPreset preset)
    {
        const double width = 160;
        const double height = 54;
        const double totalSeconds = 1.4;
        const int steps = 70;
        const double dt = totalSeconds / steps;
        var m = preset.Mass;
        var k = preset.Stiffness;
        var c = preset.Damping;
        var x = 0.0;
        var v = 0.0;

        // Points の既定値は実装によって null のことがあるため、毎回新しい集合を入れる。
        polyline.Points = new PointCollection();
        polyline.Points.Add(new Point(0, height - 4));
        for (var i = 1; i <= steps; i++)
        {
            for (var s = 0; s < 4; s++)
            {
                var f = -k * (x - 1.0) - c * v;
                v += f / m * (dt / 4.0);
                x += v * (dt / 4.0);
            }

            var px = i / (double)steps * width;
            var py = height - 4 - x * (height - 16);
            polyline.Points.Add(new Point(px, py));
        }
    }

    private void RefreshFeelDetails()
    {
        var feel = _session.Current.Feel;
        var normalStyle = (Style)Resources["FeelCardButtonStyle"];
        var selectedStyle = (Style)Resources["SelectedFeelCardButtonStyle"];
        CardAiry.Style = feel is not ("Snappy" or "Heavy" or "Bouncy") ? selectedStyle : normalStyle;
        CardSnappy.Style = feel == "Snappy" ? selectedStyle : normalStyle;
        CardHeavy.Style = feel == "Heavy" ? selectedStyle : normalStyle;
        CardBouncy.Style = feel == "Bouncy" ? selectedStyle : normalStyle;

        var preset = _session.Current.ToFeelPreset();
        MassValueText.Text = $"{preset.Mass:0.##}";
        FrequencyValueText.Text = $"{preset.Frequency:0.##} Hz";
        DampingRatioValueText.Text = $"{preset.DampingRatio:0.##}";
        DerivedValueText.Text = $"k {preset.Stiffness:F0} · c {preset.Damping:F1}";
    }

    private void FeelCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string feel })
        {
            FeelBox.SelectedIndex = feel switch { "Snappy" => 1, "Heavy" => 2, "Bouncy" => 3, _ => 0 };
        }
    }

    private void Feel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && FeelBox.SelectedItem is ComboBoxItem { Tag: string feel })
        {
            _session.Update(_session.Current with { Feel = feel });
            RefreshFeelDetails();
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
