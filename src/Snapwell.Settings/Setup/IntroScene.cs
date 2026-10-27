using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snapwell.Core.Setup;
using Windows.UI;

namespace Snapwell.Settings.Setup;

/// <summary>点の場を Composition で描き、Core の台本を表示する。</summary>
internal sealed partial class IntroScene : Grid, IDisposable
{
    private readonly Func<bool> _visible;
    private readonly Action _finished;
    private readonly Action<double> _revealWelcome;
    private readonly Canvas _dotsHost = new();
    private readonly Canvas _windowsHost = new();
    private readonly Canvas _waveHost = new();
    private readonly Border _veil = new();
    private readonly Grid _logo = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        ColumnSpacing = 20
    };
    private readonly Canvas _mark = new() { Width = 72, Height = 72 };
    private readonly TextBlock _name = new()
    {
        Text = "Snapwell",
        FontSize = 64,
        FontFamily = new("Georgia"),
        FontWeight = Microsoft.UI.Text.FontWeights.Medium
    };
    private readonly TranslateTransform _nameTransform = new();
    private readonly CompositeTransform _squareTransform = new();
    private readonly Rectangle _square = new() { Width = 17.41, Height = 17.41, RadiusX = 3.73, RadiusY = 3.73 };
    private readonly List<Line> _arc = [];
    private readonly List<IntroWindowVisual> _windows = [];
    private readonly List<(ShapeVisual Visual, CompositionEllipseGeometry Geometry, CompositionColorBrush Brush)> _dots = [];
    private readonly IntroPalette _palette;
    private IntroChoreography? _model;
    private ContainerVisual? _field;
    private readonly IntroFrameClock _clock = new();
    private double? _skipElapsed;
    private bool _disposed;
    private bool _rendering;
    private XamlRoot? _root;

    internal IntroScene(Func<bool> visible, Action finished, Action<double> revealWelcome)
    {
        _visible = visible; _finished = finished; _revealWelcome = revealWelcome;
        _palette = new(this);
        Children.Add(_dotsHost); Children.Add(_waveHost); Children.Add(_windowsHost); Children.Add(_veil);
        _logo.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _logo.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _logo.Children.Add(_mark); _logo.Children.Add(_name); Grid.SetColumn(_name, 1);
        _name.RenderTransform = _nameTransform;
        BuildMark();
        Children.Add(_logo);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        AutomationProperties.SetAccessibilityView(_windowsHost, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_logo, AccessibilityView.Raw);
        Opacity = 1;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new IntroPeer(this);

    private void OnLoaded(object sender, RoutedEventArgs args) => Initialize();

    /// <summary>レイアウトが途中の大きさで届いたあとに確定した大きさへ変わったときは、台本を作り直す。</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Clip = new RectangleGeometry { Rect = new(0, 0, ActualWidth, ActualHeight) };
        if (_model is not null && (_model.Width != ActualWidth || _model.Height != ActualHeight)) { ReleaseScene(); }
        Initialize();
    }

    private sealed partial class IntroPeer(IntroScene owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(IntroScene);
        protected override IList<AutomationPeer> GetChildrenCore() => [];
    }

    private void BuildMark()
    {
        var background = new Rectangle
        {
            Width = 72,
            Height = 72,
            RadiusX = 16.36,
            RadiusY = 16.36,
            Fill = new SolidColorBrush(Color.FromArgb(255, 27, 27, 26))
        };
        _mark.Children.Add(background);
        var frame = new Rectangle
        {
            Width = 52.23,
            Height = 52.23,
            RadiusX = 11.19,
            RadiusY = 11.19,
            StrokeThickness = 4.09,
            Stroke = new SolidColorBrush(Color.FromArgb(255, 242, 242, 240))
        };
        Canvas.SetLeft(frame, 9.88); Canvas.SetTop(frame, 9.88); _mark.Children.Add(frame);
        // アプリアイコンの stroke-dasharray="3 3" と同じ破線にする。曲線を弧長で測り、3 進んで 3 空ける。
        // 根元から順に現れるよう、1 本の破線をさらに小さな線分へ分ける。
        static Vector2 Curve(double t)
        {
            var u = 1 - t;
            return new((float)(8 * u * u * u + 3 * 14 * u * u * t + 3 * 18 * u * t * t + 26 * t * t * t),
                (float)(34 * u * u * u + 3 * 34 * u * u * t + 3 * 26 * u * t * t + 18 * t * t * t));
        }
        const int samples = 256;
        var lengths = new double[samples + 1];
        for (var i = 1; i <= samples; i++)
        {
            lengths[i] = lengths[i - 1] + Vector2.Distance(Curve((i - 1d) / samples), Curve((double)i / samples));
        }
        Vector2 At(double length)
        {
            var i = Array.BinarySearch(lengths, length);
            if (i < 0) { i = ~i; }
            if (i <= 0) { return Curve(0); }
            var t = (length - lengths[i - 1]) / (lengths[i] - lengths[i - 1]);
            return Curve((i - 1 + t) / samples);
        }
        var scale = (float)(72d / 44 * .76);
        for (var start = 0d; start < lengths[^1]; start += 6)
        {
            var end = Math.Min(start + 3, lengths[^1]);
            for (var i = 0; i < 3; i++)
            {
                var a = At(start + (end - start) * i / 3) * scale + new Vector2(8.64f);
                var b = At(start + (end - start) * (i + 1) / 3) * scale + new Vector2(8.64f);
                var line = new Line
                {
                    X1 = a.X,
                    Y1 = a.Y,
                    X2 = b.X,
                    Y2 = b.Y,
                    StrokeThickness = 2.49,
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 155, 154, 151))
                };
                _arc.Add(line); _mark.Children.Add(line);
            }
        }
        _square.Fill = new SolidColorBrush(Color.FromArgb(255, 140, 203, 242));
        Canvas.SetLeft(_square, 37.24); Canvas.SetTop(_square, 17.35);
        _square.RenderTransform = _squareTransform;
        _squareTransform.CenterX = 8.7; _squareTransform.CenterY = 8.7;
        _mark.Children.Add(_square);
    }

    /// <summary>台本は大きさが決まってから作る。Loaded の時点では 0 のことがあり、そのまま作ると点も窓も左上の 1 点に潰れる。</summary>
    private void Initialize()
    {
        if (_disposed || _model is not null || XamlRoot is null || ActualWidth <= 0 || ActualHeight <= 0) { return; }
        _model = new(ActualWidth, ActualHeight);
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _field = compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_dotsHost, _field);
        foreach (var dot in _model.Dots)
        {
            var geometry = compositor.CreateEllipseGeometry();
            geometry.Radius = new(1.3f);
            geometry.Center = new(4);
            var brush = compositor.CreateColorBrush(_palette.Text.Color);
            var shape = compositor.CreateSpriteShape(geometry);
            shape.FillBrush = brush;
            var visual = compositor.CreateShapeVisual();
            visual.Size = new(8); visual.Shapes.Add(shape);
            _field.Children.InsertAtTop(visual);
            _dots.Add((visual, geometry, brush));
        }
        foreach (var window in _model.Windows)
        {
            var visual = new IntroWindowVisual();
            _windows.Add(visual); _windowsHost.Children.Add(visual);
        }
        _root = XamlRoot;
        _root.Changed += OnRootChanged;
        RefreshActivity();
        Paint();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => RefreshActivity();

    /// <summary>表示状態と終了状態から描画購読を切り替える。</summary>
    internal void RefreshActivity()
    {
        if (_disposed || _model is null) { return; }
        SetRendering(SetupPresentation.ShouldSubscribeIntroRendering(
            _visible() && _root is { IsHostVisible: true }, _model.IsComplete || _skipElapsed >= .8, _disposed));
    }

    private void SetRendering(bool rendering)
    {
        if (_rendering == rendering) { return; }
        _rendering = rendering;
        if (rendering)
        {
            _clock.Reset();
            CompositionTarget.Rendering += OnFrame;
        }
        else { CompositionTarget.Rendering -= OnFrame; }
    }

    /// <summary>何度呼ばれても同じ 0.8 秒のフェードを一度だけ始める。</summary>
    internal void Skip()
    {
        if (_disposed) { return; }
        // 台本ができる前は描画購読がなく OnFrame で終われないため、そのまま終える。
        if (_model is null) { _finished(); return; }
        if (_model is not { Elapsed: >= 9.6 }) { _skipElapsed ??= 0; }
    }

    private void OnFrame(object? sender, object args)
    {
        RefreshActivity();
        if (!_rendering || _model is null) { return; }
        var dt = _clock.Advance((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _model.Advance(dt);
        if (_skipElapsed is { } skip) { _skipElapsed = skip + dt; }
        Paint();
        if (_model.IsComplete || _skipElapsed >= .8)
        {
            RefreshActivity();
            _finished();
        }
    }

    private void Paint()
    {
        if (_model is null) { return; }
        var model = _model;
        var t = model.Elapsed;
        Background = _palette.Background;
        _veil.Background = _palette.Background;
        _veil.Opacity = .97 * Reveal((t - 7.1) / .7);
        _name.Foreground = _palette.Ink;
        var accent = _palette.Accent;
        var textColor = _palette.Text.Color;
        _logo.Opacity = model.LogoVisible ? 1 : 0;
        _mark.Opacity = Math.Clamp((t - 7.4) / .6, 0, 1);
        _name.Opacity = Math.Clamp((t - 7.75) / .8, 0, 1);
        _nameTransform.Y = 20 * (1 - model.LogoName);
        _square.Opacity = Math.Clamp((t - 7.85) / .3, 0, 1);
        _squareTransform.TranslateX = -16 * (1 - model.LogoSquare);
        _squareTransform.TranslateY = 20 * (1 - model.LogoSquare);
        _squareTransform.ScaleX = _squareTransform.ScaleY = .5 + .5 * model.LogoSquare;
        for (var i = 0; i < _arc.Count; i++) { _arc[i].Opacity = Reveal((t - 7.5) / .7) * _arc.Count > i ? 1 : 0; }
        for (var i = 0; i < _dots.Count; i++)
        {
            var p = model.Dots[i];
            var dot = _dots[i];
            var reveal = Reveal((t * 700 - double.Hypot(p.X - model.Width / 2, p.Y - model.Height / 2)) / 160);
            var heat = Math.Min(1, double.Hypot(p.DisplacementX, p.DisplacementY) / 10);
            var radius = (1.3 + heat * 1.8) * (.5 + reveal * .5);
            dot.Geometry.Radius = new((float)radius);
            dot.Visual.Offset = new((float)(p.X + p.DisplacementX - 4), (float)(p.Y + p.DisplacementY - 4), 0);
            dot.Visual.Opacity = (float)((heat > .1 ? .25 + heat * .65 : .2) * reveal);
            dot.Brush.Color = heat > .1 ? accent : textColor;
        }
        _waveHost.Children.Clear();
        foreach (var wave in model.Waves)
        {
            var age = t - wave.StartedAt;
            var radius = wave.Speed * age;
            var ring = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = _palette.AccentBrush,
                StrokeThickness = 1.5,
                Opacity = .45 * Math.Exp(-age * 1.8)
            };
            Canvas.SetLeft(ring, wave.X - radius); Canvas.SetTop(ring, wave.Y - radius);
            _waveHost.Children.Add(ring);
        }
        if (t < 2.2 && model.Windows.Count > 0)
        {
            var trail = model.Windows[0].Trail;
            for (var i = 1; i < trail.Count; i++)
            {
                var q = (double)i / trail.Count;
                _waveHost.Children.Add(new Line
                {
                    X1 = trail[i - 1].X,
                    Y1 = trail[i - 1].Y,
                    X2 = trail[i].X,
                    Y2 = trail[i].Y,
                    Stroke = _palette.AccentBrush,
                    StrokeThickness = 1 + q * 5,
                    Opacity = q * .5
                });
            }
        }
        for (var i = 0; i < _windows.Count; i++)
        {
            var w = model.Windows[i];
            var visual = _windows[i];
            visual.Visibility = w.Visible ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetZIndex(visual, w.Settled ? 0 : 1);
            visual.Paint(w.X - w.Width / 2, w.Y - w.Height / 2, w.Width, w.Height, w.Rotation,
                Math.Min(1, double.Hypot(w.VelocityX, w.VelocityY) / 700), w.Flash, _palette);
        }
        var opacity = _skipElapsed is { } skip ? 1 - Math.Clamp(skip / .8, 0, 1) : model.Opacity;
        Opacity = opacity;
        if (opacity < 1) { _revealWelcome(1 - opacity); }
    }

    private static double Reveal(double value) => 1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);

    /// <summary>ウィンドウを閉じたとき、静的な Rendering イベントを必ず外す。</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        SetRendering(false);
        Loaded -= OnLoaded; Unloaded -= OnUnloaded; SizeChanged -= OnSizeChanged;
        ReleaseScene();
    }

    /// <summary>点と窓の描画資源を解放し、次の Initialize で作り直せる状態に戻す。</summary>
    private void ReleaseScene()
    {
        if (_root is not null) { _root.Changed -= OnRootChanged; _root = null; }
        foreach (var window in _windows) { window.Dispose(); }
        _windows.Clear(); _windowsHost.Children.Clear();
        foreach (var dot in _dots) { dot.Visual.Dispose(); dot.Geometry.Dispose(); dot.Brush.Dispose(); }
        _dots.Clear();
        // 要素ツリーに残したまま解放しない。
        ElementCompositionPreview.SetElementChildVisual(_dotsHost, null);
        _field?.Dispose();
        _field = null;
        _model = null;
    }
}
