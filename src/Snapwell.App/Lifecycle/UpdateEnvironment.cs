using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Snapwell.Core.Settings;
using Snapwell.Platform.Startup;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>進行役の待機と通常権限での本体起動を OS へ接続する。</summary>
internal sealed class UpdateEnvironment(string sid, string executablePath, IAdminTaskService tasks)
    : IUpdateCoordinatorEnvironment
{
    private readonly UpdateSignalNames _names = new(sid);

    public bool IsProtected => ExecutableLocation.IsProtected(executablePath);

    public bool QuitAndWait(TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        do
        {
            UpdateSignalNames.TrySignal(_names.Quit);
            // 所有権ではなく、常駐が保持する名前付きオブジェクトの消滅を待つ。
            using var instance = SingleInstance.TryAcquireNamed(@"Local\Snapwell." + sid);
            if (instance is not null) { return true; }
            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        } while (Stopwatch.GetElapsedTime(start) < timeout);
        return false;
    }

    public bool WaitProcess(int? pid, TimeSpan timeout)
    {
        if (pid is null) { return true; }
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException) { return true; }
    }

    public IUpdateCoordinatorSignals CreateSignals() => new CoordinatorSignals(_names);

    public void StartResident() => UpdateResidentLauncher.Start(executablePath, tasks);

    public void WriteResult(UpdateLastResult result) => UpdateResultFile.Write(ResultPath, result);

    internal static string ResultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snapwell", "update", "last-result.json");

    private sealed class CoordinatorSignals : IUpdateCoordinatorSignals
    {
        private readonly EventWaitHandle _applied;
        private readonly EventWaitHandle _ready;

        internal CoordinatorSignals(UpdateSignalNames names)
        {
            _applied = UpdateSignalNames.Create(names.Applied);
            try { _ready = UpdateSignalNames.Create(names.Ready); }
            catch { _applied.Dispose(); throw; }
        }
        public bool WaitApplied(TimeSpan timeout) => _applied.WaitOne(timeout);
        public bool WaitReady(TimeSpan timeout) => _ready.WaitOne(timeout);
        public void Dispose() { _applied.Dispose(); _ready.Dispose(); }
    }
}

/// <summary>管理者起動の設定を登録済みタスクの有無から選ぶ。</summary>
internal static class UpdateResidentLauncher
{
    internal static void Start(string path, IAdminTaskService tasks, Action<string>? normalStart = null)
    {
        // 別の配置先を指す古いタスクを実行すると、旧配置の起動を更新の成功と誤認する。
        if (tasks.ReadCommand() is { } command && string.Equals(command, Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                tasks.Run();
                return;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or COMException
                or InvalidOperationException)
            {
                // 登録済みタスクを実行する権限が無い場合 (既存の登録は読み取りのみ) は、通常権限で起動する。
                // 起動確認が来ないと新版が巻き戻されるため、起動しないよりよい。
            }
        }
        if (normalStart is not null) { normalStart(path); }
        else { Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })?.Dispose(); }
    }
}

/// <summary>結果を同じフォルダーの一時ファイルから原子的に置き換える。</summary>
internal static class UpdateResultFile
{
    internal static void Write(string path, UpdateLastResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var json = new Utf8JsonWriter(file);
                json.WriteStartObject();
                json.WriteString("result", result.Result);
                json.WriteNumber("code", result.Code);
                json.WriteString("utc", result.Utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                json.WriteString("version", result.Version);
                json.WriteEndObject();
                json.Flush();
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
