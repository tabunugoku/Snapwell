using System.ComponentModel;
using System.Diagnostics;
using Snapwell.Platform.Startup;

namespace Snapwell.Platform.Updates;

/// <summary>進行役が保持するヘルパーの終了待機と終了コード。</summary>
public interface IUpdateHelperProcess : IDisposable
{
    /// <summary>上限内にヘルパーが終了したかを返す。</summary>
    bool WaitForExit(TimeSpan timeout);

    /// <summary>未終了なら null、UAC のキャンセルなら 1223 を返す。</summary>
    int? ExitCode { get; }
}

/// <summary>昇格の有無によらず、同じヘルパーの寿命を進行役へ渡す。</summary>
public interface IUpdateHelperLauncher
{
    /// <summary>UAC の応答待ちも含めたプロセスを開始する。</summary>
    IUpdateHelperProcess Start(string arguments, bool elevate);
}

/// <summary>Shell の起動を別スレッドへ渡し、UAC 中も進行役の期限を保つ。</summary>
public sealed class UpdateHelperLauncher(string executablePath) : IUpdateHelperLauncher
{
    /// <summary>引数をそのまま専用モードへ渡し、OS のキャンセルを保持する。</summary>
    public IUpdateHelperProcess Start(string arguments, bool elevate) => new PendingProcess(Task.Run(() =>
    {
        try
        {
            if (elevate)
            {
                var result = ElevatedRunner.Start(executablePath, arguments);
                return result.Cancelled ? (IUpdateHelperProcess)new FinishedProcess(1223)
                    : new ElevatedAdapter(result.Process!);
            }
            return new NormalProcess(Process.Start(new ProcessStartInfo(executablePath, arguments)
            {
                UseShellExecute = false,
            })!);
        }
        catch (Win32Exception exception) { return new FinishedProcess(exception.NativeErrorCode); }
        catch (Exception) { return new FinishedProcess((int)UpdateInstallResult.IoError); }
    }));

    private sealed class PendingProcess(Task<IUpdateHelperProcess> launch) : IUpdateHelperProcess
    {
        public int? ExitCode => launch.IsCompletedSuccessfully ? launch.Result.ExitCode : null;
        public bool WaitForExit(TimeSpan timeout)
        {
            var start = Stopwatch.GetTimestamp();
            if (!launch.Wait(timeout)) { return false; }
            var remaining = timeout - Stopwatch.GetElapsedTime(start);
            return launch.Result.WaitForExit(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }
        public void Dispose() => _ = launch.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) { task.Result.Dispose(); }
        }, TaskScheduler.Default);
    }

    private sealed class ElevatedAdapter(ElevatedProcess process) : IUpdateHelperProcess
    {
        public int? ExitCode => process.ExitCode;
        public bool WaitForExit(TimeSpan timeout) => process.WaitForExit(timeout);
        public void Dispose() => process.Dispose();
    }

    private sealed class NormalProcess(Process process) : IUpdateHelperProcess
    {
        public int? ExitCode => process.HasExited ? process.ExitCode : null;
        public bool WaitForExit(TimeSpan timeout) => process.WaitForExit((int)timeout.TotalMilliseconds);
        public void Dispose() => process.Dispose();
    }

    private sealed class FinishedProcess(int code) : IUpdateHelperProcess
    {
        public int? ExitCode => code;
        public bool WaitForExit(TimeSpan timeout) => true;
        public void Dispose() { }
    }
}
