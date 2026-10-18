using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Snapwell.Settings;

/// <summary>同じユーザーが開いている設定画面だけを復元して前面化する。</summary>
internal static partial class SettingsWindowActivator
{
    internal static void Activate(string sid)
    {
        if (!Activate(sid, "Snapwell の設定"))
        {
            Activate(sid, "Snapwell の初回セットアップ");
        }
    }

    private static bool Activate(string sid, string title)
    {
        nint window = 0;
        while ((window = FindWindowEx(0, window, null, title)) != 0)
        {
            GetWindowThreadProcessId(window, out var processId);
            if (!IsSameUser(processId, sid))
            {
                continue;
            }

            if (IsIconic(window) != 0)
            {
                ShowWindow(window, 9);
            }

            // OS の前面化制限により成功しない場合がある。実機未確認。
            SetForegroundWindow(window);
            return true;
        }
        return false;
    }

    private static bool IsSameUser(uint processId, string sid)
    {
        using var process = OpenProcess(0x1000, 0, processId);
        if (process.IsInvalid)
        {
            return false;
        }

        var opened = OpenProcessToken(process, 0x0008, out var token) != 0;
        using (token)
        {
            if (!opened)
            {
                return false;
            }

            try
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                return string.Equals(identity.User?.Value, sid, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or
                ArgumentException)
            {
                // 権限不足や終了済みの対象を、同じユーザーと推測して前面化しない。
                return false;
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? className, string title);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, int inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("user32.dll")]
    private static partial int IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(nint window);
}
