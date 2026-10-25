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
        Assert.Equal(["signals", "quit", "pid:42", "helper:True", "applied", "start", "ready",
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
        internal Fixture()
        {
            Environment = new(Calls, Clock);
            Launcher = new(Calls);
        }
        internal UpdateLastResult Run() => new UpdateCoordinator(Environment, Launcher, Clock, new FakeLog())
            .Run(_command, "2.0.0");
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
        internal FakeSignals Signals { get; } = new(calls, clock);
        public bool IsProtected { get; set; } = true;
        public bool QuitAndWait(TimeSpan timeout)
        {
            Assert.Equal(TimeSpan.FromSeconds(30), timeout);
            calls.Add("quit");
            return Quit;
        }
        public bool WaitProcess(int? pid, TimeSpan timeout) { calls.Add("pid:" + pid); return ProcessExit; }
        public IUpdateCoordinatorSignals CreateSignals() { calls.Add("signals"); return Signals; }
        public void StartResident() { calls.Add("start"); StartAction?.Invoke(); }
        public void WriteResult(UpdateLastResult result) { Saved = result; calls.Add("write:" + result.Result); }
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
        internal FakeProcess Process { get; } = new(calls);
        public IUpdateHelperProcess Start(string arguments, bool elevate)
        {
            calls.Add("helper:" + elevate);
            Arguments = arguments;
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
        public void Error(Exception exception) { }
    }
}
