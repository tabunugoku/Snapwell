using System.ComponentModel;
using System.Diagnostics;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class SettingsLauncherTests
{
    /// <summary>終了を待たず、セットアップ引数をシェルに解釈させずに渡す。</summary>
    [Fact]
    public void SetupArgumentIsPassedAsOneArgumentWithoutWaitingForExit()
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new SettingsLauncher(new RecordingLog(), exists: _ => true, start: starts.Add);
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
        }, starts.Add, messages.Add);
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
            start: _ => starts++, show: messages.Add);
        launcher.Open();
        Assert.Equal(0, starts);
        Assert.Contains("見つかりません", Assert.Single(messages));
    }

    [Fact]
    public void StartupExceptionIsLoggedAndShownOnceWithoutEscaping()
    {
        var log = new RecordingLog();
        var messages = new List<string>();
        var exception = new Win32Exception("start");
        var launcher = new SettingsLauncher(log, exists: _ => true,
            start: _ => throw exception, show: messages.Add);
        launcher.Open();
        Assert.Same(exception, Assert.Single(log.Errors));
        Assert.Equal("設定画面を起動できませんでした。", Assert.Single(messages));
    }

    private sealed class RecordingLog : IAppLog
    {
        internal List<Exception> Errors { get; } = [];
        public void Error(Exception exception) => Errors.Add(exception);
    }
}
