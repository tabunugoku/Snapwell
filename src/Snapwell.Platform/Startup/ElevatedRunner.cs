using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
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

    private static StartupChangeResult Run(string path, string arguments)
    {
        ElevatedLaunchResult result;
        try { result = Start(path, arguments); }
        catch (Win32Exception) { return StartupChangeResult.Failed; }
        using var ownedResult = result;
        if (result.Cancelled) { return StartupChangeResult.Cancelled; }
        if (result.Process is not { } process)
        {
            return StartupChangeResult.Failed;
        }
        // 既存の登録経路は終了まで待つ。新しい待機 API に無限値を渡さない。
        while (!process.WaitForExit(TimeSpan.FromMinutes(1))) { }
        return process.ExitCode switch
        {
            0 => StartupChangeResult.Success,
            2 => StartupChangeResult.SidMismatch,
            3 => StartupChangeResult.UnprotectedLocation,
            4 => StartupChangeResult.ExpandablePath,
            _ => StartupChangeResult.Failed,
        };
    }

    /// <summary>任意の引数で昇格し、終了を待たずにプロセスのハンドルを返す。</summary>
    public static unsafe ElevatedLaunchResult Start(string path, string arguments)
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
        if (initialized.Failed && initialized.Value != unchecked((int)0x80010106))
        {
            throw new Win32Exception(initialized.Value);
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

    private static unsafe ElevatedLaunchResult RunInitialized(string path, string arguments)
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
                    return new(null, true);
                }

                throw new Win32Exception(error);
            }

            return new(new ElevatedProcess((nint)info.hProcess.Value), false);
        }
    }
}

/// <summary>UAC のキャンセルと、所有権を受け取ったプロセスを区別する。</summary>
public sealed record ElevatedLaunchResult(ElevatedProcess? Process, bool Cancelled) : IDisposable
{
    /// <summary>起動したプロセスのハンドルを解放する。</summary>
    public void Dispose() => Process?.Dispose();
}

/// <summary>昇格プロセスのハンドルを保持し、有限時間の待機を提供する。</summary>
public sealed class ElevatedProcess : IDisposable
{
    private readonly EventWaitHandle _handle = new(false, EventResetMode.ManualReset);

    internal ElevatedProcess(nint handle)
    {
        var original = _handle.SafeWaitHandle;
        _handle.SafeWaitHandle = new SafeWaitHandle(handle, true);
        original.Dispose();
    }

    /// <summary>ほかの信号と同時に待てるプロセスハンドル。</summary>
    public WaitHandle Handle => _handle;

    /// <summary>指定した上限内に終了したかを返す。</summary>
    public bool WaitForExit(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return _handle.WaitOne(timeout);
    }

    /// <summary>終了済みの場合だけ終了コードを返す。</summary>
    public unsafe int? ExitCode
    {
        get
        {
            if (!_handle.WaitOne(0)) { return null; }
            uint code;
            if (!PInvoke.GetExitCodeProcess(new HANDLE(_handle.SafeWaitHandle.DangerousGetHandle()), &code))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            return (int)code;
        }
    }

    /// <summary>プロセスを強制終了せず、保持したハンドルだけを解放する。</summary>
    public void Dispose() => _handle.Dispose();
}
