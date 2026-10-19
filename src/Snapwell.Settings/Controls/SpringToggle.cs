using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Physics;
using Windows.UI.ViewManagement;

namespace Snapwell.Settings.Controls;

/// <summary>ToggleSwitch のつまみを、固定の継続時間でなく feel の stiff, heavy のばねで動かす添付プロパティ。</summary>
public static class SpringToggle
{
    /// <summary>Enabled 添付プロパティ。SnapwellToggleSwitchStyle が true にする。</summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SpringToggle),
            new PropertyMetadata(false, OnEnabledChanged));

    internal static readonly DependencyProperty AttachmentProperty =
        DependencyProperty.RegisterAttached(
            "Attachment",
            typeof(object),
            typeof(SpringToggle),
            new PropertyMetadata(null));

    /// <summary>Enabled 添付プロパティの値を取得する。</summary>
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    /// <summary>Enabled 添付プロパティの値を設定する。</summary>
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is ToggleSwitch toggle && (bool)args.NewValue && toggle.GetValue(AttachmentProperty) is null)
        {
            toggle.SetValue(AttachmentProperty, new Attachment(toggle));
        }
    }

    /// <summary>一つのスイッチに結びつく状態。テンプレートの部品を探し、入力をばねの物体へ渡す。</summary>
    private sealed class Attachment
    {
        // 横ドラッグと見なす移動量。これ未満はタップ。
        private const double DragSlop = 4;

        private static readonly UISettings _systemSettings = new();

        private readonly ToggleSwitch _toggle;
        private SpringSwitch? _body;
        private FrameworkElement? _root;
        private FrameworkElement? _knob;
        private CompositeTransform? _transform;
        private UIElement? _trackFill;
        private UIElement? _trackStroke;
        private UIElement? _thumbOn;
        private uint _pointerId;
        private bool _pressed;
        private bool _dragging;
        private double _pressX;
        private double _pressPosition;

        public Attachment(ToggleSwitch toggle)
        {
            _toggle = toggle;
            toggle.Loaded += (_, _) => Attach();
            toggle.Unloaded += (_, _) => Detach();
            toggle.RegisterPropertyChangedCallback(ToggleSwitch.IsOnProperty, (_, _) => OnIsOnChanged());
            if (toggle.IsLoaded)
            {
                Attach();
            }
        }

        // 動きを減らす設定（Windows の「アニメーション効果」）がオフなら、ばねを使わず直ちに静止する。
        private static bool ReduceMotion => !_systemSettings.AnimationsEnabled;

        /// <summary>1 フレーム進める。静止したら true。</summary>
        public bool Step(double seconds)
        {
            if (_body is null)
            {
                return true;
            }

            var settled = _body.Advance(seconds);
            Apply();
            return settled;
        }

        private void Attach()
        {
            Detach();
            _toggle.ApplyTemplate();
            _root = Find("LayoutRoot");
            _knob = Find("SwitchKnob");
            _transform = _knob?.RenderTransform as CompositeTransform;
            _trackFill = Find("TrackFill");
            _trackStroke = Find("TrackBorder");
            _thumbOn = Find("ThumbOnEllipse");
            if (_root is null || _knob is null || _transform is null)
            {
                return;
            }

            // 既定の Thumb が先に入力を受けて ToggleSwitch 自身が切り替えないよう、入力は LayoutRoot で一括して受ける。
            if (Find("SwitchThumb") is { } thumb)
            {
                thumb.IsHitTestVisible = false;
            }

            var travel = _root.Width - _knob.Width - _knob.Margin.Left - _knob.Margin.Right;
            _body = new SpringSwitch(travel, FeelPreset.StiffHeavy, _toggle.IsOn);
            _root.PointerPressed += Root_PointerPressed;
            _root.PointerMoved += Root_PointerMoved;
            _root.PointerReleased += Root_PointerReleased;
            _root.PointerCanceled += Root_PointerCanceled;
            _root.PointerCaptureLost += Root_PointerCaptureLost;
            _root.Tapped += Root_Tapped;
            Apply();
        }

        private void Detach()
        {
            SpringDriver.Remove(this);
            if (_root is not null)
            {
                _root.PointerPressed -= Root_PointerPressed;
                _root.PointerMoved -= Root_PointerMoved;
                _root.PointerReleased -= Root_PointerReleased;
                _root.PointerCanceled -= Root_PointerCanceled;
                _root.PointerCaptureLost -= Root_PointerCaptureLost;
                _root.Tapped -= Root_Tapped;
            }

            _body = null;
            _root = null;
            _pressed = false;
            _dragging = false;
        }

        private FrameworkElement? Find(string name) => FindChild(_toggle, name);

        private static FrameworkElement? FindChild(DependencyObject parent, string name)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Name == name)
                {
                    return element;
                }

                if (FindChild(child, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnIsOnChanged()
        {
            // 自分が離したときに決めた IsOn の書き戻しは、すでにばねの目標と同じなので何もしない。
            if (_body is null || _body.IsOn == _toggle.IsOn)
            {
                return;
            }

            _body.Set(_toggle.IsOn);
            Begin();
        }

        private void Begin()
        {
            if (_body is null)
            {
                return;
            }

            if (ReduceMotion)
            {
                _body.SettleNow();
                Apply();
            }

            if (!_body.IsSettled)
            {
                SpringDriver.Add(this);
            }
        }

        private void Apply()
        {
            if (_body is null || _transform is null)
            {
                return;
            }

            var stretch = _body.Stretch;
            _transform.TranslateX = _body.Position;
            _transform.ScaleX = stretch;
            _transform.ScaleY = 1 / Math.Sqrt(stretch);
            var fill = _body.Fill;
            if (_trackFill is not null)
            {
                _trackFill.Opacity = fill;
            }

            if (_trackStroke is not null)
            {
                _trackStroke.Opacity = 1 - fill;
            }

            if (_thumbOn is not null)
            {
                _thumbOn.Opacity = fill;
            }
        }

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // マウスは左ボタンだけ。右・中ボタンは押下状態を作らず、通常の処理（コンテキストメニューなど）に任せる。
            if (_root is null || !e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
            {
                return;
            }

            e.Handled = true;
            if (_body is null || !_toggle.IsEnabled || _pressed)
            {
                return;
            }

            _toggle.Focus(FocusState.Pointer);
            _pressed = true;
            _dragging = false;
            _pointerId = e.Pointer.PointerId;
            _pressX = e.GetCurrentPoint(_root).Position.X;
            _pressPosition = _body.Position;
            _root.CapturePointer(e.Pointer);
        }

        private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_pressed || _body is null || _root is null || e.Pointer.PointerId != _pointerId)
            {
                return;
            }

            var point = e.GetCurrentPoint(_root);
            var dx = point.Position.X - _pressX;
            if (!_dragging && Math.Abs(dx) < DragSlop)
            {
                return;
            }

            // ポインターは位置を決めない。手の位置だけを動かし、つまみはハンドばねに引かれてついてくる。
            var time = point.Timestamp / 1_000_000.0;
            if (!_dragging)
            {
                _dragging = true;
                _body.Grab(_pressPosition + dx, time);
                SpringDriver.Add(this);
            }
            else
            {
                _body.Move(_pressPosition + dx, time);
            }

            e.Handled = true;
        }

        private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            if (_root is null || !_pressed || e.Pointer.PointerId != _pointerId)
            {
                return;
            }

            // 先に確定する。キャプチャを外すと PointerCaptureLost が来て、取り消しとして扱われるため。
            // 入力面の内外で確定を分けるのはタップだけ。ドラッグは端を越えて離しても、予測位置で決める。
            // 押したまま入力面の外へ出して離したタップは取り消す（捕捉しているので外でも Released が来る）。
            var position = e.GetCurrentPoint(_root).Position;
            var inside = SpringSwitch.IsTapRelease(position.X, position.Y, _root.ActualWidth, _root.ActualHeight);
            Finish(commit: _dragging || inside);
            _root.ReleasePointerCapture(e.Pointer);
        }

        private void Root_PointerCanceled(object sender, PointerRoutedEventArgs e) => Finish(commit: false);

        private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => Finish(commit: false);

        private void Root_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

        private void Finish(bool commit)
        {
            if (!_pressed || _body is null)
            {
                return;
            }

            var wasDragging = _dragging;
            _pressed = false;
            _dragging = false;
            if (wasDragging && commit && _toggle.IsEnabled)
            {
                _body.Release();
                _toggle.IsOn = _body.IsOn;
            }
            else if (wasDragging)
            {
                // 取り消しや無効化では IsOn を書かない（Toggled も自動起動の登録も起きない）。
                _body.Cancel();
            }
            else if (commit && _toggle.IsEnabled)
            {
                // タップは目標を反対側に替えるだけ。位置はばねが運ぶ。
                _toggle.IsOn = !_toggle.IsOn;
            }

            Begin();
        }
    }

    /// <summary>動いているスイッチを描画のたびに進める共通の駆動。全部が静止したらフレームの購読を止める。</summary>
    private static class SpringDriver
    {
        private static readonly List<Attachment> _active = [];
        private static long _last;

        public static void Add(Attachment attachment)
        {
            if (_active.Contains(attachment))
            {
                return;
            }

            _active.Add(attachment);
            if (_active.Count == 1)
            {
                _last = 0;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        public static void Remove(Attachment attachment)
        {
            if (_active.Remove(attachment) && _active.Count == 0)
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        private static void OnRendering(object? sender, object e)
        {
            // イベント引数は RenderingEventArgs へキャストしない。NativeAOT では InvalidCastException（E_NOINTERFACE）になり、
            // 設定画面が落ちるため。経過時間は自前の時計で測る。
            var now = Stopwatch.GetTimestamp();
            var seconds = _last == 0 ? 1.0 / 60 : Stopwatch.GetElapsedTime(_last, now).TotalSeconds;
            _last = now;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Step(seconds))
                {
                    Remove(_active[i]);
                }
            }
        }
    }
}
