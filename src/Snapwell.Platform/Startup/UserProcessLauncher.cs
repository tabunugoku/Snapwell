using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Snapwell.Platform.Startup;

/// <summary>昇格状態の確認と通常権限での起動を、OS から切り離す境界。</summary>
public interface IUserProcessLauncher
{
    /// <summary>現在のプロセスのトークンが昇格しているか返す。</summary>
    bool IsElevated { get; }

    /// <summary>同じユーザーのシェルの通常権限トークンで起動する。</summary>
    void StartUnelevated(ProcessStartInfo info);
}

/// <summary>公開 API だけで、設定画面への昇格の継承を防ぐ。</summary>
public sealed unsafe class UserProcessLauncher : IUserProcessLauncher
{
    /// <summary>TokenElevation の取得失敗は起動失敗とし、通常起動へ推測で進まない。</summary>
    public bool IsElevated
    {
        get
        {
            HANDLE token = default;
            Check(PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_QUERY, &token));
            try
            {
                return IsTokenElevated(token);
            }
            finally
            {
                PInvoke.CloseHandle(token);
            }
        }
    }

    /// <summary>シェルが同じ SID かつ非昇格の場合だけ、そのトークンを複製して起動する。</summary>
    public void StartUnelevated(ProcessStartInfo info)
    {
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull)
        {
            throw new InvalidOperationException("通常権限のシェルが見つからない。");
        }

        uint processId;
        _ = PInvoke.GetWindowThreadProcessId(shell, &processId);
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        HANDLE token = default;
        HANDLE primary = default;
        try
        {
            Check(PInvoke.OpenProcessToken(process,
                TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, &token));
            using var current = WindowsIdentity.GetCurrent();
            using var shellIdentity = new WindowsIdentity((nint)token);
            if (current.User is null || current.User != shellIdentity.User || IsTokenElevated(token))
            {
                throw new InvalidOperationException("同じユーザーの通常権限シェルを確認できない。");
            }

            Check(PInvoke.DuplicateTokenEx(token,
                TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_ASSIGN_PRIMARY,
                null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, &primary));
            StartWithToken(primary, info);
        }
        finally
        {
            if (!primary.IsNull)
            {
                PInvoke.CloseHandle(primary);
            }

            if (!token.IsNull)
            {
                PInvoke.CloseHandle(token);
            }

            PInvoke.CloseHandle(process);
        }
    }

    private static bool IsTokenElevated(HANDLE token)
    {
        TOKEN_ELEVATION elevation;
        uint length;
        Check(PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation,
            &elevation, (uint)sizeof(TOKEN_ELEVATION), &length));
        return elevation.TokenIsElevated != 0;
    }

    private static void StartWithToken(HANDLE token, ProcessStartInfo info)
    {
        var commandLine = BuildCommandLine(info).ToCharArray();
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        PROCESS_INFORMATION child;
        fixed (char* command = commandLine, application = info.FileName, directory = info.WorkingDirectory)
        {
            Check(PInvoke.CreateProcessWithToken(token, 0, new PCWSTR(application), new PWSTR(command),
                0, null, new PCWSTR(directory), &startup, &child));
        }

        PInvoke.CloseHandle(child.hThread);
        PInvoke.CloseHandle(child.hProcess);
    }

    internal static string BuildCommandLine(ProcessStartInfo info)
    {
        // CreateProcess の可変バッファと、C ランタイムの引数解析に合わせる。
        var builder = new StringBuilder();
        foreach (var argument in new[] { info.FileName }.Concat(info.ArgumentList))
        {
            if (argument.Contains('\0'))
            {
                throw new InvalidOperationException("引数に NUL は使えない。");
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"');
            var slashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    slashes++;
                    continue;
                }

                builder.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                builder.Append(character);
                slashes = 0;
            }

            builder.Append('\\', slashes * 2);
            builder.Append('"');
        }

        return builder.Append('\0').ToString();
    }

    private static void Check(bool success)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
}
