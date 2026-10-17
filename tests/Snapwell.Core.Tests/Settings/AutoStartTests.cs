using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class AutoStartTests
{
    private const string ExecutablePath = @"C:\Snapwell folder\Snapwell.exe";

    [Fact]
    public void MissingValueIsOffAndEnablingWritesOneQuotedPath()
    {
        var key = new FakeRunKey();
        var autoStart = new AutoStart(key, ExecutablePath);
        Assert.Equal(AutoStartState.Off, autoStart.GetState());
        autoStart.SetEnabled(true);
        Assert.Equal(("Snapwell", $"\"{ExecutablePath}\""), Assert.Single(key.Writes));
        Assert.Equal(AutoStartState.On, autoStart.GetState());
    }

    [Theory]
    [InlineData(@"  C:\SNAPWELL FOLDER\SNAPWELL.EXE  ", AutoStartState.On)]
    [InlineData("  \"C:\\Snapwell folder\\Snapwell.exe\"  ", AutoStartState.On)]
    [InlineData(@"C:\Other\Snapwell.exe", AutoStartState.Elsewhere)]
    [InlineData("", AutoStartState.Elsewhere)]
    [InlineData("\"C:\\Snapwell folder\\Snapwell.exe\" --x", AutoStartState.Elsewhere)]
    public void ComparisonIgnoresCaseQuotesAndWhitespaceButNotArguments(string value, AutoStartState expected)
    {
        var key = new FakeRunKey { Value = value };
        Assert.Equal(expected, new AutoStart(key, ExecutablePath).GetState());
    }

    [Fact]
    public void EnablingElsewhereReplacesPathAndDisablingDeletesOnlyExistingValue()
    {
        var key = new FakeRunKey { Value = @"C:\Other\Snapwell.exe" };
        var autoStart = new AutoStart(key, ExecutablePath);
        autoStart.SetEnabled(true);
        Assert.Equal($"\"{ExecutablePath}\"", key.Value);
        autoStart.SetEnabled(false);
        autoStart.SetEnabled(false);
        Assert.Equal("Snapwell", Assert.Single(key.Deletes));
        Assert.Equal(AutoStartState.Off, autoStart.GetState());
    }

    private sealed class FakeRunKey : IRunKey
    {
        internal string? Value;
        internal List<(string Name, string Value)> Writes { get; } = [];
        internal List<string> Deletes { get; } = [];
        public string? Read(string name) => Value;
        public void Write(string name, string value) { Writes.Add((name, value)); Value = value; }
        public void Delete(string name) { Deletes.Add(name); Value = null; }
    }
}
