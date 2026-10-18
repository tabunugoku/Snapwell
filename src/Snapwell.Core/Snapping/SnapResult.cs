using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Snapping;

/// <summary>投げの予測点と選択した吸着先。</summary>
public readonly record struct SnapResult
{
    /// <summary>目標の矩形と選択の情報をまとめる。</summary>
    public SnapResult(PixelRect target, SnapTargetKind kind, string monitorId, Vec2 predictedPosition, bool resized,
        IReadOnlyList<Vec2>? stops = null, double clickRadius = WindowBody.DefaultClickRadius)
    {
        Target = target;
        Kind = kind;
        MonitorId = monitorId;
        PredictedPosition = predictedPosition;
        Resized = resized;
        Stops = stops ?? [new Vec2(target.X, target.Y)];
        ClickRadius = clickRadius;
    }

    /// <summary>物理ピクセルの目標矩形。</summary>
    public PixelRect Target { get; }

    /// <summary>吸着先の種類。</summary>
    public SnapTargetKind Kind { get; }

    /// <summary>吸着先のモニター識別子。</summary>
    public string MonitorId { get; }

    /// <summary>空走で止まると予測した左上の物理ピクセル座標。</summary>
    public Vec2 PredictedPosition { get; }

    /// <summary>現在の矩形から幅または高さを変更したか。</summary>
    public bool Resized { get; }

    /// <summary>磁石場の停止点（左上の物理ピクセル座標）。</summary>
    public IReadOnlyList<Vec2> Stops { get; }

    /// <summary>磁石場の吸着半径。物理ピクセル。</summary>
    public double ClickRadius { get; }

    /// <summary>停止点の配列は中身で比べる。</summary>
    public bool Equals(SnapResult other) => Target == other.Target && Kind == other.Kind &&
        MonitorId == other.MonitorId && PredictedPosition == other.PredictedPosition && Resized == other.Resized &&
        ClickRadius == other.ClickRadius && Stops.SequenceEqual(other.Stops);

    /// <summary>停止点を含めたハッシュ値。</summary>
    public override int GetHashCode() => HashCode.Combine(Target, Kind, MonitorId, PredictedPosition, Resized,
        ClickRadius, Stops.Count);
}
