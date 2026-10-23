using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.App.Motion;

/// <summary>サイズを変更しない、着地時に変更する、移動中も変更する方式。</summary>
internal enum SizeMode
{
    None,
    Land,
    Live,
}

/// <summary>一つのウィンドウの移動要求。座標はすべて物理ピクセル。Stops があれば磁石場、なければ目標へのばねで動く。</summary>
internal sealed record MotionRequest(nint Handle, PixelRect Current, PixelRect Target, PixelRect Walls,
    FeelPreset Feel, Vec2 Velocity = default, SizeMode SizeMode = SizeMode.None, IReadOnlyList<Vec2>? Stops = null,
    double ClickRadius = WindowBody.DefaultClickRadius);

/// <summary>ドラッグとキー操作から見た移動サービス。</summary>
internal interface IMotionEngine : IDisposable
{
    void Request(MotionRequest request);
    void Cancel(nint handle);
    void CancelAll();
    bool IsMoving(nint handle);
}

/// <summary>合成フレームまでの待機。</summary>
internal interface IFramePacer
{
    void Wait(CancellationToken cancellationToken);
}

/// <summary>単調増加する秒単位の時計。</summary>
internal interface IClock
{
    double Seconds { get; }
}

/// <summary>アプリ設定と OS のモーション削減の選択。</summary>
internal interface IMotionPreference
{
    bool ReduceMotion { get; }
}
