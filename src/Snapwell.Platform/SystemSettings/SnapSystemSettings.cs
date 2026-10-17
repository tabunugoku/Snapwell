using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.SystemSettings;

/// <summary>ドラッグでの標準スナップ設定の取得と変更。</summary>
public interface ISnapSystemSettings
{
    /// <summary>取得に成功した場合だけ現在の値を返す。</summary>
    bool TryGetEnabled(out bool enabled);

    /// <summary>設定を変更し、API が失敗した場合は false を返す。</summary>
    bool TrySetEnabled(bool enabled);
}

/// <summary>レジストリを変更せず現在のセッションのスナップだけを制御する。</summary>
public sealed class Win32SnapSystemSettings : ISnapSystemSettings
{
    private readonly Func<uint, uint, nint, uint, bool> _systemParametersInfo;

    /// <summary>標準スナップの変更に Win32 API を使う。</summary>
    public Win32SnapSystemSettings() : this(InvokeSystemParametersInfo)
    {
    }

    internal Win32SnapSystemSettings(Func<uint, uint, nint, uint, bool> systemParametersInfo)
    {
        _systemParametersInfo = systemParametersInfo;
    }

    /// <summary>SPI_GETWINARRANGING で現在の値を取得する。</summary>
    public unsafe bool TryGetEnabled(out bool enabled)
    {
        BOOL value = default;
        var success = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWINARRANGING,
            0, &value, 0);
        enabled = value;
        return success;
    }

    /// <summary>SPI_SETWINARRANGING でセッション内の値だけを変更する。</summary>
    public bool TrySetEnabled(bool enabled)
    {
        // UPDATEINIFILE を付けないのでサインインし直せば戻る。設定アプリの表示とはずれる。
        // 文書の指定は pvParam 自体の TRUE/FALSE だが、ARM64 の Windows 11 26200 で一度だけ、
        // uiParam を参照しているように見える挙動を確認した。ほかのビルドは実機未確認。
        // どちらにも対応するため、BOOL へのポインターではなく同じ 0/1 を両方へ渡す。
        var value = enabled ? 1u : 0u;
        return _systemParametersInfo((uint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWINARRANGING,
            value, (nint)value, (uint)SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
    }

    private static unsafe bool InvokeSystemParametersInfo(uint action, uint uiParam, nint pvParam, uint flags) =>
        PInvoke.SystemParametersInfo((SYSTEM_PARAMETERS_INFO_ACTION)action, uiParam, (void*)pvParam,
            (SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS)flags);
}
