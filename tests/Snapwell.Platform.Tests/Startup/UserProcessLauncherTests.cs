using System.Diagnostics;
using Snapwell.Platform.Startup;

namespace Snapwell.Platform.Tests.Startup;

public sealed class UserProcessLauncherTests
{
    [Fact]
    public void TokenLaunchCommandLinePreservesSetupAndQuotesSpacesEmptyAndTrailingSlashes()
    {
        var info = new ProcessStartInfo(@"C:\Program Files\Snapwell.Settings.exe");
        info.ArgumentList.Add("--setup");
        info.ArgumentList.Add("a b");
        info.ArgumentList.Add("");
        info.ArgumentList.Add("a\"b");
        info.ArgumentList.Add(@"C:\folder\");
        Assert.Equal("\"C:\\Program Files\\Snapwell.Settings.exe\" \"--setup\" \"a b\" \"\" \"a\\\"b\" \"C:\\folder\\\\\"\0",
            UserProcessLauncher.BuildCommandLine(info));
    }
}
