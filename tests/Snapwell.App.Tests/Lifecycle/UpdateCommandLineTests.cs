using Snapwell.App.Lifecycle;
using Snapwell.Core.Updates;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class UpdateCommandLineTests
{
    private static string[] Valid(string mode = "--apply-update") =>
        [mode, "--for-sid", "S-1-5-21-1", "--manifest", @"C:\update folder\latest.json", "--sig",
            @"C:\update folder\latest.json.sig", "--zip", @"C:\update folder\package.zip"];

    [Theory]
    [InlineData("--apply-update", false)]
    [InlineData("--apply-update-elevated", true)]
    public void ParsesOnlyDedicatedModes(string mode, bool elevated)
    {
        Assert.True(UpdateCommandLine.TryParse(Valid(mode), out var command));
        Assert.Equal(elevated, command!.Elevated);
        Assert.Null(command.WaitPid);
        Assert.Contains("--manifest \"C:\\update folder\\latest.json\"", command.HelperArguments);
        Assert.True(UpdateCommandLine.TryParse([.. Valid(mode), "--wait-pid", "123"], out var withPid));
        Assert.Equal(123, withPid!.WaitPid);
    }

    [Fact]
    public void RejectsUnknownDuplicateMissingAndInjectedArguments()
    {
        Assert.False(UpdateCommandLine.TryParse([], out _));
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--unknown", "x"], out _));
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--for-sid", "S-1-5-21-1"], out _));
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--wait-pid", "-1"], out _));
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--wait-pid", "2147483648"], out _));
        Assert.False(UpdateCommandLine.TryParse(Valid()[..^1], out _));
        foreach (var (index, value) in new[] { (0, "--other"), (2, "S-1-5-bad"), (4, ""),
            (6, "\" --zip injected"), (8, "--manifest"), (8, "a\0b") })
        {
            var args = Valid();
            args[index] = value;
            Assert.False(UpdateCommandLine.TryParse(args, out _));
        }
    }

    /// <summary>ローカルで決めた検証条件と、前回の未解決状態を要求へ渡す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HelperRequestDerivesInstallDirectoryVersionKeyAndTimeoutLocally(bool unresolved)
    {
        Assert.True(UpdateCommandLine.TryParse(Valid("--apply-update-elevated"), out var command));
        var request = UpdateModes.CreateRequest(command!, @"C:\Program Files\Snapwell\Snapwell.exe", @"C:\work", unresolved);
        Assert.Equal(@"C:\Program Files\Snapwell", request.InstallDir);
        Assert.Equal(UpdatePublicKey.Current ?? "", request.PublicKeySpkiBase64);
        Assert.Equal(TimeSpan.FromSeconds(45), request.ReadyTimeout);
        Assert.Equal(unresolved, request.PreviousUpdateUnresolved);
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--current-version", "0.0.0"], out _));
        Assert.False(UpdateCommandLine.TryParse([.. Valid(), "--install-dir", @"C:\other"], out _));
    }

    [Fact]
    public void OnlyHelperAllowsDifferentAdministratorSid()
    {
        Assert.True(UpdateCommandLine.TryParse(Valid(), out var coordinator));
        Assert.True(UpdateModes.IsAllowedSid(coordinator!, "S-1-5-21-1"));
        Assert.False(UpdateModes.IsAllowedSid(coordinator!, "S-1-5-21-2"));
        Assert.True(UpdateCommandLine.TryParse(Valid("--apply-update-elevated"), out var helper));
        Assert.True(UpdateModes.IsAllowedSid(helper!, "S-1-5-21-2"));
    }

    [Fact]
    public void MalformedDedicatedModeReturnsBeforeAnyNormalStartupOrLog()
    {
        var log = new NoLog();
        Assert.Equal(1, UpdateModes.Execute([.. Valid(), "--unexpected"], log));
        Assert.Equal(1, UpdateModes.Execute(["--apply-update-elevated"], log));
    }

    private sealed class NoLog : IAppLog
    {
        public void Error(Exception exception) => Assert.Fail("An invalid command must have no side effects.");
    }
}
