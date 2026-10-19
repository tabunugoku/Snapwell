using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snapwell.Core.Physics;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Windows.Foundation;
using Windows.Globalization.NumberFormatting;

namespace Snapwell.Settings.Pages;

/// <summary>物理モデルと吸着の方式を編集する。</summary>
public sealed partial class FeelPage : Page
{
    // 範囲は feel の rig のスライダー（質量・振動数・減衰比）と Snapwell の設定（磁石の強さ・吸着半径）。
    // 質量と振動数は対数、ほかは線形のスライダー。位置は 0〜1000。
    private readonly record struct Tuning(FeelParameter? Parameter, Slider Slider, NumberBox Box, double Minimum,
        double Maximum, bool Log, int Digits);

    private const double SliderMaximum = 1000;

    private readonly SettingsSession _session;
    private readonly Tuning[] _tunings;
    private bool _refreshing = true;

    /// <summary>現在の値を標準コントロールへ設定する。</summary>
    public FeelPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        _tunings =
        [
            new(FeelParameter.Mass, MassSlider, MassBox, FeelPreset.MinimumMass, FeelPreset.MaximumMass, true, 2),
            new(FeelParameter.Frequency, FrequencySlider, FrequencyBox, FeelPreset.MinimumFrequency,
                FeelPreset.MaximumFrequency, true, 2),
            new(FeelParameter.DampingRatio, DampingRatioSlider, DampingRatioBox, FeelPreset.MinimumDampingRatio,
                FeelPreset.MaximumDampingRatio, false, 2),
            new(FeelParameter.MagnetStrength, MagnetStrengthSlider, MagnetStrengthBox, 0,
                FeelPreset.MaximumMagnetStrength, false, 2),
            new(null, SnapRadiusSlider, SnapRadiusBox, PortableSettings.MinimumSnapRadius,
                PortableSettings.MaximumSnapRadius, false, 0),
        ];
        BuildCurve(AiryCurve, FeelPreset.Airy);
        BuildCurve(SnappyCurve, FeelPreset.Snappy);
        BuildCurve(HeavyCurve, FeelPreset.Heavy);
        BuildCurve(BouncyCurve, FeelPreset.Bouncy);

        var value = session.Current;
        IsEnabled = !session.IsReadOnly;
        ModeBox.SelectedIndex = value.SnapMode == SnapMode.Corners ? 1 : 0;
        MotionSwitch.IsOn = value.ReduceMotion;
        foreach (var tuning in _tunings)
        {
            var captured = tuning;
            tuning.Box.NumberFormatter = new DecimalFormatter { FractionDigits = tuning.Digits, IntegerDigits = 1 };
            tuning.Slider.ValueChanged += (_, e) => Edit(captured, FromPosition(captured, e.NewValue), true);
            tuning.Box.ValueChanged += (_, e) => Edit(captured, e.NewValue, false);
        }

        ShowValues();
        _refreshing = false;
    }

    private static void BuildCurve(Polyline polyline, FeelPreset preset)
    {
        const double width = 160;
        const double height = 54;
        const double totalSeconds = 1.4;
        const int steps = 70;

        // Points の既定値は実装によって null のことがあるため、毎回新しい集合を入れる。
        polyline.Points = new PointCollection();
        polyline.Points.Add(new Point(0, height - 4));
        for (var i = 1; i <= steps; i++)
        {
            var px = i / (double)steps * width;
            var py = height - 4 - preset.StepPosition(i / (double)steps * totalSeconds) * (height - 16);
            polyline.Points.Add(new Point(px, py));
        }
    }

    private void RefreshFeelDetails()
    {
        var current = _session.Current;
        var custom = current.IsCustomFeel();
        var feel = current.Feel;
        var normalStyle = (Style)Resources["FeelCardButtonStyle"];
        var selectedStyle = (Style)Resources["SelectedFeelCardButtonStyle"];
        // カスタムのときは、どのカードも選ばれていない。
        CardAiry.Style = !custom && feel is not ("Snappy" or "Heavy" or "Bouncy") ? selectedStyle : normalStyle;
        CardSnappy.Style = !custom && feel == "Snappy" ? selectedStyle : normalStyle;
        CardHeavy.Style = !custom && feel == "Heavy" ? selectedStyle : normalStyle;
        CardBouncy.Style = !custom && feel == "Bouncy" ? selectedStyle : normalStyle;
        FeelStateText.Text = custom ? $"カスタム（{feel} から変更）" : $"プリセット: {feel}";
        FeelResetButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

        var preset = current.ToFeelPreset();
        DerivedValueText.Text = $"k {preset.Stiffness:F0} · c {preset.Damping:F1} · m/c {preset.CoastTime:0.00} s";
    }

    // 編集中のスライダーを動かし直すと指に逆らうため、編集元のスライダー以外へ現在の値を戻す。
    private void ShowValues(Slider? editing = null)
    {
        var preset = _session.Current.ToFeelPreset();
        var previous = _refreshing;
        _refreshing = true;
        try
        {
            foreach (var tuning in _tunings)
            {
                var value = tuning.Parameter switch
                {
                    FeelParameter.Mass => preset.Mass,
                    FeelParameter.Frequency => preset.Frequency,
                    FeelParameter.DampingRatio => preset.DampingRatio,
                    FeelParameter.MagnetStrength => preset.MagnetStrength,
                    _ => _session.Current.SnapRadius,
                };
                tuning.Box.Value = Math.Round(value, tuning.Digits);
                if (tuning.Slider != editing)
                {
                    tuning.Slider.Value = ToPosition(tuning, value);
                }
            }
        }
        finally
        {
            _refreshing = previous;
        }

        RefreshFeelDetails();
    }

    private static double ToPosition(Tuning tuning, double value)
    {
        var clamped = Math.Clamp(value, tuning.Minimum, tuning.Maximum);
        var ratio = tuning.Log
            ? Math.Log(clamped / tuning.Minimum) / Math.Log(tuning.Maximum / tuning.Minimum)
            : (clamped - tuning.Minimum) / (tuning.Maximum - tuning.Minimum);
        return ratio * SliderMaximum;
    }

    private static double FromPosition(Tuning tuning, double position)
    {
        var ratio = position / SliderMaximum;
        return tuning.Log
            ? tuning.Minimum * Math.Pow(tuning.Maximum / tuning.Minimum, ratio)
            : tuning.Minimum + ratio * (tuning.Maximum - tuning.Minimum);
    }

    private void Edit(Tuning tuning, double value, bool fromSlider)
    {
        if (_refreshing)
        {
            return;
        }

        if (double.IsFinite(value))
        {
            _session.Update(tuning.Parameter is { } parameter
                ? _session.Current.WithFeelValue(parameter, value)
                : _session.Current with
                {
                    SnapRadius = Math.Clamp(Math.Round(value),
                    PortableSettings.MinimumSnapRadius, PortableSettings.MaximumSnapRadius)
                });
        }

        ShowValues(fromSlider ? tuning.Slider : null);
    }

    private void FeelCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string feel })
        {
            SelectPreset(feel);
        }
    }

    private void FeelReset_Click(object sender, RoutedEventArgs e) => SelectPreset(_session.Current.Feel);

    // 同じプリセットを選び直す（カスタムから戻す）ときも値を入れ直す。
    private void SelectPreset(string feel)
    {
        _session.Update(_session.Current.WithPreset(feel));
        ShowValues();
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
