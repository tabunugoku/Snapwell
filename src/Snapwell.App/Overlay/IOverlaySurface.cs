using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>物理ピクセルの矩形と画素を表示する差し替え可能な出力先。</summary>
internal interface IOverlaySurface : IDisposable
{
    void Show(PixelRect bounds, OverlayFrame frame);
    void Hide();
}

/// <summary>オーバーレイ上のポインタ操作。</summary>
internal enum OverlayPointerKind { Down, Move, Up, RightDown }

/// <summary>物理ピクセルの画面座標によるポインタ入力。</summary>
internal sealed record OverlayPointer(OverlayPointerKind Kind, Vec2 Position);

/// <summary>描画を専用スレッドへ要求し、入力を受け取る窓口。</summary>
internal interface IOverlayPresenter : IDisposable
{
    event Action<OverlayPointer>? Pointer;
    void Request(PixelRect bounds, Func<OverlayFrame> paint);
    void Hide();
}

/// <summary>Win32 から切り離した待機と配送の境界。</summary>
internal interface IOverlayLoop
{
    event Action<OverlayPointer>? Pointer;
    void Run(Func<bool> process);
    void Wake();
}

/// <summary>クライアント座標から物理ピクセルの画面座標への変換。</summary>
internal static class OverlayCoordinates
{
    internal static Vec2 ToScreen(Vec2 client, Vec2 origin) => client + origin;
}
