using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Snapwell.Platform.Dwm;

/// <summary>DWM_WINDOW_CORNER_PREFERENCE と同じ値を持つ角の指定。</summary>
public enum CornerStyle
{
    Default = 0,
    Round = 2,
    RoundSmall = 3,
    DoNotRound = 1,
}

/// <summary>角の設定に使う API の境界。</summary>
public interface ICornerApi
{
    /// <summary>HRESULT が成功の場合だけ true を返す。</summary>
    bool TrySet(nint handle, CornerStyle style);
}

/// <summary>公開 DWM API による他プロセスの角設定。</summary>
public sealed class Win32CornerApi : ICornerApi
{
    /// <summary>失効したハンドルや権限不足の失敗を false で返す。</summary>
    public unsafe bool TrySet(nint handle, CornerStyle style)
    {
        var preference = (DWM_WINDOW_CORNER_PREFERENCE)style;
        return PInvoke.DwmSetWindowAttribute(new HWND(handle), DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Succeeded;
    }
}

/// <summary>変更したウィンドウを覚え、無効化時に既定の角へ戻す。</summary>
public sealed class CornerStyler(ICornerApi api)
{
    private readonly object _gate = new();
    private readonly HashSet<nint> _modified = [];

    /// <summary>既定以外の角を設定したハンドルの数。</summary>
    public int TrackedCount
    {
        get
        {
            lock (_gate)
            {
                return _modified.Count;
            }
        }
    }

    /// <summary>API と記録の更新を直列化し、失敗したハンドルを忘れる。</summary>
    public bool Apply(nint handle, CornerStyle style)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)style, 3u);
        lock (_gate)
        {
            var success = api.TrySet(handle, style);
            if (success && style != CornerStyle.Default)
            {
                _modified.Add(handle);
            }
            else
            {
                _modified.Remove(handle);
            }

            return success;
        }
    }

    /// <summary>閉じたウィンドウの記録を取り除く。</summary>
    public void Forget(nint handle)
    {
        lock (_gate)
        {
            _modified.Remove(handle);
        }
    }

    /// <summary>変更した角を既定に戻し、閉じたハンドルも一覧から取り除く。</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach (var handle in _modified)
            {
                api.TrySet(handle, CornerStyle.Default);
            }

            _modified.Clear();
        }
    }
}
