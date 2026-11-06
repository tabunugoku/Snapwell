namespace Snapwell.Platform.Hooks;

/// <summary>WM_WTSSESSION_CHANGE と WM_POWERBROADCAST の wParam を、フックの張り直しの契機に直す。</summary>
public static class HookEnvironmentMessages
{
    /// <summary>セッションの変化の通知。ウィンドウは WTSRegisterSessionNotification で登録する。</summary>
    public const uint WmWtsSessionChange = 0x02B1;

    /// <summary>電源状態の変化の通知。トップレベルのウィンドウへ届く（メッセージ専用ウィンドウには届かない）。</summary>
    public const uint WmPowerBroadcast = 0x0218;

    /// <summary>WTS_CONSOLE_CONNECT など。関係のない値は null。</summary>
    public static HookEnvironmentChange? FromSessionChange(nuint wParam) => wParam switch
    {
        // CONSOLE_DISCONNECT, REMOTE_DISCONNECT, SESSION_LOCK
        2 or 4 or 7 => HookEnvironmentChange.Interrupted,
        // CONSOLE_CONNECT, REMOTE_CONNECT, SESSION_LOGON, SESSION_UNLOCK
        1 or 3 or 5 or 8 => HookEnvironmentChange.Restored,
        _ => null,
    };

    /// <summary>PBT_APMSUSPEND など。関係のない値は null。</summary>
    public static HookEnvironmentChange? FromPowerBroadcast(nuint wParam) => wParam switch
    {
        // PBT_APMSUSPEND
        0x4 => HookEnvironmentChange.Interrupted,
        // PBT_APMRESUMESUSPEND, PBT_APMRESUMEAUTOMATIC
        0x7 or 0x12 => HookEnvironmentChange.Restored,
        _ => null,
    };
}
