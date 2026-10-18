using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Settings.Pages;

/// <summary>操作感を試す領域。カードの動きは Core の FeelTrial（本体と同じ磁石場）が決める。</summary>
public sealed partial class FeelPage
{
    private const double SampleWindowSeconds = 0.1;
    private const double KeyThrowSpeed = 2200;

    private readonly List<(double Time, Vec2 Point)> _samples = [];
    private readonly List<Rectangle> _zoneShapes = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private FeelTrial? _trial;
    private SnapMode _trialMode = SnapMode.Zones;
    private bool _dragging;
    private bool _rendering;
    private double _lastFrame;

    private void TrialStage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TrialStage.Clip = new RectangleGeometry { Rect = new(0, 0, e.NewSize.Width, e.NewSize.Height) };
        _trial = new FeelTrial(e.NewSize.Width, e.NewSize.Height, _trialMode);
        StopRendering();
        BuildTrialShapes();
        PaintTrial();
    }

    private void BuildTrialShapes()
    {
        foreach (var shape in _zoneShapes)
        {
            TrialStage.Children.Remove(shape);
        }

        _zoneShapes.Clear();
        foreach (var child in TrialStage.Children.OfType<Ellipse>().ToList())
        {
            TrialStage.Children.Remove(child);
        }

        if (_trial is null)
        {
            return;
        }

        var line = ThemeBrush("SnapwellCellBorderBrush");
        foreach (var zone in _trial.Guides)
        {
            var rectangle = new Rectangle
            {
                Width = zone.W,
                Height = zone.H,
                RadiusX = 4,
                RadiusY = 4,
                Stroke = line,
                StrokeThickness = 1,
                StrokeDashArray = [3, 3],
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rectangle, zone.X);
            Canvas.SetTop(rectangle, zone.Y);
            TrialStage.Children.Insert(0, rectangle);
            _zoneShapes.Add(rectangle);
        }

        var muted = ThemeBrush("SnapwellMutedBrush");
        foreach (var stop in _trial.CornerPoints)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = muted, Opacity = 0.6, IsHitTestVisible = false };
            Canvas.SetLeft(dot, stop.X - 4);
            Canvas.SetTop(dot, stop.Y - 4);
            TrialStage.Children.Insert(_zoneShapes.Count, dot);
        }
    }

    private void PaintTrial()
    {
        if (_trial is null)
        {
            return;
        }

        var size = _trial.Size;
        TrialCard.Width = size.X;
        TrialCard.Height = size.Y;
        Canvas.SetLeft(TrialCard, _trial.Center.X - size.X / 2);
        Canvas.SetTop(TrialCard, _trial.Center.Y - size.Y / 2);
        // 浮き上がりは速さから決まる（feel の corner-card と同じ）。
        var scale = 1 + 0.05 * (_dragging ? 0.4 : _trial.Lift);
        TrialCardTransform.ScaleX = TrialCardTransform.ScaleY = scale;
        var highlight = ThemeBrush("SnapwellCellBrush");
        for (var i = 0; i < _zoneShapes.Count; i++)
        {
            _zoneShapes[i].Fill = _trial.GuideIndex == i ? highlight : null;
        }

    }

    private void TrialMode_Click(object sender, RoutedEventArgs e)
    {
        _trialMode = ((Button)sender).Tag is "Corners" ? SnapMode.Corners : SnapMode.Zones;
        TrialZonesButton.Style = (Style)Resources[_trialMode == SnapMode.Zones ? "SelectedChipButtonStyle" : "ChipButtonStyle"];
        TrialCornersButton.Style = (Style)Resources[_trialMode == SnapMode.Corners ? "SelectedChipButtonStyle" : "ChipButtonStyle"];
        if (_trial is not null)
        {
            StopRendering();
            _trial.SetMode(_trialMode);
            BuildTrialShapes();
            PaintTrial();
        }
    }

    private void TrialThrow_Click(object sender, RoutedEventArgs e)
    {
        var angle = Random.Shared.NextDouble() * Math.PI * 2;
        var speed = 1200 + Random.Shared.NextDouble() * 2400;
        Launch(new(Math.Cos(angle) * speed, Math.Sin(angle) * speed));
    }

    private void TrialCard_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var direction = e.Key switch
        {
            Windows.System.VirtualKey.Left => new Vec2(-1, 0),
            Windows.System.VirtualKey.Right => new Vec2(1, 0),
            Windows.System.VirtualKey.Up => new Vec2(0, -1),
            Windows.System.VirtualKey.Down => new Vec2(0, 1),
            _ => default,
        };
        if (direction != default)
        {
            e.Handled = true;
            Launch(direction * KeyThrowSpeed);
        }
    }

    private void TrialStage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = Position(e);
        var kind = e.Pointer.PointerDeviceType switch
        {
            Microsoft.UI.Input.PointerDeviceType.Touch => TrialPointerKind.Touch,
            Microsoft.UI.Input.PointerDeviceType.Pen => TrialPointerKind.Pen,
            _ => TrialPointerKind.Mouse,
        };
        // タイトルバー以外（本体や舞台の背景）では、動いている最中のカードも止めない。
        if (_trial is null || !_trial.HitsTitleBar(point, kind))
        {
            return;
        }

        StopRendering();
        _trial.Grab(point, kind);
        _dragging = true;
        TrialStage.CapturePointer(e.Pointer);
        _samples.Clear();
        _samples.Add((_clock.Elapsed.TotalSeconds, point));
        PaintTrial();
    }

    private void TrialStage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _trial is null)
        {
            return;
        }

        var point = Position(e);
        _trial.Move(point);
        var now = _clock.Elapsed.TotalSeconds;
        _samples.Add((now, point));
        _samples.RemoveAll(sample => now - sample.Time > SampleWindowSeconds);
        PaintTrial();
    }

    private void TrialStage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _trial is null)
        {
            return;
        }

        _dragging = false;
        TrialStage.ReleasePointerCapture(e.Pointer);
        var first = _samples[0];
        var last = _samples[^1];
        var seconds = Math.Max(0.016, last.Time - first.Time);
        var velocity = (last.Point - first.Point) * (1 / seconds);
        if (_trial.Release(velocity, _session.Current.ToFeelPreset(), _session.Current.SnapRadius))
        {
            StartFlight();
        }
        else
        {
            PaintTrial();
        }
    }

    private void Launch(Vec2 velocity)
    {
        if (_trial is null)
        {
            return;
        }

        StopRendering();
        if (_trial.Release(velocity, _session.Current.ToFeelPreset(), _session.Current.SnapRadius))
        {
            StartFlight();
        }
    }

    // 動きを減らす設定がオンなら、アニメーションせずに止まる先へ直ちに移る。
    private void StartFlight()
    {
        if (_session.Current.ReduceMotion)
        {
            for (var i = 0; i < 60 * 12 && !_trial!.Advance(1.0 / 60); i++)
            {
            }

            PaintTrial();
            return;
        }

        _lastFrame = _clock.Elapsed.TotalSeconds;
        if (!_rendering)
        {
            _rendering = true;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnTrialRendering;
        }
    }

    private void OnTrialRendering(object? sender, object e)
    {
        if (_trial is null)
        {
            StopRendering();
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        var settled = _trial.Advance(now - _lastFrame);
        _lastFrame = now;
        PaintTrial();
        if (settled)
        {
            StopRendering();
        }
    }

    private void StopRendering()
    {
        if (_rendering)
        {
            _rendering = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTrialRendering;
        }
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private Vec2 Position(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TrialStage).Position;
        return new(point.X, point.Y);
    }
}
