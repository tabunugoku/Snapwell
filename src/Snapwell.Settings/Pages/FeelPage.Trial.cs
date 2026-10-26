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
    private VelocityTracker _velocity = new();
    private ManipulationModes _savedManipulation = ManipulationModes.System;
    private uint _dragPointerId;
    private readonly List<Rectangle> _zoneShapes = [];
    private readonly Stopwatch _clock = new();
    private FeelTrial? _trial;
    private SnapMode _trialMode = SnapMode.Zones;
    private bool _dragging;
    private bool _rendering;
    private bool _trialOpen;
    private double _lastFrame;

    private void TrialStage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        StopTrial();
        InitializeTrial();
    }

    private void Trial_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        _trialOpen = true;
        // 開く前の寸法は使わず、展開後のレイアウトで初期配置する。
        DispatcherQueue.TryEnqueue(InitializeTrial);
    }

    private void Trial_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        _trialOpen = false;
        StopTrial();
    }

    private void Trial_Unloaded(object sender, RoutedEventArgs e)
    {
        _trialOpen = false;
        StopTrial();
        TrialExpander.IsExpanded = false;
    }

    private void InitializeTrial()
    {
        var width = TrialStage.ActualWidth;
        var height = TrialStage.ActualHeight;
        if (!_trialOpen || _trial is not null || width <= 0 || height <= 0)
        {
            return;
        }

        TrialStage.Clip = new RectangleGeometry { Rect = new(0, 0, width, height) };
        _trial = new FeelTrial(width, height, _trialMode);
        _clock.Restart();
        BuildTrialShapes();
        PaintTrial();
    }

    private void StopTrial()
    {
        StopRendering();
        if (_dragging)
        {
            _dragging = false;
            TrialStage.ManipulationMode = _savedManipulation;
            TrialStage.ReleasePointerCaptures();
        }

        _trial = null;
        _clock.Reset();
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
        if (_dragging)
        {
            return;
        }

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
        if (_trial is not null && !_dragging)
        {
            Launch(_trial.RandomThrowVelocity(_session.Current.ToFeelPreset(), _session.Current.SnapRadius,
                Random.Shared));
        }
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
        if (direction != default && _trial is not null && !_dragging)
        {
            e.Handled = true;
            Launch(_trial.KeyThrowVelocity(direction, _session.Current.ToFeelPreset(), _session.Current.SnapRadius));
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

        // ドラッグ中の二本目の押下は無視する（元の ManipulationMode は最初の開始時だけ保存する）
        if (_dragging)
        {
            return;
        }

        StopRendering();
        _trial.Grab(point, kind, e.Pointer.PointerId);
        _dragging = true;
        _dragPointerId = e.Pointer.PointerId;
        // 親の ScrollViewer に入力を奪われないよう、ドラッグの間だけシステムの manipulation を止める。
        // 終了時（離す・キャンセル）に元へ戻す。実機（タッチ・ペン）未確認
        _savedManipulation = TrialStage.ManipulationMode;
        TrialStage.ManipulationMode = ManipulationModes.None;
        FindScrollViewer()?.CancelDirectManipulations();
        TrialStage.CapturePointer(e.Pointer);
        _velocity = new();
        _velocity.AddSample(point, _clock.Elapsed.TotalSeconds);
        PaintTrial();
    }

    private void TrialStage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _trial is null || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        var point = Position(e);
        _trial.Move(point, _dragPointerId);
        _velocity.AddSample(point, _clock.Elapsed.TotalSeconds);
        PaintTrial();
    }

    private void TrialStage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _trial is null || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        _dragging = false;
        TrialStage.ManipulationMode = _savedManipulation;
        TrialStage.ReleasePointerCapture(e.Pointer);
        // 本体と同じ速度の推定。止めてから離せば、最後の動きの速度は使わない
        var velocity = _velocity.Release(_clock.Elapsed.TotalSeconds);
        // 離した位置も渡し、本体と同じく上下端の帯で四分割を選ばせる
        if (_trial.Release(velocity, _session.Current.ToFeelPreset(), _session.Current.SnapRadius, _dragPointerId,
            Position(e)))
        {
            StartFlight();
        }
        else
        {
            PaintTrial();
        }
    }

    // キャンセルとキャプチャの喪失では、投げずにつかみだけを解く（カードはその場に残る）
    private void TrialStage_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _trial is null || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        _dragging = false;
        TrialStage.ManipulationMode = _savedManipulation;
        _trial.Cancel(_dragPointerId);
        PaintTrial();
    }

    private ScrollViewer? FindScrollViewer()
    {
        for (var node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(TrialStage); node is not null;
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    private void Launch(Vec2 velocity)
    {
        // ドラッグ中は、キーやボタンからの投げを受け付けない
        if (_trial is null || _dragging)
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
        // アプリの設定か OS のアニメーション設定のどちらかが削減を求めれば、止まる先へ直ちに確定する
        if (_session.Current.ReduceMotion || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            _trial!.Settle();
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
