using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snapwell.Core.Setup;
using Windows.UI.ViewManagement;

namespace Snapwell.Settings.Setup;

/// <summary>手順の進行から独立した、ポインターでつかんで投げられる舞台。</summary>
internal sealed partial class WelcomeStage : Grid, IDisposable
{
    private readonly Func<bool> _visible;
    private readonly Action<bool, bool> _hint;
    private readonly UISettings _uiSettings = new();
    private readonly Canvas _canvas = new();
    private readonly Rectangle[] _zones = Enumerable.Range(0, 3).Select(_ => new Rectangle
    {
        RadiusX = 8,
        RadiusY = 8,
        StrokeThickness = 1.5,
        StrokeDashArray = [4, 3],
        IsHitTestVisible = false,
    }).ToArray();
    private readonly IntroWindowVisual _window = new(welcome: true);
    private readonly IntroPalette _palette;
    private readonly List<(double Time, double X, double Y)> _track = [];
    private readonly Stopwatch _clock = new();
    private WelcomeThrowModel? _model;
    private Pointer? _pointer;
    private double _grabX;
    private double _grabY;
    private double _last;
    private bool _rendering;
    private bool _disposed;

    internal WelcomeStage(Func<bool> visible, Action<bool, bool> hint)
    {
        _visible = visible; _hint = hint;
        _palette = new(this);
        Height = 188; MaxWidth = 520; HorizontalAlignment = HorizontalAlignment.Stretch;
        CornerRadius = new(10); BorderThickness = new(1);
        Children.Add(_canvas);
        foreach (var zone in _zones) { _canvas.Children.Add(zone); }
        _canvas.Children.Add(_window);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_canvas, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_window, AccessibilityView.Raw);
        _window.ManipulationMode = ManipulationModes.None;
        _window.PointerPressed += OnPressed;
        _window.PointerMoved += OnMoved;
        _window.PointerReleased += OnReleased;
        _window.PointerCanceled += OnReleased;
        _window.PointerCaptureLost += OnReleased;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        ActualThemeChanged += (_, _) => Paint();
    }

    // 子の骨組みも支援技術の読み上げ対象にしない。
    protected override AutomationPeer OnCreateAutomationPeer() => new StagePeer(this);

    private sealed partial class StagePeer(WelcomeStage owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
        protected override IList<AutomationPeer> GetChildrenCore() => [];
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _rendering) { return; }
        _model = new(ActualWidth, ActualHeight);
        _clock.Start(); _last = _clock.Elapsed.TotalSeconds;
        _rendering = true;
        CompositionTarget.Rendering += OnFrame;
        Paint();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Release();
        _model?.Resize(args.NewSize.Width, args.NewSize.Height);
        Clip = new RectangleGeometry { Rect = new(0, 0, args.NewSize.Width, args.NewSize.Height) };
        Paint();
    }

    private void OnFrame(object? sender, object args)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = now - _last; _last = now;
        if (_model is null || !_visible()) { return; }
        var wasSettled = _model.Settled;
        _model.Advance(dt, animationsEnabled: _uiSettings.AnimationsEnabled);
        if (!wasSettled || !_model.Settled) { Paint(); }
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_model is null || _model.Held) { return; }
        var point = args.GetCurrentPoint(_canvas);
        if (!point.Properties.IsLeftButtonPressed && args.Pointer.PointerDeviceType ==
            Microsoft.UI.Input.PointerDeviceType.Mouse) { return; }
        if (!_window.CapturePointer(args.Pointer)) { return; }
        _pointer = args.Pointer;
        _grabX = point.Position.X - _model.X; _grabY = point.Position.Y - _model.Y;
        _model.Grab();
        _track.Clear();
        Record();
        _hint(true, true);
        Paint(); args.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_model is not { Held: true } || args.Pointer.PointerId != _pointer?.PointerId) { return; }
        var point = args.GetCurrentPoint(_canvas);
        _model.Move(point.Position.X - _grabX, point.Position.Y - _grabY);
        Record(); Paint(); args.Handled = true;
    }

    private void Record()
    {
        if (_model is null) { return; }
        var now = _clock.Elapsed.TotalSeconds;
        _track.Add((now, _model.X, _model.Y));
        _track.RemoveAll(p => p.Time < now - .08);
    }

    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerId == _pointer?.PointerId) { Release(); args.Handled = true; }
    }

    private void Release()
    {
        if (_model is not { Held: true }) { return; }
        // 止めてから離したときも、古い移動の速度を投げとして使わない。
        Record();
        var first = _track[0]; var last = _track[^1];
        var dt = last.Time - first.Time;
        _model.Release(dt > 0 ? (last.X - first.X) / dt : 0,
            dt > 0 ? (last.Y - first.Y) / dt : 0, _uiSettings.AnimationsEnabled);
        var pointer = _pointer; _pointer = null;
        if (pointer is not null) { _window.ReleasePointerCapture(pointer); }
        _hint(false, true);
        Paint();
    }

    private void Paint()
    {
        if (_model is null || _disposed) { return; }
        Background = _palette.Background; BorderBrush = _palette.Border;
        for (var i = 0; i < _zones.Length; i++)
        {
            var z = _model.Cells[i]; var shape = _zones[i];
            shape.Width = z.Width; shape.Height = z.Height;
            Canvas.SetLeft(shape, z.X); Canvas.SetTop(shape, z.Y);
            var selected = i == _model.Target;
            shape.Stroke = selected ? _palette.AccentBrush : _palette.Border;
            shape.Fill = selected ? new SolidColorBrush(_palette.Accent) { Opacity = _palette.Dark ? .16 : .12 }
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        _window.Paint(_model.X, _model.Y, _model.Width, _model.Height, 0, _model.Held ? .7 : .2, 0, _palette);
    }

    /// <summary>非表示への遷移と閉じる操作で描画とキャプチャを止める。</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        Release();
        _disposed = true;
        CompositionTarget.Rendering -= OnFrame;
        _clock.Stop();
        _window.Dispose();
        Loaded -= OnLoaded; Unloaded -= OnUnloaded;
    }
}
