using Snapwell.Core.Geometry;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Physics;

/// <summary>
/// 設定画面の「試す」領域。小さな舞台に置いた一枚のカードを、いまの操作感で投げて吸着させる。
/// 物理は本体と同じ <see cref="WindowBody"/>（カードの中心を、停止点の磁石場の中で動かす）。
/// </summary>
public sealed class FeelTrial
{
    /// <summary>離した速さがこれ未満なら吸着しない（px/s）。本体の通常のドラッグと投げの区別と同じ値。</summary>
    public const double ThrowThreshold = 150;

    /// <summary>カードの既定の寸法。</summary>
    public static readonly Vec2 DefaultSize = new(120, 76);

    private const double Gap = 6;
    private const double CornerInset = 12;

    private WindowBody? _body;
    private Vec2[] _stops = [];
    private RectD[] _zones = [];
    private double _width;
    private double _height;

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

    /// <summary>止まっている区画の番号。区画以外では null。</summary>
    public int? ZoneIndex { get; private set; }

    /// <summary>投げられて動いている最中か。</summary>
    public bool IsFlying => _body is not null;

    /// <summary>速さに応じた浮き上がり量。</summary>
    public double Lift => _body?.Lift ?? 0;

    /// <summary>
    /// 区画の一覧（区画の方式のときだけ）。左半分、右半分、左上、右上、左下、右下の順。
    /// 最大化は上端へ押し当てる操作のときだけ候補になるため、試す領域には置かない。
    /// </summary>
    public IReadOnlyList<RectD> Zones => _zones;

    /// <summary>磁石場の停止点（カードの中心の座標）。</summary>
    public IReadOnlyList<Vec2> Stops => _stops;

    /// <summary>舞台の大きさが変わったとき、停止点を作り直してカードを中央へ戻す。</summary>
    public void Layout(double width, double height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _body = null;
        if (Mode == SnapMode.Zones)
        {
            _zones =
            [
                Zone(0, 0, 0.5, 1), Zone(0.5, 0, 0.5, 1), Zone(0, 0, 0.5, 0.5), Zone(0.5, 0, 0.5, 0.5),
                Zone(0, 0.5, 0.5, 0.5), Zone(0.5, 0.5, 0.5, 0.5)
            ];
            _stops = [.. _zones.Select(zone => new Vec2(zone.X + zone.W / 2, zone.Y + zone.H / 2))];
        }
        else
        {
            _zones = [];
            var x = DefaultSize.X / 2 + CornerInset;
            var y = DefaultSize.Y / 2 + CornerInset;
            _stops = [new(x, y), new(_width - x, y), new(_width - x, _height - y), new(x, _height - y)];
        }

        Center = new(_width / 2, _height / 2);
        Size = DefaultSize;
        ZoneIndex = null;
    }

    /// <summary>吸着の方式を切り替える。カードは中央へ戻る。</summary>
    public void SetMode(SnapMode mode)
    {
        Mode = mode;
        Layout(_width, _height);
    }

    /// <summary>カードをつかむ。動いていれば止め、元の大きさに戻して、つかんだ点を中心にする。</summary>
    public void Grab(Vec2 point)
    {
        _body = null;
        ZoneIndex = null;
        Size = DefaultSize;
        Center = Clamp(point);
    }

    /// <summary>つかんだカードを動かす。</summary>
    public void Move(Vec2 point) => Center = Clamp(point);

    /// <summary>
    /// 離す。速さが <see cref="ThrowThreshold"/> 未満ならその場に置き、false を返す。
    /// それ以上なら磁石場の中を動き始め、true を返す。
    /// </summary>
    public bool Release(Vec2 velocity, FeelPreset feel, double clickRadius)
    {
        if (velocity.Length < ThrowThreshold)
        {
            return false;
        }

        Throw(velocity, feel, clickRadius);
        return true;
    }

    /// <summary>いまの位置から、指定の速度で磁石場の中へ投げ出す。</summary>
    public void Throw(Vec2 velocity, FeelPreset feel, double clickRadius)
    {
        ArgumentNullException.ThrowIfNull(feel);
        ZoneIndex = null;
        Size = DefaultSize;
        _body = new(new(Center.X, Center.Y, 0, 0), new(Center.X, Center.Y, 0, 0), new(0, 0, _width, _height), feel,
            velocity, _stops, clickRadius);
    }

    /// <summary>描画間隔だけ進める。止まったら最寄りの停止点に合わせ、区画ならその大きさにして true を返す。</summary>
    public bool Advance(double seconds)
    {
        if (_body is null)
        {
            return true;
        }

        var settled = Simulator.Advance(_body, seconds);
        Center = new(_body.State.X, _body.State.Y);
        if (!settled)
        {
            return false;
        }

        _body = null;
        Snap(Center);
        return true;
    }

    private void Snap(Vec2 point)
    {
        var index = 0;
        var best = double.PositiveInfinity;
        for (var i = 0; i < _stops.Length; i++)
        {
            var distance = (_stops[i] - point).Length;
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        Center = _stops[index];
        if (Mode == SnapMode.Zones)
        {
            ZoneIndex = index;
            Size = new(_zones[index].W, _zones[index].H);
        }
        else
        {
            ZoneIndex = null;
            Size = DefaultSize;
        }
    }

    private RectD Zone(double x, double y, double w, double h) =>
        new(x * _width + Gap / 2, y * _height + Gap / 2, w * _width - Gap, h * _height - Gap);

    private Vec2 Clamp(Vec2 point) => new(Math.Clamp(point.X, 0, _width), Math.Clamp(point.Y, 0, _height));
}
