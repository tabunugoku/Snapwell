using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Snapwell.Core.Updates;
using Snapwell.Platform.Startup;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐を作らず、進行役と昇格ヘルパーを実行する入口。</summary>
internal static class UpdateModes
{
    internal static int Execute(string[] args, IAppLog log)
    {
        if (!UpdateCommandLine.TryParse(args, out var parsed)) { return 1; }
        var command = parsed!;
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User?.Value ?? "";
        if (!IsAllowedSid(command, currentSid)) { return 2; }
        if (!command.Elevated && (command.WaitPid == Environment.ProcessId
            || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))) { return 1; }
        try
        {
            var path = Environment.ProcessPath!;
            if (command.Elevated)
            {
                // 別の管理者での UAC でも、信号の名前だけは元の利用者の SID を使う。
                var names = new UpdateSignalNames(command.Sid);
                // UAC の承認が遅れて進行役が既に終了していたら、差し替えを始めない。
                using (UpdateSignalNames.Open(names.Applied, signal: true)) { }
                var protectedLocation = ExecutableLocation.IsProtected(path);
                return (int)RunWithInstallLock(Path.GetDirectoryName(path)!, new UpdateInstallLock(protectedLocation), () =>
                {
                    using var work = new UpdateWorkDirectory(protectedLocation);
                    using var signals = new UpdateSignals(names);
                    var unresolved = HasPreviousUpdateUnresolved(work.Path, FindPreviousJournals());
                    var request = CreateRequest(command, path, work.Path, unresolved);
                    var result = new UpdateInstallSession(new UpdateFileSystem(), signals, new UpdateResidentStopper(names))
                        .Run(request, out var diagnostic);
                    work.Preserve = result is UpdateInstallResult.ApplyFailedRollbackIncomplete
                        or UpdateInstallResult.ReadyTimeoutRollbackIncomplete
                        or UpdateInstallResult.ReadyTimeoutResidentRunning
                        || Directory.Exists(Path.Combine(work.Path, "staging"));
                    // i18n-keep: 画面に出さない開発者向けの終了コードと診断。
                    log.Error(new InvalidOperationException($"Update helper: {(int)result}. {diagnostic}"));
                    return result;
                });
            }
            var environment = new UpdateEnvironment(command.Sid, path, new TaskSchedulerService());
            var outcome = new UpdateCoordinator(environment, new UpdateHelperLauncher(path), TimeProvider.System, log)
                .Run(command, ReadVersion(command.Manifest));
            // i18n-keep: 画面に出さない開発者向けの終了コードと診断。
            log.Error(new InvalidOperationException($"Update coordinator: {outcome.Result}, {outcome.Code}."));
            return outcome.Code;
        }
        catch (Exception exception)
        {
            log.Error(exception);
            if (!command.Elevated)
            {
                try
                {
                    UpdateResultFile.Write(UpdateEnvironment.ResultPath,
                        new("failed", (int)UpdateInstallResult.IoError, DateTimeOffset.UtcNow, null));
                }
                catch (Exception writeException) { log.Error(writeException); }
            }
            return (int)UpdateInstallResult.IoError;
        }
    }

    internal static UpdateInstallResult RunWithInstallLock(string installDir, IUpdateInstallLock installLock,
        Func<UpdateInstallResult> run)
    {
        // 作業フォルダーの作成と journal の列挙も、取得後に同じスレッドで実行する。
        using var ownership = installLock.TryAcquire(installDir);
        return ownership is null ? UpdateInstallResult.AnotherUpdateRunning : run();
    }

    internal static bool IsAllowedSid(UpdateCommandLine command, string currentSid)
    {
        var matches = string.Equals(command.Sid, currentSid, StringComparison.Ordinal);
        return matches || command.Elevated;
    }

    internal static UpdateInstallRequest CreateRequest(UpdateCommandLine command, string ownPath, string work,
        bool previousUpdateUnresolved)
    {
        var version = typeof(UpdateModes).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!ReleaseVersion.TryParse(version, out var current))
        {
            // i18n-keep: 画面に出さない開発者向けの例外。
            throw new InvalidOperationException("The installed version is invalid.");
        }
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => UpdateOsArchitecture.X64,
            Architecture.Arm64 => UpdateOsArchitecture.Arm64,
            _ => UpdateOsArchitecture.Other,
        };
        return new(Path.GetDirectoryName(ownPath)!, work, command.Manifest, command.Signature, command.Zip,
            UpdatePublicKey.Current ?? "", current, architecture, TimeSpan.FromSeconds(45), previousUpdateUnresolved);
    }

    internal static bool HasPreviousUpdateUnresolved(string currentWork, IEnumerable<string> journalPaths)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentWork));
        return journalPaths.Any(journal => !string.Equals(current,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(journal)!)),
            StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FindPreviousJournals()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.LocalApplicationData })
        {
            var root = Path.Combine(Environment.GetFolderPath(folder), "Snapwell", "update");
            if (!Directory.Exists(root)) { continue; }
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var journal = Path.Combine(directory, "journal.jsonl");
                if (File.Exists(journal)) { yield return journal; }
            }
        }
    }

    private static string? ReadVersion(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > UpdateManifestParser.MaximumBytes) { return null; }
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return UpdateManifestParser.Parse(bytes).Manifest?.Version.ToString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
