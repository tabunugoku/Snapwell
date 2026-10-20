using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;

namespace Snapwell.Settings.Pages;

/// <summary>角の設定とオーバーレイの線を編集する。</summary>
public sealed partial class LookPage : Page
{
    private readonly SettingsSession _session;
    private bool _refreshing = true;

    /// <summary>編集中の設定を共有し、保存は SettingsSession に任せる。</summary>
    public LookPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _session.Changed += Session_Changed;
            Refresh();
        };
        Unloaded += (_, _) => _session.Changed -= Session_Changed;
        Refresh();
    }

    private void Session_Changed(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        IsEnabled = !_session.IsReadOnly;
        CornerBox.SelectedIndex = _session.Current.CornerPreference switch
        {
            WindowCornerPreference.Round => 0,
            WindowCornerPreference.RoundSmall => 1,
            WindowCornerPreference.DoNotRound => 2,
            _ => 3,
        };
        ZonePreviewToggle.IsOn = _session.Current.ShowZonePreview;
        DragPreviewBox.SelectedIndex = _session.Current.DragPreviewStyle == DragPreviewStyle.OnRelease ? 1 : 0;
        ZoneLinesToggle.IsOn = _session.Current.ShowZoneLines;
        PredictionLineToggle.IsOn = _session.Current.ShowPredictionLine;
        _refreshing = false;
        UpdateCornerCards();
        UpdateOverlayFigure();
    }

    private void Corner_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
        {
            var preference = CornerBox.SelectedIndex switch
            {
                0 => WindowCornerPreference.Round,
                1 => WindowCornerPreference.RoundSmall,
                2 => WindowCornerPreference.DoNotRound,
                _ => WindowCornerPreference.Default,
            };
            _session.Update(_session.Current with { CornerPreference = preference });
            UpdateCornerCards();
        }
    }

    private void ZonePreview_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with { ShowZonePreview = ZonePreviewToggle.IsOn });
            UpdateOverlayFigure();
        }
    }

    private void DragPreview_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with
            {
                DragPreviewStyle = DragPreviewBox.SelectedIndex == 1 ? DragPreviewStyle.OnRelease
                    : DragPreviewStyle.LiveUntilStop,
            });
        }
    }

    private void Lines_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with
            {
                ShowZoneLines = ZoneLinesToggle.IsOn,
                ShowPredictionLine = PredictionLineToggle.IsOn,
            });
            UpdateOverlayFigure();
        }
    }

    private void CornerCard_Click(object sender, RoutedEventArgs e)
    {
        // カードの並び（既定・強制・小さい・なし）をコンボボックスの並びに直す
        CornerBox.SelectedIndex = int.Parse((string)((FrameworkElement)sender).Tag) switch
        {
            0 => 3,
            1 => 0,
            2 => 1,
            _ => 2,
        };
    }

    private void UpdateCornerCards()
    {
        var index = CornerBox.SelectedIndex;
        CardRoundMark.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardSmallMark.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        CardNoneMark.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        CardDefaultMark.Visibility = index is < 0 or > 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOverlayFigure()
    {
        var preview = ZonePreviewToggle.IsOn;
        OverlayFill.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        OverlayBox.Visibility = preview && ZoneLinesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        var prediction = preview && PredictionLineToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        OverlayPredLine.Visibility = prediction;
        OverlayPredDot.Visibility = prediction;
        OverlayLegend.Text = preview ? string.Empty : "区画のプレビューがオフ: ドラッグ中は何も表示しません";
    }
}
