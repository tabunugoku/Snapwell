using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Snapwell.Settings.Setup;

/// <summary>中身の上に枠を重ねる、文字のない Windows 11 風の窓。</summary>
internal sealed partial class IntroWindowVisual : Canvas, IDisposable
{
    private readonly Border _surface = new() { CornerRadius = new(9) };
    private readonly Canvas _content = new();
    private readonly Border _title = new() { CornerRadius = new(9, 9, 0, 0) };
    private readonly Border _icon = new() { Width = 10, Height = 10, CornerRadius = new(2.5) };
    private readonly Border _caption = new() { Height = 5, CornerRadius = new(2.5) };
    private readonly Border[] _bars;
    private readonly Line[] _controls;
    private readonly Rectangle _maximize = new() { Width = 7, Height = 7, StrokeThickness = 1 };
    private readonly Border _frame = new() { CornerRadius = new(9), BorderThickness = new(1.5) };
    private readonly RotateTransform _rotation = new();
    private readonly DropShadow _shadow;
    private readonly SpriteVisual _shadowVisual;
    private readonly CompositionRoundedRectangleGeometry _shadowGeometry;
    private readonly ShapeVisual _maskVisual;
    private readonly CompositionVisualSurface _maskSurface;
    private readonly CompositionSurfaceBrush _maskBrush;
    private readonly CompositionSpriteShape _maskShape;
    private readonly CompositionColorBrush _maskFill;
    private readonly bool _welcome;

    internal IntroWindowVisual(bool welcome = false)
    {
        _welcome = welcome;
        IsHitTestVisible = welcome;
        RenderTransform = _rotation;
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var shadowHost = new Canvas { IsHitTestVisible = false };
        Children.Add(shadowHost);
        _shadow = compositor.CreateDropShadow();
        _shadow.Color = Color.FromArgb(255, 0, 0, 0);
        _shadowVisual = compositor.CreateSpriteVisual();
        _shadowVisual.Shadow = _shadow;
        _shadowGeometry = compositor.CreateRoundedRectangleGeometry();
        _shadowGeometry.CornerRadius = new(9);
        _maskVisual = compositor.CreateShapeVisual();
        _maskShape = compositor.CreateSpriteShape(_shadowGeometry);
        _maskFill = compositor.CreateColorBrush(Microsoft.UI.Colors.White);
        _maskShape.FillBrush = _maskFill;
        _maskVisual.Shapes.Add(_maskShape);
        _maskSurface = compositor.CreateVisualSurface();
        _maskSurface.SourceVisual = _maskVisual;
        _maskBrush = compositor.CreateSurfaceBrush(_maskSurface);
        _shadowVisual.Brush = _maskBrush;
        _shadow.Mask = _maskBrush;
        ElementCompositionPreview.SetElementChildVisual(shadowHost, _shadowVisual);
        Children.Add(_surface);
        Children.Add(_content);
        _content.Children.Add(_title);
        _content.Children.Add(_icon);
        _content.Children.Add(_caption);
        _bars = Enumerable.Range(0, welcome ? 2 : 4).Select(_ => new Border
        {
            CornerRadius = new(welcome ? 3 : 3.5),
            Height = welcome ? 6 : 7,
        }).ToArray();
        foreach (var bar in _bars) { _content.Children.Add(bar); }
        _controls = Enumerable.Range(0, 3).Select(_ => new Line { StrokeThickness = 1 }).ToArray();
        foreach (var line in _controls) { _content.Children.Add(line); }
        _content.Children.Add(_maximize);
        Children.Add(_frame);
    }

    internal void Paint(double x, double y, double width, double height, double rotation,
        double lift, double flash, IntroPalette palette)
    {
        Width = Math.Max(0, width); Height = Math.Max(0, height);
        Canvas.SetLeft(this, x); Canvas.SetTop(this, y);
        _rotation.CenterX = width / 2; _rotation.CenterY = height / 2;
        _rotation.Angle = rotation * 180 / Math.PI;
        _surface.Width = Width; _surface.Height = Height; _surface.Background = palette.Surface;
        _frame.Width = Width; _frame.Height = Height;
        var accent = palette.Accent;
        _frame.BorderBrush = flash > .02
            ? new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(.35 + flash * .65, 0, 1)),
                accent.R, accent.G, accent.B)) : palette.Border;
        _content.Clip = new RectangleGeometry { Rect = new(0, 0, Width, Height) };
        var tb = _welcome ? 16 : 24;
        _title.Width = Width; _title.Height = Math.Min(tb, Height); _title.Background = palette.Surface2;
        _icon.Background = palette.AccentBrush;
        Canvas.SetLeft(_icon, 8); Canvas.SetTop(_icon, _welcome ? 3 : 7);
        _icon.Width = _welcome ? 7 : 10; _icon.Height = _welcome ? 7 : 10;
        _caption.Background = palette.Skeleton; _caption.Width = Math.Min(width * .28, 70);
        Canvas.SetLeft(_caption, _welcome ? 20 : 24); Canvas.SetTop(_caption, _welcome ? 5 : 9.5);
        var bars = _welcome ? new[] { 1d, .6 } : [.7, .48, .58, .4];
        for (var i = 0; i < _bars.Length; i++)
        {
            var by = _welcome ? 22 + i * 12 : 38 + i * 14;
            _bars[i].Visibility = by + _bars[i].Height <= height - 8 ? Visibility.Visible : Visibility.Collapsed;
            _bars[i].Width = Math.Max(0, (width - (_welcome ? 16 : 24)) * bars[i]);
            _bars[i].Background = palette.Skeleton;
            Canvas.SetLeft(_bars[i], _welcome ? 8 : 12); Canvas.SetTop(_bars[i], by);
        }
        var cx = width - 84;
        SetLine(_controls[0], cx + 10, tb / 2 + .5, cx + 18, tb / 2 + .5, palette);
        SetLine(_controls[1], width - 18, tb / 2 - 3.5, width - 10, tb / 2 + 4.5, palette);
        SetLine(_controls[2], width - 10, tb / 2 - 3.5, width - 18, tb / 2 + 4.5, palette);
        _maximize.Stroke = palette.Text;
        Canvas.SetLeft(_maximize, width - 45.5); Canvas.SetTop(_maximize, tb / 2 - 3.5);
        _shadowVisual.Size = new((float)Width, (float)Height);
        _shadowGeometry.Size = _shadowVisual.Size;
        _maskVisual.Size = _shadowVisual.Size;
        _maskSurface.SourceSize = _shadowVisual.Size;
        _shadow.Opacity = (float)(.12 + lift * .2);
        _shadow.BlurRadius = (float)(8 + lift * 26);
        _shadow.Offset = new Vector3(0, (float)(2 + lift * 12), 0);
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2, IntroPalette palette)
    {
        line.X1 = x1; line.Y1 = y1; line.X2 = x2; line.Y2 = y2;
        line.Stroke = palette.Text; line.Opacity = .55;
    }

    /// <summary>Composition の描画資源を解放する。</summary>
    public void Dispose()
    {
        _shadowVisual.Dispose(); _shadow.Dispose();
        _maskBrush.Dispose(); _maskSurface.Dispose(); _maskVisual.Dispose();
        _maskShape.Dispose(); _maskFill.Dispose(); _shadowGeometry.Dispose();
    }
}
