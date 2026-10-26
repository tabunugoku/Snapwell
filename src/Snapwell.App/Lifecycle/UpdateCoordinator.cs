using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>進行役が所有する Applied と Ready の有限時間待機。</summary>
internal interface IUpdateCoordinatorSignals : IDisposable
{
    bool WaitApplied(TimeSpan timeout);
    bool WaitReady(TimeSpan timeout);
}

/// <summary>進行役の OS 操作を単体テストから切り離す。</summary>
internal interface IUpdateCoordinatorEnvironment
{
    bool QuitAndWait(TimeSpan timeout);
    bool WaitProcess(int? pid, TimeSpan timeout);
    bool IsProtected { get; }
    IUpdateCoordinatorSignals CreateSignals();
    void StartResident();
    /// <summary>引数の三つの親が既知の pending と一致するときだけ削除する。</summary>
    void CleanupPending(UpdateCommandLine command);
    void WriteResult(UpdateLastResult result);
}

/// <summary>UI が次回起動時に読む更新処理の最終結果。</summary>
internal sealed record UpdateLastResult(string Result, int Code, DateTimeOffset Utc, string? Version);

/// <summary>本体の正常終了、ヘルパー、新版の起動確認を順序付ける。</summary>
internal sealed class UpdateCoordinator(IUpdateCoordinatorEnvironment environment, IUpdateHelperLauncher launcher,
    TimeProvider clock, IAppLog log)
{
    private static readonly TimeSpan _quitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _appliedTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _helperExitTimeout = TimeSpan.FromSeconds(120);

    internal UpdateLastResult Run(UpdateCommandLine command, string? version)
    {
        var cleanupAttempted = false;
        UpdateLastResult result;
        bool stopped;
        bool restart;
        IUpdateHelperProcess? helper;
        try { result = RunSession(command, version, CleanupPending, out stopped, out restart, out helper); }
        finally { CleanupPending(); }
        helper?.Dispose();
        if (stopped && restart)
        {
            try { environment.StartResident(); }
            catch (Exception exception)
            {
                log.Error(exception);
                result = result with { Result = "failed", Code = (int)UpdateInstallResult.IoError };
            }
        }
        result = result with { Utc = clock.GetUtcNow() };
        environment.WriteResult(result);
        return result;

        void CleanupPending()
        {
            if (cleanupAttempted) { return; }
            // 削除失敗も試行済みとし、後始末で更新結果を変えない。
            cleanupAttempted = true;
            try { environment.CleanupPending(command); }
            catch (Exception exception) { log.Error(exception); }
        }
    }

    private UpdateLastResult RunSession(UpdateCommandLine command, string? version, Action cleanupPending,
        out bool stopped, out bool restart, out IUpdateHelperProcess? helper)
    {
        var result = new UpdateLastResult("failed", (int)UpdateInstallResult.IoError, clock.GetUtcNow(), version);
        stopped = false;
        restart = false;
        helper = null;
        try
        {
            using var signals = environment.CreateSignals();
            var start = clock.GetTimestamp();
            if (!environment.QuitAndWait(_quitTimeout)) { return result; }
            stopped = true;
            restart = true;
            var remaining = _quitTimeout - clock.GetElapsedTime(start);
            if (!environment.WaitProcess(command.WaitPid, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
            {
                return result;
            }

            // UAC の応答中もこの期限を消費する。
            start = clock.GetTimestamp();
            helper = launcher.Start(command.HelperArguments, environment.IsProtected);
            var applied = false;
            while (clock.GetElapsedTime(start) < _appliedTimeout)
            {
                if (signals.WaitApplied(TimeSpan.FromMilliseconds(50))) { applied = true; break; }
                if (helper.WaitForExit(TimeSpan.Zero)) { break; }
            }
            if (!applied)
            {
                // Applied と終了が同時なら、配置後の処理として扱う。
                applied = signals.WaitApplied(TimeSpan.Zero);
            }
            if (!applied)
            {
                // 遅れて UAC が承認された場合も、ヘルパーの処理中に本体を再起動しない。
                restart = helper.WaitForExit(_helperExitTimeout);
                var code = helper.ExitCode is { } exitCode && exitCode != 0
                    ? exitCode : (int)UpdateInstallResult.IoError;
                restart &= code is not ((int)UpdateInstallResult.ApplyFailedRollbackIncomplete)
                    and not ((int)UpdateInstallResult.ReadyTimeoutRollbackIncomplete)
                    and not ((int)UpdateInstallResult.ReadyTimeoutResidentRunning);
                result = result with { Result = code == 1223 ? "cancelled" : "failed", Code = code };
                return result;
            }

            restart = false;
            // ヘルパーは作業用コピーだけを使うため、新版の起動と Ready 待機には不要。
            cleanupPending();
            var readyStart = clock.GetTimestamp();
            try { environment.StartResident(); }
            catch (Exception exception) { log.Error(exception); }
            remaining = _readyTimeout - clock.GetElapsedTime(readyStart);
            if (signals.WaitReady(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
            {
                // 自分の exe.old をヘルパーが消せるよう、終了を待たずハンドルだけを手放す。
                result = result with { Result = "installed", Code = (int)UpdateInstallResult.Success };
                return result;
            }
            if (!helper.WaitForExit(_helperExitTimeout)) { return result; }
            var exit = helper.ExitCode ?? (int)UpdateInstallResult.IoError;
            restart = exit is (int)UpdateInstallResult.ReadyTimeoutRolledBack
                or (int)UpdateInstallResult.ApplyFailedRolledBack;
            result = result with { Result = restart ? "rolled-back" : "failed", Code = exit };
            return result;
        }
        catch (Exception exception)
        {
            log.Error(exception);
            // ヘルパーの生存が分からない場合、差し替えと起動を競合させない。
            if (helper is not null) { restart = false; }
            return result;
        }
    }
}
