using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Platform.Monitors;
using Windows.Globalization.NumberFormatting;

namespace Snapwell.Settings.Pages;

/// <summary>分割案と間隔を編集する。</summary>
public sealed partial class GridPage : Page
{
    private readonly SettingsSession _session;
    private bool _refreshing = true;
    private bool _linkedEdge;
    private readonly GridMonitorCatalog _monitors;
    private string? _monitorId;
    private IReadOnlyList<GridMonitorChoice> _choices = [];
    private GridSettings EditingGrid => MonitorGridResolver.GetForEditing(_session.Current, _monitorId);
    private GridSettings DisplayGrid => MonitorGridResolver.Get(_session.Current, _monitorId);
    private PortableSettings PreviewSettings => _session.Current with
    {
        Grid = new(DisplayGrid.Columns, DisplayGrid.Rows),
        Gap = DisplayGrid.Gap!.Value,
        Edge = DisplayGrid.Edge!.Value,
    };

    /// <summary>保存せずに編集中の設定へ反映する画面を作る。</summary>
    public GridPage(SettingsSession session, IMonitorSource? monitors = null)
    {
        _session = session;
        _monitors = new(monitors ?? new Win32MonitorSource());
        InitializeComponent();
        ColumnsBox.NumberFormatter = new DecimalFormatter { FractionDigits = 0 };
        RowsBox.NumberFormatter = new DecimalFormatter { FractionDigits = 0 };
        _linkedEdge = Math.Abs(session.Current.Gap - session.Current.Edge) < 0.001;
        IsEnabled = !session.IsReadOnly;
        RefreshMonitors();
        Loaded += (_, _) =>
        {
            _session.Changed += Session_Changed;
            Refresh();
        };
        Unloaded += (_, _) => _session.Changed -= Session_Changed;
        Refresh();
    }

    private void Session_Changed(object? sender, EventArgs e) => Refresh();

    private void RefreshMonitors()
    {
        _refreshing = true;
        try
        {
            _choices = _monitors.GetChoices();
            if (!_choices.Any(choice => choice.Id == _monitorId))
            {
                _monitorId = null;
            }

            MonitorBox.ItemsSource = _choices;
            MonitorBox.SelectedItem = _choices.First(choice => choice.Id == _monitorId);
            MonitorStatus.Text = "未設定のモニターは共通の案を使います";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _choices = [new(null, "全モニター共通", null)];
            _monitorId = null;
            MonitorBox.ItemsSource = _choices;
            MonitorBox.SelectedIndex = 0;
            MonitorStatus.Text = "モニター一覧を取得できません。一覧を開くと再試行します";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Monitor_DropDownOpened(object sender, object e)
    {
        RefreshMonitors();
        Refresh();
    }

    private void Monitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && MonitorBox.SelectedItem is GridMonitorChoice choice)
        {
            _monitorId = choice.Id;
            _linkedEdge = DisplayGrid.Gap == DisplayGrid.Edge;
            Refresh();
        }
    }

    private void UpdateGrid(GridSettings grid)
    {
        // 開いた後に外れたモニターの案を、共通設定へ誤って書き込まない。
        var selected = _monitorId;
        RefreshMonitors();
        if (selected == _monitorId)
        {
            _session.Update(MonitorGridResolver.Update(_session.Current, _monitorId, grid));
        }
    }

