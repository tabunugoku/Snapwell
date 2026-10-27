using System.Text.Json;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Settings;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class UpdateCoordinatorTests
{
    private static readonly UpdateCommandLine _command = new(false, "S-1-5-21-1",
        @"C:\update folder\latest.json", @"C:\update folder\latest.json.sig", @"C:\update folder\package.zip", 42);

    [Fact]
    public void SuccessStartsNewVersionAndLeavesWithoutWaitingForHelper()
    {
        var fixture = new Fixture();
        var result = fixture.Run();
        Assert.Equal("installed", result.Result);
        Assert.Equal(0, result.Code);
        Assert.Equal(["signals", "quit", "pid:42", "helper:True", "applied", "cleanup", "start", "ready",
            "signals.dispose", "helper.dispose", "write:installed"], fixture.Calls);
        Assert.Equal(_command.HelperArguments, fixture.Launcher.Arguments);
        Assert.Equal("2.0.0", fixture.Environment.Saved!.Version);
    }

    [Fact]
    public void ResidentTimeoutDoesNotStartHelperOrRestartResident()
    {
        var fixture = new Fixture();
        fixture.Environment.Quit = false;
        Assert.Equal("failed", fixture.Run().Result);
        Assert.DoesNotContain("start", fixture.Calls);
        Assert.Null(fixture.Launcher.Arguments);
    }

    [Fact]
    public void OtherProcessTimeoutRestartsWithoutApplying()
    {
        var fixture = new Fixture();
        fixture.Environment.ProcessExit = false;
        Assert.Equal("failed", fixture.Run().Result);
        Assert.Contains("start", fixture.Calls);
        Assert.Null(fixture.Launcher.Arguments);
    }

    [Theory]
    [InlineData(1223, "cancelled")]
    [InlineData((int)UpdateInstallResult.SignatureInvalid, "failed")]
    public void HelperFailureRestartsOriginal(int code, string expected)
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = false;
        fixture.Launcher.Process.Code = code;
        Assert.Equal(expected, fixture.Run().Result);
        Assert.Equal(code, fixture.Environment.Saved!.Code);
        Assert.Equal(1, fixture.Calls.Count(call => call == "start"));
    }

    /// <summary>コード 31 は元の版を再起動し、last-result.json に failed と 31 を保存する。</summary>
    [Fact]
    public void AnotherUpdateRunningRestartsOriginalAndWritesFailedResult()
    {
        var directory = Path.Combine(Path.GetTempPath(), "snapwell-busy-result-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "last-result.json");
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = false;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.AnotherUpdateRunning;
        fixture.Environment.WriteAction = result => UpdateResultFile.Write(path, result);
        try
        {
            var result = fixture.Run();
            Assert.Equal("failed", result.Result);
            Assert.Equal(31, result.Code);
            Assert.Equal(result, fixture.Environment.Saved);
            Assert.Equal(1, fixture.Calls.Count(call => call == "start"));
            var calls = fixture.Calls.ToList();
            Assert.True(calls.IndexOf("helper.dispose") < calls.IndexOf("start"));
            Assert.True(calls.IndexOf("start") < calls.IndexOf("write:failed"));
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal("failed", json.RootElement.GetProperty("result").GetString());
            Assert.Equal(31, json.RootElement.GetProperty("code").GetInt32());
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Fact]
    public void ReadyTimeoutWaitsForRollbackBeforeRestarting()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Ready = false;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.ReadyTimeoutRolledBack;
        var result = fixture.Run();
        Assert.Equal("rolled-back", result.Result);
        Assert.Equal((int)UpdateInstallResult.ReadyTimeoutRolledBack, result.Code);
        Assert.Equal(2, fixture.Calls.Count(call => call == "start"));
        var calls = fixture.Calls.ToList();
        Assert.True(calls.IndexOf("helper.wait:120") < calls.LastIndexOf("start"));
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Environment.Signals.ReadyWait);
    }

    [Fact]
    public void IncompleteRollbackDoesNotStartPartiallyRestoredExecutable()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Ready = false;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.ReadyTimeoutRollbackIncomplete;
        Assert.Equal("failed", fixture.Run().Result);
        Assert.Equal(1, fixture.Calls.Count(call => call == "start"));
    }

    /// <summary>停止できない新版を残した結果 24 は、旧版を再起動せず失敗として保存する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunningResidentFailureIsSavedWithoutRecoveryRestart(bool applied)
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = applied;
        fixture.Environment.Signals.Ready = false;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.ReadyTimeoutResidentRunning;
        var result = fixture.Run();
        Assert.Equal("failed", result.Result);
        Assert.Equal(24, result.Code);
        Assert.Equal(result, fixture.Environment.Saved);
        Assert.Equal(applied ? 1 : 0, fixture.Calls.Count(call => call == "start"));
    }

    [Fact]
    public void UnprotectedLocationDoesNotElevate()
    {
        var fixture = new Fixture();
        fixture.Environment.IsProtected = false;
        Assert.Equal("installed", fixture.Run().Result);
        Assert.Contains("helper:False", fixture.Calls);
    }

    [Fact]
    public void AppliedTimeoutIncludesPendingUacAndDoesNotRestartWhileHelperIsPending()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = false;
        fixture.Launcher.Process.Exits = false;
        Assert.Equal("failed", fixture.Run().Result);
        Assert.DoesNotContain("start", fixture.Calls);
        Assert.True(fixture.Clock.GetUtcNow() - DateTimeOffset.UnixEpoch >= TimeSpan.FromSeconds(120));
        Assert.Contains("helper.wait:120", fixture.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestartUsesRegisteredTaskWhenPresent(bool registered)
    {
        var tasks = new FakeTasks { Command = registered ? @"C:\Snapwell.exe" : null };
        var normal = 0;
        UpdateResidentLauncher.Start(@"C:\Snapwell.exe", tasks, _ => normal++);
        Assert.Equal(registered ? 1 : 0, tasks.Runs);
        Assert.Equal(registered ? 0 : 1, normal);
    }

    [Fact]
    public void RestartIgnoresTaskRegisteredForAnotherLocation()
    {
        var tasks = new FakeTasks { Command = @"D:\Old\Snapwell.exe" };
        var normal = 0;
        UpdateResidentLauncher.Start(@"C:\Snapwell.exe", tasks, _ => normal++);
        Assert.Equal(0, tasks.Runs);
        Assert.Equal(1, normal);
    }

    [Fact]
    public void RestartFallsBackToNormalStartWhenTaskCannotRun()
    {
        var tasks = new FakeTasks { Command = @"C:\Snapwell.exe", RunException = new UnauthorizedAccessException() };
        var normal = 0;
        UpdateResidentLauncher.Start(@"C:\Snapwell.exe", tasks, _ => normal++);
        Assert.Equal(1, tasks.Runs);
        Assert.Equal(1, normal);
    }

    [Fact]
    public void CoordinatorStartsNewVersionThroughAdminTask()
    {
        var fixture = new Fixture();
        var tasks = new FakeTasks { Command = @"C:\Snapwell.exe" };
        fixture.Environment.StartAction = () => UpdateResidentLauncher.Start(@"C:\Snapwell.exe", tasks,
            _ => Assert.Fail("The registered task must be used."));
        Assert.Equal("installed", fixture.Run().Result);
        Assert.Equal(1, tasks.Runs);
    }

    [Fact]
    public void FailedRecoveryStartIsRecordedAsFailed()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = false;
        fixture.Launcher.Process.Code = 1223;
        fixture.Environment.StartAction = () => throw new IOException();
        Assert.Equal("failed", fixture.Run().Result);
        Assert.Equal((int)UpdateInstallResult.IoError, fixture.Environment.Saved!.Code);
    }

    /// <summary>セッションの開始前やヘルパーの起動中の例外でも pending を片付ける。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SessionExceptionCleansPending(bool beforeSignals)
    {
        var fixture = new Fixture();
        if (beforeSignals) { fixture.Environment.SignalsException = new IOException(); }
        else { fixture.Launcher.StartException = new IOException(); }
        Assert.Equal("failed", fixture.Run().Result);
    }

    /// <summary>新版を起動できなくても Ready を待ち、削除の試行を繰り返さない。</summary>
    [Fact]
    public void FailedNewResidentStartStillWaitsReadyAfterCleanup()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Ready = false;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.ReadyTimeoutRolledBack;
        fixture.Environment.StartAction = () => throw new IOException();
        Assert.Equal("failed", fixture.Run().Result);
        Assert.True(fixture.Calls.IndexOf("cleanup") < fixture.Calls.IndexOf("start"));
        Assert.True(fixture.Calls.IndexOf("start") < fixture.Calls.IndexOf("ready"));
    }

    /// <summary>Ready もヘルパー終了も時間切れになった場合も削除は一度だけ試みる。</summary>
    [Fact]
    public void ReadyAndHelperTimeoutCleansPendingOnce()
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Ready = false;
        fixture.Launcher.Process.Exits = false;
        Assert.Equal("failed", fixture.Run().Result);
    }

    /// <summary>削除に失敗しても成功・失敗の結果と起動確認の順序を変えない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupFailureOnlyLogs(bool applied)
    {
        var fixture = new Fixture();
        fixture.Environment.Signals.Applied = applied;
        fixture.Launcher.Process.Code = (int)UpdateInstallResult.SignatureInvalid;
        var exception = new IOException("Pending is locked.");
        fixture.Environment.CleanupAction = () => throw exception;
        var result = fixture.Run();
        Assert.Equal(applied ? "installed" : "failed", result.Result);
        Assert.Equal(applied ? 0 : (int)UpdateInstallResult.SignatureInvalid, result.Code);
        Assert.Same(exception, Assert.Single(fixture.Log.Errors));
    }

    /// <summary>実体の削除後も新版の起動と Ready 待機が進み、結果ファイルだけが残る。</summary>
    [Fact]
    public void RealCleanupLeavesOnlyLastResultBeforeNewResidentStarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapwell-pending-" + Guid.NewGuid().ToString("N"));
        var pending = Path.Combine(root, "Snapwell", "update", "pending");
        var resultPath = Path.Combine(root, "Snapwell", "update", "last-result.json");
        var command = CreatePending(pending);
        var environment = new UpdateEnvironment(command.Sid, @"C:\Snapwell.exe", new FakeTasks(), root);
        var fixture = new Fixture();
        fixture.Environment.CleanupAction = () => environment.CleanupPending(command with
        {
            Manifest = command.Manifest.ToUpperInvariant(),
            Signature = Path.Combine(pending, "nested", "..", "manifest.sig"),
        });
        // 製品側は StartResident の例外を握りつぶすため、アサートは呼び出しの外で行う。
        bool? pendingExistedAtStart = null;
        fixture.Environment.StartAction = () => pendingExistedAtStart = Directory.Exists(pending);
        fixture.Environment.WriteAction = result => UpdateResultFile.Write(resultPath, result);
        try
        {
            Assert.Equal("installed", fixture.Run().Result);
            Assert.False(pendingExistedAtStart);
            Assert.Equal([resultPath], Directory.GetFileSystemEntries(Path.GetDirectoryName(resultPath)!));
            environment.CleanupPending(command);
            Assert.True(File.Exists(resultPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>三つの親が既知の pending と一致しなければ、どちらのフォルダーも消さない。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RealCleanupPreservesUnexpectedParents(int outsideIndex)
    {
        var root = Path.Combine(Path.GetTempPath(), "snapwell-pending-" + Guid.NewGuid().ToString("N"));
        var pending = Path.Combine(root, "Snapwell", "update", "pending");
        var command = CreatePending(pending);
        var environment = new UpdateEnvironment(command.Sid, @"C:\Snapwell.exe", new FakeTasks(), root);
        try
        {
            foreach (var other in new[] { pending + "-other", Path.Combine(pending, "child"),
                Path.GetDirectoryName(pending)! })
            {
                var outside = CreatePending(other);
                environment.CleanupPending(command with
                {
                    Manifest = outsideIndex is -1 or 0 ? outside.Manifest : command.Manifest,
                    Signature = outsideIndex is -1 or 1 ? outside.Signature : command.Signature,
                    Zip = outsideIndex is -1 or 2 ? outside.Zip : command.Zip,
                });
                foreach (var path in new[] { command.Manifest, command.Signature, command.Zip,
                    outside.Manifest, outside.Signature, outside.Zip })
                {
                    Assert.True(File.Exists(path));
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static UpdateCommandLine CreatePending(string pending)
    {
        Directory.CreateDirectory(pending);
        var command = _command with
        {
            Manifest = Path.Combine(pending, "manifest.json"),
            Signature = Path.Combine(pending, "manifest.sig"),
            Zip = Path.Combine(pending, "package.zip"),
        };
        foreach (var path in new[] { command.Manifest, command.Signature, command.Zip })
        {
            File.WriteAllText(path, "pending");
        }
        return command;
    }

    [Fact]
    public void LastResultIsAtomicJsonWithUtcVersionAndNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), "snapwell-result-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "last-result.json");
        try
        {
            var utc = DateTimeOffset.Parse("2026-10-25T12:00:00+00:00",
                System.Globalization.CultureInfo.InvariantCulture);
            UpdateResultFile.Write(path, new("installed", 0, utc, "2.0.0"));
            using (var json = JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                Assert.Equal("installed", json.RootElement.GetProperty("result").GetString());
                Assert.Equal(0, json.RootElement.GetProperty("code").GetInt32());
                Assert.Equal(utc, json.RootElement.GetProperty("utc").GetDateTimeOffset());
                Assert.Equal("2.0.0", json.RootElement.GetProperty("version").GetString());
            }
            UpdateResultFile.Write(path, new("failed", 30, utc, null));
            using var second = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("version").ValueKind);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    private sealed class Fixture
    {
        internal List<string> Calls { get; } = [];
        internal FakeClock Clock { get; } = new();
        internal FakeEnvironment Environment { get; }
        internal FakeLauncher Launcher { get; }
        internal FakeLog Log { get; } = new();
        internal Fixture()
        {
            Environment = new(Calls, Clock);
            Launcher = new(Calls);
        }
        internal UpdateLastResult Run()
        {
            var result = new UpdateCoordinator(Environment, Launcher, Clock, Log).Run(_command, "2.0.0");
            Assert.Single(Calls, call => call == "cleanup");
            Assert.True(Calls.IndexOf("cleanup") < Calls.IndexOf("write:" + result.Result));
            return result;
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

    private sealed class FakeEnvironment(List<string> calls, FakeClock clock) : IUpdateCoordinatorEnvironment
    {
        internal bool Quit = true;
        internal bool ProcessExit = true;
        internal UpdateLastResult? Saved;
        internal Action? StartAction;
        internal Action<UpdateLastResult>? WriteAction;
        internal Action? CleanupAction;
        internal Exception? SignalsException;
        internal FakeSignals Signals { get; } = new(calls, clock);
        public bool IsProtected { get; set; } = true;
        public bool QuitAndWait(TimeSpan timeout)
        {
            Assert.Equal(TimeSpan.FromSeconds(30), timeout);
            calls.Add("quit");
            return Quit;
        }
        public bool WaitProcess(int? pid, TimeSpan timeout) { calls.Add("pid:" + pid); return ProcessExit; }
        public IUpdateCoordinatorSignals CreateSignals()
        {
            calls.Add("signals");
            if (SignalsException is not null) { throw SignalsException; }
            return Signals;
        }
        public void StartResident() { calls.Add("start"); StartAction?.Invoke(); }
        /// <summary>削除要求の引数と順序を記録し、実体の削除や失敗を注入する。</summary>
        public void CleanupPending(UpdateCommandLine command)
        {
            Assert.Same(_command, command);
            calls.Add("cleanup");
            CleanupAction?.Invoke();
        }
        public void WriteResult(UpdateLastResult result)
        {
            Saved = result;
            calls.Add("write:" + result.Result);
            WriteAction?.Invoke(result);
        }
    }

    private sealed class FakeSignals(List<string> calls, FakeClock clock) : IUpdateCoordinatorSignals
    {
        internal bool Applied = true;
        internal bool Ready = true;
        internal TimeSpan ReadyWait;
        public bool WaitApplied(TimeSpan timeout)
        {
            if (!Applied) { clock.Advance(timeout); }
            else { calls.Add("applied"); }
            return Applied;
        }
        public bool WaitReady(TimeSpan timeout)
        {
            calls.Add("ready");
            ReadyWait = timeout;
            if (!Ready) { clock.Advance(timeout); }
            return Ready;
        }
        public void Dispose() => calls.Add("signals.dispose");
    }

    private sealed class FakeLauncher(List<string> calls) : IUpdateHelperLauncher
    {
        internal string? Arguments;
        internal Exception? StartException;
        internal FakeProcess Process { get; } = new(calls);
        public IUpdateHelperProcess Start(string arguments, bool elevate)
        {
            calls.Add("helper:" + elevate);
            Arguments = arguments;
            if (StartException is not null) { throw StartException; }
            return Process;
        }
    }

    private sealed class FakeProcess(List<string> calls) : IUpdateHelperProcess
    {
        internal int? Code;
        internal bool Exits = true;
        public int? ExitCode => Code;
        public bool WaitForExit(TimeSpan timeout)
        {
            calls.Add("helper.wait:" + timeout.TotalSeconds);
            return Exits && Code is not null;
        }
        public void Dispose() => calls.Add("helper.dispose");
    }

    private sealed class FakeTasks : IAdminTaskService
    {
        internal string? Command;
        internal int Runs;
        public string? ReadCommand() => Command;
        internal Exception? RunException;
        public void Run()
        {
            Runs++;
            if (RunException is not null) { throw RunException; }
        }
        public void Register(TaskExecutablePath executablePath, string userSid) => throw new NotSupportedException();
        public void Delete() => throw new NotSupportedException();
    }

    private sealed class FakeLog : IAppLog
    {
        internal List<Exception> Errors { get; } = [];
        public void Error(Exception exception) => Errors.Add(exception);
    }
}
