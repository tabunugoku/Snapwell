using System.Runtime.InteropServices;
using System.Security.Principal;
using Snapwell.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Startup;

/// <summary>通常権限の画面から専用ヘルパーだけを runas で起動する。</summary>
public sealed class ElevatedRunner(string executablePath) : IElevatedRunner
{
    /// <summary>UI を止めずに UAC と終了を待ち、OS のキャンセルを区別する。</summary>
    public Task<StartupChangeResult> RunAsync(bool enabled)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("SID を取得できない。");
        return Task.Run(() => Run(executablePath, $"--set-admin-task {(enabled ? "on" : "off")} --for-sid {sid}"));
    }

    private static unsafe StartupChangeResult Run(string path, string arguments)
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
        if (initialized.Failed && initialized.Value != unchecked((int)0x80010106))
        {
            return StartupChangeResult.Failed;
        }

        try
        {
            return RunInitialized(path, arguments);
        }
        finally
        {
            if (initialized.Succeeded)
            {
                PInvoke.CoUninitialize();
            }
        }
    }

    private static unsafe StartupChangeResult RunInitialized(string path, string arguments)
    {
        fixed (char* verb = "runas", file = path, parameters = arguments)
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                // ハンドルを保持して終了を待つ。Shell の非同期処理と既定のエラー画面は使わない。
                fMask = 0x00000040 | 0x00000100 | 0x00000400,
                lpVerb = new PCWSTR(verb),
                lpFile = new PCWSTR(file),
                lpParameters = new PCWSTR(parameters),
                nShow = (int)SHOW_WINDOW_CMD.SW_HIDE,
            };
            if (!PInvoke.ShellExecuteEx(ref info))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 1223)
                {
                    return StartupChangeResult.Cancelled;
                }

                return StartupChangeResult.Failed;
            }

            try
            {
                uint code;
                if (PInvoke.WaitForSingleObject(info.hProcess, uint.MaxValue) != WAIT_EVENT.WAIT_OBJECT_0 ||
                    !PInvoke.GetExitCodeProcess(info.hProcess, &code))
                {
                    return StartupChangeResult.Failed;
                }

                return code switch
                {
                    0 => StartupChangeResult.Success,
                    2 => StartupChangeResult.SidMismatch,
                    3 => StartupChangeResult.UnprotectedLocation,
                    4 => StartupChangeResult.ExpandablePath,
                    _ => StartupChangeResult.Failed,
                };
            }
            finally
            {
                PInvoke.CloseHandle(info.hProcess);
            }
        }
    }
}
