using System.ComponentModel;
using System.Diagnostics;
using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Updates;

/// <summary>配置先の現行イメージに終了を要求し、応答しない常駐だけを強制終了する。</summary>
public sealed class UpdateResidentStopper(UpdateSignalNames names) : IUpdateResidentStopper
{
    /// <summary>期限の前半を正常終了に使い、残る常駐を強制終了して終了を確認する。</summary>
    public bool StopResidents(string installDir, TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        var residents = new List<Process>();
        try
        {
            UpdateSignalNames.TrySignal(names.Quit);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)) + Path.DirectorySeparatorChar;
            // OS の保護プロセスまで対象にせず、配置した実行ファイルと同名の候補をパスで確認する。
            var processNames = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var identified = FindResidents(root, processNames, residents);
            var gracefulTimeout = timeout / 2;
            while (residents.Any(process => !process.HasExited)
                && Stopwatch.GetElapsedTime(start) < gracefulTimeout)
            {
                // 新版が Quit を作成する前に最初の通知が来た場合も、期限内は再通知する。
                UpdateSignalNames.TrySignal(names.Quit);
                var remaining = gracefulTimeout - Stopwatch.GetElapsedTime(start);
                if (remaining > TimeSpan.Zero)
                {
                    Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
                }
            }
            foreach (var process in residents)
            {
                try
                {
                    if (!process.HasExited) { process.Kill(); }
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    if (!process.HasExited) { identified = false; }
                }
            }
            foreach (var process in residents)
            {
                var remaining = timeout - Stopwatch.GetElapsedTime(start);
                if (!process.WaitForExit((int)Math.Max(0, remaining.TotalMilliseconds))) { identified = false; }
            }
            // 待機中に別の常駐が起動していた場合も、停止済みと誤認しない。
            var remainingResidents = new List<Process>();
            try
            {
                return FindResidents(root, processNames, remainingResidents)
                    && identified && remainingResidents.Count == 0;
            }
            finally
            {
                foreach (var process in remainingResidents) { process.Dispose(); }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            foreach (var process in residents) { process.Dispose(); }
        }
    }

    private static bool FindResidents(string root, string[] processNames, List<Process> residents)
    {
        var identified = true;
        foreach (var name in processNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                var retained = false;
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) { continue; }
                    var path = GetImagePath(process);
                    if (string.IsNullOrEmpty(path)) { identified = false; continue; }
                    if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) { continue; }
                    // ヘルパーと進行役の実行中イメージは Apply で .old に変わるため、停止対象から除く。
                    if (path.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) { continue; }
                    residents.Add(process);
                    retained = true;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    // 終了済みでなければ、パスを読めない候補が配置先の常駐である可能性を残す。
                    if (!process.HasExited) { identified = false; }
                }
                finally
                {
                    if (!retained) { process.Dispose(); }
                }
            }
        }
        return identified;
    }

    private static string? GetImagePath(Process process)
    {
        // MainModule の列挙権限や対象プロセスのアーキテクチャに依存しない問い合わせを使う。
        var api = new Win32ProcessNameApi();
        var handle = api.Open((uint)process.Id);
        if (handle == 0) { return null; }
        try { return api.GetImagePath(handle); }
        finally { api.Close(handle); }
    }
}
