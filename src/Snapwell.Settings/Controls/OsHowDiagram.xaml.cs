using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Snapwell.Settings.Controls;

/// <summary>標準機能の比較図の種類。</summary>
public enum OsHowDiagramKind
{
    DragSnap,
    SnapLayout,
    SnapBar,
    WinArrow,
}

/// <summary>標準機能と Snapwell の違いを、折りたためる比較カードで表示する。</summary>
public sealed partial class OsHowDiagram : UserControl
{
    /// <summary>比較図の種類を指定する依存関係プロパティ。</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(OsHowDiagramKind), typeof(OsHowDiagram),
        new PropertyMetadata(OsHowDiagramKind.DragSnap, (sender, _) => ((OsHowDiagram)sender).Populate()));

    /// <summary>表示する標準機能の種類。</summary>
    public OsHowDiagramKind Kind
    {
        get => (OsHowDiagramKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>開閉状態を保存せず、閉じた比較図を作る。</summary>
    public OsHowDiagram()
    {
        InitializeComponent();
        Populate();
    }

    private void Populate()
    {
        var (before, after, reason) = GetCopy();
        BeforeText.Text = before;
        AfterText.Text = after;
        BeforeHeading.Text = Kind == OsHowDiagramKind.SnapBar ? "Windows の標準（22H2 以降）" : "Windows の標準";
        Reason.Inlines.Clear();
        Reason.Inlines.Add(new Run
        {
            Text = Kind == OsHowDiagramKind.WinArrow ? "仕組み: " : "止める理由: ",
            FontWeight = FontWeights.SemiBold,
        });
        Reason.Inlines.Add(new Run { Text = reason });
        BeforeDrawing.Content = CreateDrawing(false);
        AfterDrawing.Content = CreateDrawing(true);
        AutomationProperties.SetName(Disclosure, Kind switch
        {
            OsHowDiagramKind.DragSnap => "ドラッグでのスナップ: Windows の標準との違いを図で見る",
            OsHowDiagramKind.SnapLayout => "スナップ レイアウト: Windows の標準との違いを図で見る",
            OsHowDiagramKind.SnapBar => "画面上端のスナップ バー: Windows の標準との違いを図で見る",
            _ => "Win + 矢印: Windows の標準との違いを図で見る",
        });
    }

    private (string Before, string After, string Reason) GetCopy() => Kind switch
    {
        OsHowDiagramKind.DragSnap => (
            "端や四隅へ持っていくと、灰色の枠が出ます。離すと、半分か 4 分の 1 の決まった大きさに広がり、続けて残りの場所に並べるウィンドウの候補が出ます（これも同じ設定で止まります）。",
            "標準の枠は出しません。投げて離すと、吸着先の区画を青く表示し、ばねと磁石の物理モデルでその区画に収まります" +
            "（青い表示は「区画のプレビュー」がオンのとき）。大きさは決まった形に限りません。",
            "標準のスナップと同時に動くと、離したあとにウィンドウの位置と大きさが取り合いになるため。" +
            "Windows の設定アプリの「ウィンドウのスナップ」と同じスイッチを切ります。"),
        OsHowDiagramKind.SnapLayout => (
            "ウィンドウの最大化ボタン（□）にポインターを載せると、右下にメニューが開きます。" +
            "候補（半分ずつ・2 対 1・左と右の上下分け・4 分割など）から選ぶと、ウィンドウがその形の区画に収まります。" +
            "先頭の候補には、開いている別のアプリのアイコンが出ます（そのアプリと並べる提案）。",
            "最大化ボタンにポインターを載せても、メニューは出ません（ボタンを押すと、通常どおり最大化します）。区画への配置は、ランチャー（Ctrl+Alt+Space）・ホットキー・ドラッグで行います。",
            "標準のメニューの形は Snapwell の分割案と別物で、同じウィンドウに 2 通りの配置先が出てしまうため。上の「ドラッグでのスナップ」を止めると一緒に消えることを、実機で確認しました。" +
            "このため、この項目に個別のスイッチはありません（レジストリで個別に止めても、Explorer を再起動するまで反映されないため）。"),
        OsHowDiagramKind.SnapBar => (
            "ウィンドウを持って画面の上端へ近づけると、上端の中央に配置の候補が横一列で出ます。候補の上で離すと、その形の区画に収まります。先頭の候補は、別のアプリと並べる提案です。",
            "上端に候補のバーは出ません。上端へ近づけても何も起きず、区画への吸着は Snapwell のドラッグ（離した先のプレビュー）で行います。",
            "Windows と Snapwell の配置が干渉するのを防ぐため。" +
            "最大化ボタンのメニューと同じく、上の「ドラッグでのスナップ」を止めると一緒に消えることを、実機で確認しました。個別のスイッチはありません。" +
            "Windows 11 の 22H2 より前にはない機能です。"),
        OsHowDiagramKind.WinArrow => (
            "ウィンドウを画面の左半分・右半分に寄せる、最大化する、元の大きさに戻す・最小化する。",
            "キーはそのまま、Snapwell の「左／右／上／下の半分」（既定では Ctrl+Alt+矢印と同じ操作）が動きます。半分の大きさは、分割と間隔の設定に従います。",
            "Windows が予約しているキーのため、低レベルのキーボードフックで受け取り、標準の動作を呼ばずに Snapwell の操作へ渡します。オフにすると、標準の Win + 矢印に戻ります。"),
        _ => throw new InvalidOperationException(),
    };

    private void Disclosure_Click(object sender, RoutedEventArgs e)
    {
        var open = Comparison.Visibility == Visibility.Collapsed;
        Comparison.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DisclosureGlyph.Text = open ? "▾" : "▸";
    }

    private void Comparison_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // カード内の図は縮め、説明が読みにくくなる幅ではモックアップと同様に縦へ並べる。
        var narrow = e.NewSize.Width < 580;
        Cards.RowSpacing = narrow ? 12 : 0;
        Grid.SetColumnSpan(BeforeCard, narrow ? 3 : 1);
        Grid.SetRow(Arrow, narrow ? 1 : 0);
        Grid.SetColumn(Arrow, narrow ? 0 : 1);
        Grid.SetColumnSpan(Arrow, narrow ? 3 : 1);
        Grid.SetRow(AfterCard, narrow ? 2 : 0);
        Grid.SetColumn(AfterCard, narrow ? 0 : 2);
        Grid.SetColumnSpan(AfterCard, narrow ? 3 : 1);
        Arrow.Text = narrow ? "↓" : "→";
    }

    private FrameworkElement CreateDrawing(bool after) => Kind switch
    {
        OsHowDiagramKind.DragSnap => CreateFrames(after),
        OsHowDiagramKind.SnapLayout => Screen(104, false, after
            ? [At(Window(), 4, 6, 92, 92, verticalPixels: true)]
            : [At(Window(), 4, 6, 92, 92, verticalPixels: true),
               At(Flyout(false), 58, 16, 39, 78, verticalPixels: true)]),
        OsHowDiagramKind.SnapBar => Screen(104, false, after
            ? [At(Window(), 38, 56, 24, 40, verticalPixels: true)]
            : [At(Flyout(true), 14, 5, 72, 30, verticalPixels: true),
               At(Window(), 38, 56, 24, 40, verticalPixels: true)]),
        _ => Keys(),
    };

    private Grid CreateFrames(bool after)
    {
        var frames = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        frames.ColumnDefinitions.Add(new ColumnDefinition());
        frames.ColumnDefinitions.Add(new ColumnDefinition());
        var holding = after
            ? Screen(76, true, At(Box("DiagramCell"), 12.5, 25, 37.5, 50), At(Window(), 56, 12, 30, 40))
            : Screen(76, false, At(Box("DiagramGhost"), 0, 0, 50, 100), At(Window(), 8, 30, 28, 40));
        var released = after
            ? Screen(76, true, At(Window(), 12.5, 25, 37.5, 50))
            : Screen(76, false, At(Window(), 0, 0, 50, 100));
        AddFrame(holding, "① 持っている間", 0);
        AddFrame(released, "② 離したあと", 1);
        return frames;

        void AddFrame(Border drawing, string label, int column)
        {
            drawing.Margin = new Thickness(0, 0, 0, 3);
            var frame = new StackPanel();
            frame.Children.Add(drawing);
            frame.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)Resources["DiagramText"],
                FontSize = 11.5,
                TextAlignment = TextAlignment.Center,
            });
            Grid.SetColumn(frame, column);
            frames.Children.Add(frame);
        }
    }

    private Border Screen(double height, bool grid, params Placement[] shapes)
    {
        var canvas = new Canvas { Height = height - 2, IsHitTestVisible = false };
        var placements = new List<Placement>();
        if (grid)
        {
            for (var column = 1; column < 8; column++)
            {
                placements.Add(At(Box("DiagramGridLine"), column * 12.5, 0, 1, 100, widthPixels: true));
            }
            for (var row = 1; row < 4; row++)
            {
                placements.Add(At(Box("DiagramGridLine"), 0, row * 25, 100, 0));
            }
        }
        placements.AddRange(shapes);
        foreach (var placement in placements)
        {
            canvas.Children.Add(placement.Element);
        }
        canvas.SizeChanged += (_, e) =>
        {
            // CSS の % は画面内側の幅・高さ、px は固定値として扱う。
            canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            foreach (var placement in placements)
            {
                Canvas.SetLeft(placement.Element, e.NewSize.Width * placement.Left / 100);
                Canvas.SetTop(placement.Element, placement.VerticalPixels
                    ? placement.Top : e.NewSize.Height * placement.Top / 100);
                placement.Element.Width = placement.WidthPixels
                    ? placement.Width : e.NewSize.Width * placement.Width / 100;
                placement.Element.Height = placement.VerticalPixels
                    ? placement.Height : placement.Height == 0 ? 1 : e.NewSize.Height * placement.Height / 100;
            }
        };
        var screen = Box("DiagramScreen");
        screen.Child = canvas;
        screen.Margin = new Thickness(0, 0, 0, 8);
        return screen;
    }

    private Border Window()
    {
        var window = Box("DiagramWindow");
        var titleBar = Box("DiagramTitleBar");
        titleBar.Child = Box("DiagramCaptionButton");
        window.Child = titleBar;
        return window;
    }

    private Border Flyout(bool row)
    {
        var flyout = Box("DiagramFlyout");
        var options = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
        for (var column = 0; column < (row ? 5 : 2); column++)
        {
            options.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (var index = 0; index < (row ? 1 : 3); index++)
        {
            options.RowDefinitions.Add(new RowDefinition());
        }
        for (var index = 0; index < 5; index++)
        {
            var layout = new Grid { ColumnSpacing = 1 };
            layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(index == 2 ? 2 : 1, GridUnitType.Star),
            });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            for (var column = 0; column < 2; column++)
            {
                FrameworkElement part;
                if (index == 4 || (index == 3 && column == 1))
                {
                    var stack = new Grid { RowSpacing = 1 };
                    stack.RowDefinitions.Add(new RowDefinition());
                    stack.RowDefinitions.Add(new RowDefinition());
                    stack.Children.Add(Box("DiagramLayoutCell"));
                    var bottom = Box("DiagramLayoutCell");
                    Grid.SetRow(bottom, 1);
                    stack.Children.Add(bottom);
                    part = stack;
                }
                else
                {
                    var selected = index == 0 && column == 1;
                    var cell = Box(selected ? "DiagramSelectedCell" : "DiagramLayoutCell");
                    if (selected)
                    {
                        cell.Child = Box("DiagramAppIcon");
                    }
                    part = cell;
                }
                Grid.SetColumn(part, column);
                layout.Children.Add(part);
            }
            Grid.SetColumn(layout, row ? index : index % 2);
            Grid.SetRow(layout, row ? 0 : index / 2);
            options.Children.Add(layout);
        }
        flyout.Child = options;
        return flyout;
    }

    private Grid Keys()
    {
        var keys = new Grid { ColumnSpacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        string[] labels = ["Win", "+", "←", "→", "↑", "↓"];
        for (var index = 0; index < labels.Length; index++)
        {
            keys.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new TextBlock
            {
                Text = labels[index],
                Style = (Style)Application.Current.Resources["SnapwellKbdTextStyle"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            FrameworkElement token = text;
            if (index != 1)
            {
                token = new Border
                {
                    Style = (Style)Application.Current.Resources["SnapwellKbdBorderStyle"],
                    Padding = new Thickness(2, 1, 2, 1),
                    Child = text,
                };
            }
            Grid.SetColumn(token, index);
            keys.Children.Add(token);
        }
        return keys;
    }

    private Border Box(string style) => new() { Style = (Style)Resources[style] };

    private static Placement At(FrameworkElement element, double left, double top, double width, double height,
        bool verticalPixels = false, bool widthPixels = false) =>
        new(element, left, top, width, height, verticalPixels, widthPixels);

    private sealed record Placement(FrameworkElement Element, double Left, double Top, double Width, double Height,
        bool VerticalPixels, bool WidthPixels);
}
