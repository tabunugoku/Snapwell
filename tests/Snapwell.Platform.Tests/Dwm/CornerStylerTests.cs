using Snapwell.Platform.Dwm;

namespace Snapwell.Platform.Tests.Dwm;

public sealed class CornerStylerTests
{
    [Theory]
    [InlineData(11u, "OriginalClass")]
    [InlineData(10u, "ReplacementClass")]
    public void SameProcessReusedHandleWithDifferentWindowIdentityIsDiscarded(uint threadId, string className)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-corner-identity-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "corners.json");
            var api = new FakeCornerApi();
            new CornerStyler(api, path).Apply(1, CornerStyle.Round);
            api.ThreadId = threadId;
            api.ClassName = className;
            var messages = new List<string>();
            var restarted = new CornerStyler(api, path, messages.Add);
            restarted.RestoreAll();
            Assert.Equal(CornerStyle.Round, api.Value);
            Assert.Equal(1, api.SetCount);
            Assert.Equal(0, restarted.TrackedCount);
            Assert.Equal(0, new CornerStyler(api, path).TrackedCount);
            Assert.Single(messages);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacyBackupWithoutWindowIdentityIsReadAndDiscardedWithoutRestoring()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-corner-legacy-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "corners.json");
            File.WriteAllText(path, """
                [{"Handle":1,"ProcessId":123,"ProcessStartTicks":1,"Original":1}]
                """);
            var api = new FakeCornerApi();
            var messages = new List<string>();
            var restarted = new CornerStyler(api, path, messages.Add);
            restarted.RestoreAll();
            Assert.Equal(0, api.SetCount);
            Assert.Equal(0, new CornerStyler(api, path).TrackedCount);
            Assert.Single(messages);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BackupWriteFailureNeverChangesWindowEvenOnRetry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-corner-save-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var blocked = Path.Combine(directory, "file");
        File.WriteAllText(blocked, "blocks directory creation");
        try
        {
            var api = new FakeCornerApi();
            var styler = new CornerStyler(api, Path.Combine(blocked, "corners.json"));
            Assert.Throws<IOException>(() => styler.Apply(1, CornerStyle.Round));
            Assert.Equal(0, styler.TrackedCount);
            Assert.Throws<IOException>(() => styler.Apply(1, CornerStyle.DoNotRound));
            Assert.Equal(CornerStyle.RoundSmall, api.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(CornerStyle.Round)]
    [InlineData(CornerStyle.RoundSmall)]
    [InlineData(CornerStyle.DoNotRound)]
    public void ApplyingPreferenceThenDefaultRestoresOriginalValue(CornerStyle preference)
    {
        var api = new FakeCornerApi();
        var styler = new CornerStyler(api);
        Assert.True(styler.Apply(1, preference));
        Assert.Equal(preference, api.Value);
        Assert.True(styler.Apply(1, CornerStyle.Default));
        Assert.Equal(CornerStyle.RoundSmall, api.Value);
        Assert.Equal(0, styler.TrackedCount);
    }

    [Fact]
    public void FailedSetOrRestoreKeepsBackupAndLogsFailure()
    {
        var api = new FakeCornerApi();
        var messages = new List<string>();
        var styler = new CornerStyler(api, log: messages.Add);
        styler.Apply(1, CornerStyle.Round);
        api.Success = false;
        Assert.False(styler.Apply(1, CornerStyle.DoNotRound));
        styler.RestoreAll();
        Assert.Equal(1, styler.TrackedCount);
        Assert.Equal(2, messages.Count);
        api.Success = true;
        styler.RestoreAll();
        Assert.Equal(CornerStyle.RoundSmall, api.Value);
        Assert.Equal(0, styler.TrackedCount);
    }

    [Fact]
    public void ReopeningStateAfterCrashRestoresOriginalBeforeNextApply()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-corners-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "corners.json");
            var api = new FakeCornerApi();
            new CornerStyler(api, path).Apply(1, CornerStyle.Round);
            var restarted = new CornerStyler(api, path);
            Assert.Equal(1, restarted.TrackedCount);
            restarted.RestoreAll();
            Assert.Equal(CornerStyle.RoundSmall, api.Value);
            Assert.Equal(0, new CornerStyler(api, path).TrackedCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClosedOrReusedHandleIsNeverRestoredIntoAnotherProcess()
    {
        var api = new FakeCornerApi();
        var styler = new CornerStyler(api);
        styler.Apply(1, CornerStyle.Round);
        api.Generation++;
        styler.RestoreAll();
        Assert.Equal(CornerStyle.Round, api.Value);
        Assert.Equal(0, styler.TrackedCount);
        styler.Apply(1, CornerStyle.DoNotRound);
        api.Exists = false;
        styler.RestoreAll();
        Assert.Equal(0, styler.TrackedCount);
    }

    [Fact]
    public void RapidPreferenceChangesKeepFirstOriginalAndLastSelection()
    {
        var api = new FakeCornerApi();
        var styler = new CornerStyler(api);
        styler.Apply(1, CornerStyle.Round);
        styler.Apply(1, CornerStyle.DoNotRound);
        Assert.Equal(CornerStyle.DoNotRound, api.Value);
        styler.RestoreAll();
        Assert.Equal(CornerStyle.RoundSmall, api.Value);
    }

    private sealed class FakeCornerApi : ICornerApi
    {
        internal bool Success = true;
        internal bool Exists = true;
        internal long Generation = 1;
        internal uint ThreadId = 10;
        internal string ClassName = "OriginalClass";
        internal int SetCount;
        internal CornerStyle Value = CornerStyle.RoundSmall;
        public CornerState? Read(nint handle) => Exists
            ? new((long)handle, 123, Generation, Value, ThreadId, ClassName) : null;
        public bool IsWindow(nint handle) => Exists;
        public bool TrySet(nint handle, CornerStyle style)
        {
            SetCount++;
            if (Success)
            {
                Value = style;
            }

            return Success;
        }
    }
}