    private void SnapAfterResize_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_refreshing)
        {
            _session.Update(_session.Current with { SnapAfterResize = SnapAfterResizeToggle.IsOn });
        }
    }

    private void Refresh()
    {
        _refreshing = true;
        var value = PreviewSettings;
        IsEnabled = !_session.IsReadOnly;
        SnapAfterResizeToggle.IsOn = value.SnapAfterResize;
        ColumnsBox.Value = value.Grid.Columns;
        RowsBox.Value = value.Grid.Rows;
        GapBox.Value = value.Gap;
        EdgeBox.Value = value.Edge;
        GapSlider.Value = Math.Clamp(value.Gap, GapSlider.Minimum, GapSlider.Maximum);
        EdgeSlider.Value = Math.Clamp(value.Edge, EdgeSlider.Minimum, EdgeSlider.Maximum);
        GapOutputText.Text = $"{value.Gap:0.#} px";
        EdgeOutputText.Text = $"{value.Edge:0.#} px";
        LinkEdgeCheck.IsChecked = _linkedEdge;
        EdgeSlider.IsEnabled = !_session.IsReadOnly && !_linkedEdge;
        PlanBox.SelectedIndex = value.Grid switch
        {
            { Columns: 8, Rows: 8 } => 0,
            { Columns: 12, Rows: 6 } => 1,
            { Columns: 6, Rows: 4 } => 2,
            _ => 3,
        };
        ResizeStepBox.SelectedIndex = value.ToResizeSettings().Step - ResizeSettings.MinStep;
        ResizeAnchorBox.SelectedIndex = (int)value.ToResizeSettings().Anchor;
        UpdateChips(value.Grid);
        RenderPreview(value);
        _refreshing = false;
    }

    private void UpdateChips(GridSettings grid)
    {
        var normalStyle = (Style)Resources["ChipButtonStyle"];
        var selectedStyle = (Style)Resources["SelectedChipButtonStyle"];
        var is8x8 = grid is { Columns: 8, Rows: 8 };
        var is12x6 = grid is { Columns: 12, Rows: 6 };
        var is6x4 = grid is { Columns: 6, Rows: 4 };
        var is4x4 = grid is { Columns: 4, Rows: 4 };
        var is16x9 = grid is { Columns: 16, Rows: 9 };
        var isPreset = is8x8 || is12x6 || is6x4 || is4x4 || is16x9;

        Chip8x8.Style = is8x8 ? selectedStyle : normalStyle;
        Chip12x6.Style = is12x6 ? selectedStyle : normalStyle;
        Chip6x4.Style = is6x4 ? selectedStyle : normalStyle;
        Chip4x4.Style = is4x4 ? selectedStyle : normalStyle;
        Chip16x9.Style = is16x9 ? selectedStyle : normalStyle;
        ChipCustom.Style = !isPreset ? selectedStyle : normalStyle;
        ChipCustom.Opacity = isPreset ? 0.55 : 1.0;
        ChipCustom.Content = !isPreset ? $"カスタム ({grid.Columns} × {grid.Rows})" : "カスタム…";
    }

    private void RenderPreview(PortableSettings value)
    {
        var cols = Math.Clamp(value.Grid.Columns, 1, 64);
        var rows = Math.Clamp(value.Grid.Rows, 1, 64);
        var area = _choices.FirstOrDefault(choice => choice.Id == _monitorId)?.Monitor?.WorkArea;
        var workWidth = area?.Width ?? 2560;
        var workHeight = area?.Height ?? 1400;
        var dpi = _choices.FirstOrDefault(choice => choice.Id == _monitorId)?.Monitor?.DpiX / 96.0 ?? 1;
        var physical = value.ToSpacing().ToPhysical(dpi);
        var realCw = Math.Max(0, (workWidth - 2 * physical.Edge - (cols - 1) * physical.Gap) / (double)cols);
        var realCh = Math.Max(0, (workHeight - 2 * physical.Edge - (rows - 1) * physical.Gap) / (double)rows);
        FormulaText.Text =
            $"作業領域 {workWidth} × {workHeight}\n区画 = (W − 2e − (N−1)g) / N\n" +
            $"  = {realCw:F1} × {realCh:F1} px\na 区画の幅 = a·区画 + (a−1)·g";

        var width = PreviewHost.ActualWidth - 2;
        if (width <= 32)
        {
            width = 520;
        }

        var height = Math.Round(width * workHeight / workWidth);
        PreviewCanvas.Width = width;
        PreviewCanvas.Height = height;
        PreviewCanvas.Children.Clear();

        // 選んだモニターの作業領域と物理ピクセルの間隔を縮める。
        var scale = width / workWidth;
        var gap = physical.Gap * scale;
        var edge = physical.Edge * scale;
        var cellWidth = Math.Max(1.0, (width - 2 * edge - (cols - 1) * gap) / cols);
        var cellHeight = Math.Max(1.0, (height - 2 * edge - (rows - 1) * gap) / rows);
        var cellStyle = (Style)Resources["PreviewCellBorderStyle"];

        // 区画数が極端に多いときでも UI を重くしないよう、背景セルの描画数を制限する。
        if (cols * rows <= 256)
        {
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var cell = new Border
                    {
                        Width = cellWidth,
                        Height = cellHeight,
                        Style = cellStyle,
                    };
                    Canvas.SetLeft(cell, edge + col * (cellWidth + gap));
                    Canvas.SetTop(cell, edge + row * (cellHeight + gap));
                    PreviewCanvas.Children.Add(cell);
                }
            }
        }

        var halfCols = Math.Max(1, cols / 2);
        var halfRows = Math.Max(1, rows / 2);
        AddPreviewWindow(0, 0, halfCols, halfRows, $"{halfCols} × {halfRows} 区画",
            edge, gap, cellWidth, cellHeight);
        if (cols > halfCols)
        {
            var rightCols = cols - halfCols;
            AddPreviewWindow(halfCols, 0, rightCols, rows, $"{rightCols} × {rows} 区画",
                edge, gap, cellWidth, cellHeight);
        }
    }

    private void AddPreviewWindow(int col, int row, int spanCols, int spanRows, string label,
        double edge, double gap, double cellWidth, double cellHeight)
    {
        var windowBorder = new Border
        {
            Width = Math.Max(4.0, spanCols * cellWidth + (spanCols - 1) * gap),
            Height = Math.Max(4.0, spanRows * cellHeight + (spanRows - 1) * gap),
            Style = (Style)Resources["PreviewWindowBorderStyle"],
            Child = new TextBlock
            {
                Text = label,
                Style = (Style)Resources["PreviewWindowLabelStyle"],
            },
        };
        Canvas.SetLeft(windowBorder, edge + col * (cellWidth + gap));
        Canvas.SetTop(windowBorder, edge + row * (cellHeight + gap));
        PreviewCanvas.Children.Add(windowBorder);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPreview(PreviewSettings);
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var cols) && int.TryParse(parts[1], out var rows))
        {
            UpdateGrid(EditingGrid with { Columns = cols, Rows = rows });
            Refresh();
        }
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
        UpdateGrid(EditingGrid with { Columns = grid.Columns, Rows = grid.Rows });
        Refresh();
    }

    private void Grid_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_refreshing)
        {
            return;
        }

        UpdateGrid(EditingGrid with { Columns = (int)ColumnsBox.Value, Rows = (int)RowsBox.Value });
        Refresh();
    }

    private void Gap_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_refreshing)
        {
            UpdateGrid(_linkedEdge
                ? EditingGrid with { Gap = args.NewValue, Edge = args.NewValue }
                : EditingGrid with { Gap = args.NewValue });
            Refresh();
        }
    }

    private void Edge_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_refreshing)
        {
            if (Math.Abs(args.NewValue - DisplayGrid.Gap!.Value) >= 0.001)
            {
                _linkedEdge = false;
            }

            UpdateGrid(EditingGrid with { Edge = args.NewValue });
            Refresh();
        }
    }

    private void GapSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_refreshing)
        {
            var gap = Math.Round(e.NewValue);
            UpdateGrid(_linkedEdge
                ? EditingGrid with { Gap = gap, Edge = gap }
                : EditingGrid with { Gap = gap });
            Refresh();
        }
    }

    private void EdgeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_refreshing)
        {
            UpdateGrid(EditingGrid with { Edge = Math.Round(e.NewValue) });
            Refresh();
        }
    }

    private void LinkEdge_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        _linkedEdge = LinkEdgeCheck.IsChecked == true;
        if (_linkedEdge)
        {
            UpdateGrid(EditingGrid with { Edge = DisplayGrid.Gap });
        }

        Refresh();
    }

    private void ResizeStep_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ResizeStepBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var step))
        {
            _session.Update(_session.Current with { ResizeStep = step });
            Refresh();
        }
    }

    private void ResizeAnchor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ResizeAnchorBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<ResizeAnchor>(tag, out var anchor))
        {
            _session.Update(_session.Current with { ResizeAnchor = anchor });
            Refresh();
        }
    }
}

/// <summary>設定セッションと分離した、接続モニターの表示用カタログ。</summary>
internal sealed class GridMonitorCatalog(IMonitorSource source)
{
    internal IReadOnlyList<GridMonitorChoice> GetChoices()
    {
        var result = new List<GridMonitorChoice> { new(null, "全モニター共通", null) };
        var monitors = source.GetMonitors().OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        var number = 0;
        foreach (var monitor in monitors)
        {
            number++;
            var label = $"{number} · {monitor.Bounds.Width} × {monitor.Bounds.Height} · {monitor.DpiX / 96.0 * 100:0}%" +
                (monitor.IsPrimary ? "（メイン）" : "");
            result.Add(new(monitor.DeviceName, label, monitor));
        }

        return result;
    }
}

internal sealed record GridMonitorChoice(string? Id, string Label, MonitorInfo? Monitor)
{
    public override string ToString() => Label;
}
