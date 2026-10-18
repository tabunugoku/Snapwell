using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Physics;

/// <summary>
/// 設定画面の「試す」領域。小さな舞台に置いた一枚のカードを、いまの操作感で投げて吸着させる。
/// 吸着先と停止点は本体と同じ <see cref="SnapResolver"/>（舞台を一つのモニターと見なす）が決め、
/// 動きも本体と同じ <see cref="WindowBody"/> で、試す領域だけの規則は持たない。
/// </summary>
public sealed class FeelTrial
{
    /// <summary>カードの既定の寸法。</summary>
    public static readonly Vec2 DefaultSize = new(120, 76);

    private const double Gap = 6;

    private WindowBody? _body;
    private SnapResult? _pending;
    private PixelRect _area;
    private RectD[] _guides = [];

    /// <summary>舞台の大きさと吸着の方式から試す領域を作り、カードを中央に置く。</summary>
    public FeelTrial(double width, double height, SnapMode mode)
    {
        Mode = mode;
        Layout(width, height);
    }

    /// <summary>吸着の方式。</summary>
    public SnapMode Mode { get; private set; }

    /// <summary>カードの中心。</summary>
    public Vec2 Center { get; private set; }

    /// <summary>カードの寸法。区画に止まるとその区画の大きさになる。</summary>
    public Vec2 Size { get; private set; } = DefaultSize;

    /// <summary>止まった吸着先の種類。まだ止まっていない、または置いただけなら null。</summary>
    public SnapTargetKind? Kind { get; private set; }

    /// <summary>止まった区画の案内枠の番号。区画以外では null。</summary>
    public int? GuideIndex { get; private set; }

    /// <summary>投げられて動いている最中か。</summary>
    public bool IsFlying => _body is not null;

    /// <summary>速さに応じた浮き上がり量。</summary>
    public double Lift => _body?.Lift ?? 0;

    /// <summary>
    /// 区画の方式の案内枠。左半分、右半分、左上、右上、左下、右下、最大化の順。四隅の方式では空。
    /// 本体の規則が選ぶ矩形と同じ。
    /// </summary>
    public IReadOnlyList<RectD> Guides => _guides;

    /// <summary>四隅の方式の案内点（カードの中心の座標）。区画の方式では空。</summary>
    public IReadOnlyList<Vec2> CornerPoints { get; private set; } = [];

    /// <summary>舞台の大きさが変わったとき、案内を作り直してカードを中央へ戻す。</summary>
    public void Layout(double width, double height)
    {
        _area = new(0, 0, Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
        _body = null;
        _pending = null;
        if (Mode == SnapMode.Zones)
        {
            var grid = new GridLayout(_area, new(2, 2), new PhysicalSpacing((int)Gap, (int)Gap));
            _guides =
            [
                ToRect(grid.SpanRect(0, 0, 0, 1)), ToRect(grid.SpanRect(1, 0, 1, 1)),
                ToRect(grid.CellRect(0, 0)), ToRect(grid.CellRect(1, 0)),
                ToRect(grid.CellRect(0, 1)), ToRect(grid.CellRect(1, 1)),
                ToRect(grid.SpanRect(0, 0, 1, 1)),
            ];
            CornerPoints = [];
        }
        else
        {
            _guides = [];
            var x = DefaultSize.X / 2 + Gap;
            var y = DefaultSize.Y / 2 + Gap;
            CornerPoints = [new(x, y), new(_area.Width - x, y), new(_area.Width - x, _area.Height - y),
                new(x, _area.Height - y)];
        }

        Center = new(_area.Width / 2.0, _area.Height / 2.0);
        Size = DefaultSize;
        Kind = null;
        GuideIndex = null;
    }

    /// <summary>吸着の方式を切り替える。カードは中央へ戻る。</summary>
    public void SetMode(SnapMode mode)
    {
        Mode = mode;
        Layout(_area.Width, _area.Height);
    }

    /// <summary>カードをつかむ。動いていれば止め、元の大きさに戻して、つかんだ点を中心にする。</summary>
    public void Grab(Vec2 point)
    {
        _body = null;
        _pending = null;
        Kind = null;
        GuideIndex = null;
        Size = DefaultSize;
        Center = Clamp(point);
    }

    /// <summary>つかんだカードを動かす。</summary>
    public void Move(Vec2 point) => Center = Clamp(point);

    /// <summary>
    /// 離す、または投げ出す。本体の <see cref="SnapResolver"/> が吸着先を選べば、その停止点の磁石場の中を
    /// 動き始めて true を返す。速さが足りず選ばれなければその場に置き、false を返す。
    /// </summary>
    public bool Release(Vec2 velocity, FeelPreset feel, double clickRadius)
    {
        ArgumentNullException.ThrowIfNull(feel);
        var left = Center.X - Size.X / 2;
        var top = Center.Y - Size.Y / 2;
        var current = new PixelRect((int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(Size.X),
            (int)Math.Round(Size.Y));
        var snap = SnapResolver.Resolve(current, velocity, feel, Mode, new Spacing(Gap, Gap),
            [new("trial", _area, 1)], snapRadius: clickRadius);
        if (snap is not { } result)
        {
            return false;
        }

        Kind = null;
        GuideIndex = null;
        _pending = result;
        _body = new(new(left, top, Size.X, Size.Y), ToRect(result.Target), ToRect(_area), feel, velocity,
            result.Stops, result.ClickRadius);
        return true;
    }

    /// <summary>描画間隔だけ進める。止まったら吸着先に合わせて true を返す。</summary>
    public bool Advance(double seconds)
    {
        if (_body is null)
        {
            return true;
        }

        var settled = Simulator.Advance(_body, seconds);
        Center = new(_body.State.X + _body.State.W / 2, _body.State.Y + _body.State.H / 2);
        Size = new(_body.State.W, _body.State.H);
        if (!settled)
        {
            return false;
        }

        var target = ToRect(_pending!.Value.Target);
        Kind = _pending.Value.Kind;
        GuideIndex = Array.FindIndex(_guides, guide => guide == target) is var index and >= 0 ? index : null;
        _body = null;
        _pending = null;
        return true;
    }

    private static RectD ToRect(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private Vec2 Clamp(Vec2 point) => new(Math.Clamp(point.X, 0, _area.Width), Math.Clamp(point.Y, 0, _area.Height));
}
