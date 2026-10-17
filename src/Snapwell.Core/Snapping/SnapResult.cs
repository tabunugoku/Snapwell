using Snapwell.Core.Geometry;

namespace Snapwell.Core.Snapping;

/// <summary>投げの予測点と選択した吸着先。</summary>
public readonly record struct SnapResult
{
    /// <summary>目標の矩形と選択の情報をまとめる。</summary>
    public SnapResult(PixelRect target, SnapTargetKind kind, string monitorId, Vec2 predictedPosition, bool resized)
    {
        Target = target;
        Kind = kind;
        MonitorId = monitorId;
        PredictedPosition = predictedPosition;
        Resized = resized;
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
}
