using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;
using Windows.Globalization.NumberFormatting;

namespace Snapwell.Settings.Pages;

/// <summary>分割案と間隔を編集する。</summary>
public sealed partial class GridPage : Page
{
    private readonly SettingsSession _session;
    private bool _refreshing = true;

    /// <summary>保存せずに編集中の設定へ反映する画面を作る。</summary>
    public GridPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        ColumnsBox.NumberFormatter = new DecimalFormatter { FractionDigits = 0 };
        RowsBox.NumberFormatter = new DecimalFormatter { FractionDigits = 0 };
        IsEnabled = !session.IsReadOnly;
        Refresh();
    }

    private void Refresh()
    {
        _refreshing = true;
        var value = _session.Current;
        ColumnsBox.Value = value.Grid.Columns;
        RowsBox.Value = value.Grid.Rows;
        GapBox.Value = value.Gap;
        EdgeBox.Value = value.Edge;
        PlanBox.SelectedIndex = value.Grid switch
        {
            { Columns: 8, Rows: 8 } => 0,
            { Columns: 12, Rows: 6 } => 1,
            { Columns: 6, Rows: 4 } => 2,
            _ => 3,
        };
        _refreshing = false;
    }

    private void Plan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || PlanBox.SelectedIndex is < 0 or > 2)
        {
            return;
        }

        var grid = PlanBox.SelectedIndex switch
        {
            1 => new GridSettings(12, 6),
            2 => new GridSettings(6, 4),
            _ => new GridSettings(),
        };
        _session.Update(_session.Current with { Grid = grid });
        Refresh();
    }

    private void Grid_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_refreshing)
        {
            return;
        }

        _session.Update(_session.Current with { Grid = new((int)ColumnsBox.Value, (int)RowsBox.Value) });
        Refresh();
    }

    private void Gap_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with { Gap = args.NewValue });
            Refresh();
        }
    }

    private void Edge_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with { Edge = args.NewValue });
            Refresh();
        }
    }
}
