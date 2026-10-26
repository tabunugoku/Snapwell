using Snapwell.App.Lifecycle;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>設定画面と進行役の引数の互換性を検証する。</summary>
public sealed class UpdateApplyArgumentsTests
{
    /// <summary>設定画面が組み立てた引数を進行役が受理し、空白のあるパスも保つ。</summary>
    [Fact]
    public void AcceptsSettingsArgumentList()
    {
        var info = UpdateApplyArguments.Create(@"C:\app folder\Snapwell.exe", "S-1-5-21-1",
            @"C:\update folder\manifest.json", @"C:\update folder\manifest.sig",
            @"C:\update folder\package.zip", 123);
        Assert.True(UpdateCommandLine.TryParse(info.ArgumentList.ToArray(), out var command));
        Assert.False(command!.Elevated);
        Assert.Equal("S-1-5-21-1", command.Sid);
        Assert.Equal(@"C:\update folder\manifest.json", command.Manifest);
        Assert.Equal(@"C:\update folder\manifest.sig", command.Signature);
        Assert.Equal(@"C:\update folder\package.zip", command.Zip);
        Assert.Equal(123, command.WaitPid);
    }
}
