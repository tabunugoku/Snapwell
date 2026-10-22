using System.ComponentModel;
using System.Diagnostics;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Platform.Startup;

namespace Snapwell.App.Tests.Tray;

public sealed class SettingsLauncherTests
{
    /// <summary>終了を待たず、セットアップ引数をシェルに解釈させずに渡す。</summary>
    [Fact]
    public void SetupArgumentIsPassedAsOneArgumentWithoutWaitingForExit()
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new SettingsLauncher(new RecordingLog(), exists: _ => true, start: starts.Add,
            userProcess: new FakeUserProcess());
        launcher.Open("--setup");
        Assert.Equal(new[] { "--setup" }, Assert.Single(starts).ArgumentList);
        Assert.False(starts[0].UseShellExecute);
    }

    [Fact]
    public void ExistingExecutableStartsOnceInItsOwnFolderWithoutShellOrMessage()
    {
        const string DirectoryPath = @"C:\Snapwell folder";
        var starts = new List<ProcessStartInfo>();
        var messages = new List<string>();
        var log = new RecordingLog();
        var launcher = new SettingsLauncher(log, DirectoryPath, path =>
        {
            Assert.Equal(Path.Combine(DirectoryPath, "Snapwell.Settings.exe"), path);
            return true;
        }, starts.Add, messages.Add, new FakeUserProcess());
        launcher.Open();
        var start = Assert.Single(starts);
        Assert.Equal(Path.Combine(DirectoryPath, "Snapwell.Settings.exe"), start.FileName);
        Assert.Equal(DirectoryPath, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.Empty(messages);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void MissingExecutableShowsOneMessageAndNeverStarts()
    {
        var messages = new List<string>();
        var starts = 0;
        var launcher = new SettingsLauncher(new RecordingLog(), exists: _ => false,
            start: _ => starts++, show: messages.Add, userProcess: new FakeUserProcess());
        launcher.Open();
        Assert.Equal(0, starts);
        Assert.Contains("Snapwell.Settings.exe が必要です", Assert.Single(messages));
    }

    [Fact]
    public void StartupExceptionIsLoggedAndShownOnceWithoutEscaping()
    {
        var log = new RecordingLog();
        var messages = new List<string>();
        var exception = new Win32Exception("start");
        var launcher = new SettingsLauncher(log, exists: _ => true,
            start: _ => throw exception, show: messages.Add, userProcess: new FakeUserProcess());
        launcher.Open();
        Assert.Same(exception, Assert.Single(log.Errors));
        Assert.Equal("設定画面を起動できませんでした。", Assert.Single(messages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElevatedHostUsesOnlyUnelevatedRouteAndPreservesSetupArgument(bool elevated)
    {
        var direct = new List<ProcessStartInfo>();
        var process = new FakeUserProcess { IsElevated = elevated };
        var launcher = new SettingsLauncher(new RecordingLog(), exists: _ => true, start: direct.Add,
            show: _ => throw new Exception(), userProcess: process);
        launcher.Open("--setup");
        Assert.Equal(elevated ? 0 : 1, direct.Count);
        Assert.Equal(elevated ? 1 : 0, process.Starts.Count);
        var info = Assert.Single(elevated ? process.Starts : direct);
        Assert.Equal(new[] { "--setup" }, info.ArgumentList);
    }

    [Fact]
    public void UnelevatedLaunchFailureNeverFallsBackToElevatedDirectStart()
    {
        var process = new FakeUserProcess { IsElevated = true, Fail = true };
        var messages = new List<string>();
        var launcher = new SettingsLauncher(new RecordingLog(), exists: _ => true,
            start: _ => throw new Exception("Unexpected direct launch"), show: messages.Add, userProcess: process);
        launcher.Open();
        Assert.Equal("設定画面を起動できませんでした。", Assert.Single(messages));
    }

    private sealed class FakeUserProcess : IUserProcessLauncher
    {
        public bool IsElevated { get; init; }
        internal bool Fail;
        internal List<ProcessStartInfo> Starts { get; } = [];
        public void StartUnelevated(ProcessStartInfo info)
        {
            if (Fail)
            {
                throw new Win32Exception();
            }

            Starts.Add(info);
        }
    }

    private sealed class RecordingLog : IAppLog
    {
        internal List<Exception> Errors { get; } = [];
        public void Error(Exception exception) => Errors.Add(exception);
    }
}
