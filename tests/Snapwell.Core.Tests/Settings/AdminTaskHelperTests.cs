using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class AdminTaskHelperTests
{
    private const string Sid = "S-1-5-21-123-456-789-1001";
    private const string Executable = @"C:\Program Files\Snapwell\Snapwell.exe";

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
        public string? ReadCommand() => Command;
        public void Register(string executablePath, string userSid)
        {
            Calls++;
            if (Fail)
            {
                throw new IOException();
            }

            Command = executablePath;
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
