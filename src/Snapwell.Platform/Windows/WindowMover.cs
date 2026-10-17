using System.Diagnostics.CodeAnalysis;
using Snapwell.Core.Geometry;

namespace Snapwell.Platform.Windows;

/// <summary>見た目の矩形で指定する配置。</summary>
public readonly record struct WindowPlacement(nint Handle, PixelRect VisualBounds);

/// <summary>Win32 へ渡す、見えない枠を含めた配置。</summary>
public readonly record struct NativeWindowPlacement(nint Handle, PixelRect WindowBounds);

/// <summary>ウィンドウの見た目の座標による移動。</summary>
public interface IWindowMover
{
    /// <summary>サイズと Z 順序とフォーカスを変えず同期で移動する。</summary>
    bool MoveVisual(nint handle, int x, int y);

    /// <summary>サイズを適用して実際の見た目の矩形を返し、失敗時は null を返す。</summary>
    PixelRect? SetVisualBounds(nint handle, PixelRect visualBounds);

    /// <summary>複数の位置とサイズを一括で適用する。</summary>
    bool ApplyBatch(IReadOnlyList<WindowPlacement> placements);
}

/// <summary>移動の状態判断から分離したネイティブ操作。</summary>
public interface IWindowPositionApi
{
    /// <summary>最大化を解除する。</summary>
    bool Restore(nint handle);

    /// <summary>ウィンドウ矩形で同期移動し、位置だけの場合は寸法を無視する。</summary>
    bool SetBounds(nint handle, PixelRect bounds, bool positionOnly);

    /// <summary>ウィンドウ矩形の一覧を一括で適用する。</summary>
    bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements);
}

/// <summary>枠の補正と最大化の解除を調整する移動サービス。</summary>
public sealed class WindowMover(IWindowInspector inspector, IWindowPositionApi api) : IWindowMover
{
    /// <summary>毎フレーム使う位置だけの移動を行う。</summary>
    public bool MoveVisual(nint handle, int x, int y)
    {
        if (!TryPrepare(handle, out var window))
        {
            return false;
        }

        var visual = new PixelRect(x, y, window.VisualBounds.Width, window.VisualBounds.Height);
        return api.SetBounds(handle, window.Insets.ToWindowBounds(visual), true);
    }

    /// <summary>適用後にアプリの最小・最大サイズを反映した矩形を取得する。</summary>
    public PixelRect? SetVisualBounds(nint handle, PixelRect visualBounds)
    {
        if (!TryPrepare(handle, out var window) ||
            !api.SetBounds(handle, window.Insets.ToWindowBounds(visualBounds), false))
        {
            return null;
        }

        return inspector.TryInspect(handle, out var actual) ? actual.VisualBounds : null;
    }

    /// <summary>先に全対象を調査し、補正後の矩形をまとめて適用する。</summary>
    public bool ApplyBatch(IReadOnlyList<WindowPlacement> placements)
    {
        if (placements.Count == 0)
        {
            return true;
        }

        var corrected = new NativeWindowPlacement[placements.Count];
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            if (!TryPrepare(placement.Handle, out var window))
            {
                return false;
            }

            corrected[index] = new(placement.Handle, window.Insets.ToWindowBounds(placement.VisualBounds));
        }

        return api.ApplyBatch(corrected);
    }

    private bool TryPrepare(nint handle, [NotNullWhen(true)] out WindowSnapshot? window)
    {
        if (!inspector.TryInspect(handle, out window))
        {
            return false;
        }

        // 最大化の枠幅を通常状態へ流用しない。
        return !window.IsMaximized || (api.Restore(handle) && inspector.TryInspect(handle, out window));
    }
}
