using System.Xml.Linq;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class AdminStartupTests
{
    private const string Executable = @"C:\Program Files\Snap & Well\Snapwell.exe";
    private const string Sid = "S-1-5-21-123-456-789-1001";

    [Fact]
    public void XmlPreservesPathsAndEscapesValuesAndDefinesSafeLogonSettings()
    {
        var xml = AdminTaskXml.Create(Executable, Sid);
        var task = XElement.Parse(xml);
        XNamespace ns = AdminTaskXml.Namespace;
        string? Value(string parent, string name) => (string?)task.Element(ns + parent)?.Element(ns + name);
        var principal = task.Element(ns + "Principals")!.Element(ns + "Principal")!;
        Assert.Equal(Sid, (string?)principal.Element(ns + "UserId"));
        Assert.Equal("InteractiveToken", (string?)principal.Element(ns + "LogonType"));
        Assert.Equal("HighestAvailable", (string?)principal.Element(ns + "RunLevel"));
        Assert.Equal(Sid, (string?)task.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "UserId"));
        Assert.Equal("false", Value("Settings", "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value("Settings", "StopIfGoingOnBatteries"));
        Assert.Equal("false", Value("Settings", "StartWhenAvailable"));
        Assert.Equal("false", Value("Settings", "Hidden"));
        Assert.Equal("true", Value("Settings", "AllowHardTerminate"));
        Assert.Equal("PT0S", Value("Settings", "ExecutionTimeLimit"));
        Assert.Equal("IgnoreNew", Value("Settings", "MultipleInstancesPolicy"));
        Assert.Equal(Executable, AdminTaskXml.ReadCommand(xml));
        var exec = task.Element(ns + "Actions")!.Element(ns + "Exec")!;
        Assert.Equal(@"C:\Program Files\Snap & Well", (string?)exec.Element(ns + "WorkingDirectory"));
        Assert.Null(exec.Element(ns + "Arguments"));
        Assert.Contains("Snap &amp; Well", xml);
    }

    [Fact]
    public void TaskWithArgumentsOrMultipleActionsIsElsewhere()
    {
        XNamespace ns = AdminTaskXml.Namespace;
        var xml = XElement.Parse(AdminTaskXml.Create(Executable, Sid));
        var exec = xml.Element(ns + "Actions")!.Element(ns + "Exec")!;
        exec.Add(new XElement(ns + "Arguments", "--different-mode"));
        Assert.Equal("", AdminTaskXml.ReadCommand(xml.ToString()));
        exec.Element(ns + "Arguments")!.Remove();
        xml.Element(ns + "Actions")!.Add(new XElement(exec));
        Assert.Equal("", AdminTaskXml.ReadCommand(xml.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"C:\\Program Files\\Snap & Well\\Snapwell.exe\"")]
    [InlineData("\"C:\\Other\\Snapwell.exe\"")]
    public async Task AdminRoundTripRestoresOnlyOriginalRunValueAcrossPageReopening(string? original)
    {
        var fixture = new Fixture { Run = original };
        Assert.Equal(StartupChangeResult.Success, await fixture.Controller.SetAdminAsync(true));
        Assert.Null(fixture.Run);
        Assert.Equal((StartupMode.Admin, AutoStartState.On), fixture.Controller.Read());
        Assert.Equal(StartupChangeResult.Success, await fixture.Controller.SetAdminAsync(false));
        Assert.Null(fixture.Command);
        Assert.Equal(original, fixture.Run);
        Assert.Null(fixture.Saved);
    }

    [Fact]
    public async Task AdminWinsOverRunWithoutWritingOnReadAndAutoStartOffRemovesBoth()
    {
        var fixture = new Fixture { Command = @"C:\Other\Snapwell.exe", Run = "old", Saved = "original" };
        Assert.Equal((StartupMode.Admin, AutoStartState.Elsewhere), fixture.Controller.Read());
        Assert.Equal("old", fixture.Run);
        Assert.Equal(0, fixture.Elevations);
        await fixture.Controller.SetAutoStartAsync(true);
        Assert.Equal(0, fixture.Elevations);
        await fixture.Controller.SetAutoStartAsync(false);
        Assert.Null(fixture.Run);
        Assert.Null(fixture.Command);
        Assert.Null(fixture.Saved);
    }

    [Fact]
    public async Task NormalModeKeepsExistingRunBehavior()
    {
        var fixture = new Fixture { Run = "elsewhere" };
        Assert.Equal((StartupMode.Normal, AutoStartState.Elsewhere), fixture.Controller.Read());
        await fixture.Controller.SetAutoStartAsync(true);
        Assert.Equal($"\"{Executable}\"", fixture.Run);
        await fixture.Controller.SetAutoStartAsync(false);
        Assert.Equal((StartupMode.None, AutoStartState.Off), fixture.Controller.Read());
        Assert.Equal(0, fixture.Elevations);
    }

    [Theory]
    [InlineData(StartupChangeResult.Cancelled)]
    [InlineData(StartupChangeResult.Failed)]
    [InlineData(StartupChangeResult.SidMismatch)]
    [InlineData(StartupChangeResult.UnprotectedLocation)]
    public async Task UnsuccessfulElevationPreservesBothRegistrationsAndMemory(StartupChangeResult result)
    {
        var fixture = new Fixture { Run = "original", Saved = "previous", Result = result };
        Assert.Equal(result, await fixture.Controller.SetAdminAsync(true));
        Assert.Equal("original", fixture.Run);
        Assert.Null(fixture.Command);
        Assert.Equal("previous", fixture.Saved);
        fixture.Command = Executable;
        Assert.Equal(result, await fixture.Controller.SetAdminAsync(false));
        Assert.Equal(Executable, fixture.Command);
        Assert.Equal("original", fixture.Run);
        Assert.Equal("previous", fixture.Saved);
    }

    [Fact]
    public async Task RunRemainsRegisteredUntilElevationCompletes()
    {
        var completion = new TaskCompletionSource<StartupChangeResult>();
        var fixture = new Fixture { Run = "original", Completion = completion };
        var change = fixture.Controller.SetAdminAsync(true);
        Assert.False(change.IsCompleted);
        Assert.Equal("original", fixture.Run);
        Assert.Equal("original", fixture.Saved);
        completion.SetResult(StartupChangeResult.Success);
        Assert.Equal(StartupChangeResult.Success, await change);
        Assert.Null(fixture.Run);
    }

    [Fact]
    public async Task RunnerExceptionRestoresPreviousMemoryWithoutChangingRun()
    {
        var fixture = new Fixture { Run = "original", Saved = "previous", ThrowRunner = true };
        await Assert.ThrowsAsync<IOException>(() => fixture.Controller.SetAdminAsync(true));
        Assert.Equal("original", fixture.Run);
        Assert.Equal("previous", fixture.Saved);
    }

    [Fact]
    public async Task RunFailureKeepsRecoveryInformationAndAdminState()
    {
        var fixture = new Fixture { Run = "original", ThrowRunDelete = true };
        await Assert.ThrowsAsync<IOException>(() => fixture.Controller.SetAdminAsync(true));
        Assert.Equal("original", fixture.Saved);
        Assert.Equal("original", fixture.Run);
        Assert.Equal((StartupMode.Admin, AutoStartState.On), fixture.Controller.Read());
    }

    private sealed class Fixture : IRunKey, IAdminTaskService, IElevatedRunner, IStartupMemory
    {
        internal string? Run;
        internal string? Command;
        internal string? Saved;
        internal int Elevations;
        internal StartupChangeResult Result = StartupChangeResult.Success;
        internal bool ThrowRunner;
        internal bool ThrowRunDelete;
        internal TaskCompletionSource<StartupChangeResult>? Completion;
        internal StartupController Controller => new(this, this, this, this, Executable);
        public string? Read(string name) => Run;
        public void Write(string name, string value) => Run = value;
        public void Delete(string name)
        {
            if (ThrowRunDelete)
            {
                throw new IOException();
            }

            Run = null;
        }
        public string? ReadCommand() => Command;
        public void Register(string executablePath, string userSid) => Command = executablePath;
        public void Delete() => Command = null;
        public string? Read() => Saved;
        public void Write(string? value) => Saved = value;
        public async Task<StartupChangeResult> RunAsync(bool enabled)
        {
            Elevations++;
            if (ThrowRunner)
            {
                throw new IOException();
            }

            var result = Completion is null ? Result : await Completion.Task;
            if (result == StartupChangeResult.Success)
            {
                Command = enabled ? Executable : null;
            }

            return result;
        }
    }
}
