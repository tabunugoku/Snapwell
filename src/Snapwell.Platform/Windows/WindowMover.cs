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

    /// <summary>準備後も有効な要求だけ位置とサイズを非同期で送り、送信の成否を返す。</summary>
    bool RequestVisualBounds(nint handle, PixelRect visualBounds, Func<bool>? isCurrent = null);

    /// <summary>現在の見た目の矩形を返し、調査に失敗したときは null を返す。</summary>
    PixelRect? GetVisualBounds(nint handle);

    /// <summary>調査失敗と無効な対象を除いて一括適用し、調査失敗のハンドルと API の成否を返す。</summary>
    bool ApplyBatch(IReadOnlyList<WindowPlacement> placements, bool positionOnly,
        out IReadOnlyList<nint> failedHandles, Func<nint, bool>? isCurrent = null);
}

/// <summary>移動の状態判断から分離したネイティブ操作。</summary>
public interface IWindowPositionApi
{
    /// <summary>最大化を解除する。</summary>
    bool Restore(nint handle);

    /// <summary>ウィンドウ矩形で同期移動し、位置だけの場合は寸法を無視する。</summary>
    bool SetBounds(nint handle, PixelRect bounds, bool positionOnly);

    /// <summary>ウィンドウ矩形の位置とサイズを一つの非同期要求で送る。</summary>
    bool SetBoundsAsync(nint handle, PixelRect bounds);

    /// <summary>ウィンドウ矩形の一覧を一括で適用する。</summary>
    bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly);
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

    /// <summary>バッチによるサイズ適用後も、最小・最大サイズを反映した実矩形を読み直す。</summary>
    public PixelRect? GetVisualBounds(nint handle) =>
        inspector.TryInspect(handle, out var window) ? window.VisualBounds : null;

    /// <summary>通常状態の枠で補正し、位置とサイズを同時に非同期で送る。</summary>
    public bool RequestVisualBounds(nint handle, PixelRect visualBounds, Func<bool>? isCurrent = null) =>
        (isCurrent is null || isCurrent()) &&
        TryPrepare(handle, out var window) &&
        (isCurrent is null || isCurrent()) &&
        api.SetBoundsAsync(handle, window.Insets.ToWindowBounds(visualBounds));

    /// <summary>調査できた対象だけ、通常状態の枠で補正してまとめて適用する。</summary>
    public bool ApplyBatch(IReadOnlyList<WindowPlacement> placements, bool positionOnly,
        out IReadOnlyList<nint> failedHandles, Func<nint, bool>? isCurrent = null)
    {
        var corrected = new List<NativeWindowPlacement>(placements.Count);
        var failed = new List<nint>();
        foreach (var placement in placements)
        {
            if (isCurrent is not null && !isCurrent(placement.Handle))
            {
                continue;
            }

            try
            {
                if (TryPrepare(placement.Handle, out var window))
                {
                    corrected.Add(new(placement.Handle, window.Insets.ToWindowBounds(placement.VisualBounds)));
                    continue;
                }
            }
            catch (Exception)
            {
                // 閉じた対象などの調査失敗で、ほかの対象のバッチを中断しない。
            }

            failed.Add(placement.Handle);
        }

        // 復元は同期で待つため、準備済みの対象もネイティブ呼び出し直前に確かめ直す。
        // 無効な対象は失敗に含めない。呼び出し側も同じ判定で完了処理から除く。null は全員有効。
        if (isCurrent is not null)
        {
            failed.RemoveAll(handle => !isCurrent(handle));
            corrected.RemoveAll(placement => !isCurrent(placement.Handle));
        }

        failedHandles = failed;
        return corrected.Count == 0 || api.ApplyBatch(corrected, positionOnly);
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
