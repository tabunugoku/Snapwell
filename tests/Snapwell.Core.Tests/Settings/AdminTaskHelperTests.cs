using System.Xml.Linq;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class AdminTaskHelperTests
{
    private const string Sid = "S-1-5-21-123-456-789-1001";
    private const string Executable = @"C:\Program Files\Snapwell\Snapwell.exe";

    [Theory]
    [InlineData(@"C:\Program Files\Snapwell-%SW%\Snapwell.exe")]
    [InlineData(@"C:\Program Files\Snapwell\Snapwell-%SW%.exe")]
    public void ExpandedShortNameIsRejectedByXmlHelperAndSettingsPolicy(string expanded)
    {
        const string ShortPath = @"C:\PROGRA~1\SNAPWE~1\SNAPWE~1.EXE";
        var calls = 0;
        string Normalize(string path)
        {
            Assert.Equal(ShortPath, path);
            calls++;
            return expanded;
        }

        Assert.DoesNotContain('%', ShortPath);
        Assert.Throws<ArgumentException>(() => AdminTaskXml.Create(ShortPath, Sid, Normalize));
        Assert.Equal(1, calls);
        var service = new FakeTasks();
        Assert.Equal(4, AdminTaskHelper.Execute(["--set-admin-task", "on", "--for-sid", Sid], Sid,
            ShortPath, _ => throw new InvalidOperationException("ACL を読む前に拒否する。"), service, Normalize));
        Assert.Equal(2, calls);
        Assert.Equal(0, service.Calls);
        Assert.False(ProtectedLocation.IsLiteralTaskPath(ShortPath, Normalize));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void HelperSharesOneNormalizationWithLocationCheckAndTaskXml()
    {
        const string ShortPath = @"C:\PROGRA~1\SNAPWE~1\Snapwell.exe";
        var calls = 0;
        TaskExecutablePath? checkedPath = null;
        var service = new FakeTasks();
        Assert.Equal(0, AdminTaskHelper.Execute(["--set-admin-task", "on", "--for-sid", Sid], Sid,
            ShortPath, path =>
            {
                checkedPath = path;
                Assert.Equal(Executable, path.Value);
                return true;
            }, service, path =>
            {
                Assert.Equal(ShortPath, path);
                Assert.Equal(1, ++calls);
                return Executable;
            }));
        Assert.Equal(1, calls);
        Assert.Same(checkedPath, service.Path);
        Assert.Equal(Executable, service.Command);
        var xml = AdminTaskXml.Create(service.Path!, Sid);
        Assert.Equal(Executable, AdminTaskXml.ReadCommand(xml));
        XNamespace ns = AdminTaskXml.Namespace;
        Assert.Equal(@"C:\Program Files\Snapwell",
            (string?)XElement.Parse(xml).Descendants(ns + "WorkingDirectory").Single());
    }

    [Theory]
    [InlineData(@"C:\Program Files\%SW%\Snapwell.exe")]
    [InlineData(@"C:\Program Files\Snapwell\Snap%well.exe")]
    public void ExpandablePathIsRejectedByXmlHelperAndSettingsPolicy(string path)
    {
        var service = new FakeTasks();
        Assert.Throws<ArgumentException>(() => AdminTaskXml.Create(path, Sid));
        Assert.Equal(4, AdminTaskHelper.Execute(["--set-admin-task", "on", "--for-sid", Sid], Sid,
            path, _ => true, service));
        Assert.False(ProtectedLocation.IsLiteralTaskPath(path));
        Assert.Equal(0, service.Calls);
        Assert.True(ProtectedLocation.IsLiteralTaskPath(Executable));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void ParsesOnlyDedicatedArguments(string action, bool enabled)
    {
        Assert.True(AdminTaskHelper.TryParse(["--set-admin-task", action, "--for-sid", Sid], out var actual, out var sid));
        Assert.Equal(enabled, actual);
        Assert.Equal(Sid, sid);
        Assert.False(AdminTaskHelper.TryParse(["--set-admin-task", action], out _, out _));
        Assert.False(AdminTaskHelper.TryParse(["--set-admin-task", "invalid", "--for-sid", Sid], out _, out _));
        Assert.False(AdminTaskHelper.TryParse(["--set-admin-task", action, "--for-sid", "invalid"], out _, out _));
        Assert.False(AdminTaskHelper.TryParse(["--set-admin-task", action, "--for-sid", Sid, "other.exe"], out _, out _));
    }

    [Fact]
    public void ChecksSidAndLocationBeforeTouchingTasksAndAllowsRemovalFromUnprotectedLocation()
    {
        var service = new FakeTasks();
        string[] on = ["--set-admin-task", "on", "--for-sid", Sid];
        Assert.Equal(2, AdminTaskHelper.Execute(on, "other", Executable, _ => throw new Exception(), service));
        Assert.Equal(3, AdminTaskHelper.Execute(on, Sid, Executable, _ => false, service));
        Assert.Equal(1, AdminTaskHelper.Execute(["bad"], Sid, Executable, _ => true, service));
        Assert.Equal(0, service.Calls);
        Assert.Equal(0, AdminTaskHelper.Execute(on, Sid, Executable, _ => true, service));
        Assert.Equal(Executable, service.Command);
        Assert.Equal(Sid, service.UserSid);
        Assert.Equal(0, AdminTaskHelper.Execute(["--set-admin-task", "off", "--for-sid", Sid], Sid,
            Executable, _ => throw new Exception(), service));
        Assert.Null(service.Command);
        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public void TaskFailureReturnsFailureCode()
    {
        var service = new FakeTasks { Fail = true };
        Assert.Equal(1, AdminTaskHelper.Execute(["--set-admin-task", "on", "--for-sid", Sid], Sid,
            Executable, _ => true, service));
        Assert.Equal(1, AdminTaskHelper.Execute(["--set-admin-task", "off", "--for-sid", Sid], Sid,
            Executable, _ => true, service));
    }

    private sealed class FakeTasks : IAdminTaskService
    {
        internal bool Fail;
        internal int Calls;
        internal string? Command;
        internal string? UserSid;
        internal TaskExecutablePath? Path;
        public string? ReadCommand() => Command;
        public void Register(TaskExecutablePath executablePath, string userSid)
        {
            Calls++;
            if (Fail)
            {
                throw new IOException();
            }

            Command = executablePath.Value;
            Path = executablePath;
            UserSid = userSid;
        }

        public void Delete()
        {
            Calls++;
            if (Fail)
            {
                throw new IOException();
            }

            Command = null;
        }
    }
}
