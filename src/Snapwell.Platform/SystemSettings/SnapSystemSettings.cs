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
    public unsafe bool TrySetEnabled(bool enabled)
    {
        // UPDATEINIFILE を付けないのでサインインし直せば戻る。設定アプリの表示とはずれる。
        // このアクションは BOOL へのポインターではなく、pvParam 自体に TRUE/FALSE を渡す。
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWINARRANGING,
            0, (void*)(enabled ? 1 : 0), SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
    }
}
