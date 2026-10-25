using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

public sealed class UpdateSignalsTests
{
    [Fact]
    public void NamesAreDeterministicAndIsolated()
    {
        var first = new UpdateSignalNames("S-1-5-21-1");
        Assert.Equal(first.Quit, new UpdateSignalNames("S-1-5-21-1").Quit);
        Assert.NotEqual(first.Quit, new UpdateSignalNames("S-1-5-21-2").Quit);
        Assert.StartsWith(@"Local\Snapwell.Update.Quit.", first.Quit);
        Assert.Equal(64, first.Quit.Split('.')[^1].Length);
        Assert.NotEqual(first.Applied, first.Ready);
    }

    [Fact]
    public void HelperOpensEventsAndSignalsInBothDirections()
    {
        var names = new UpdateSignalNames("S-1-5-21-1", @"Local\Snapwell.Test." + Guid.NewGuid().ToString("N"));
        using var applied = UpdateSignalNames.Create(names.Applied);
        using var ready = UpdateSignalNames.Create(names.Ready);
        using var signals = new UpdateSignals(names);
        Assert.False(signals.WaitReady(TimeSpan.FromMilliseconds(1)));
        signals.SignalApplied();
        Assert.True(applied.WaitOne(0));
        Assert.True(UpdateSignalNames.TrySignal(names.Ready));
        Assert.True(signals.WaitReady(TimeSpan.FromSeconds(1)));
        Assert.Throws<IOException>(() => UpdateSignalNames.Create(names.Ready));
    }

    [Fact]
    public void MissingReadyTimesOutWithoutCreatingEvent()
    {
        var names = new UpdateSignalNames("S-1-5-21-1", @"Local\Snapwell.Test." + Guid.NewGuid().ToString("N"));
        using var signals = new UpdateSignals(names);
        Assert.False(signals.WaitReady(TimeSpan.FromMilliseconds(30)));
        Assert.False(UpdateSignalNames.TrySignal(names.Ready));
    }

    [Fact]
    public void ReadySurvivesCoordinatorExitAfterApplied()
    {
        var names = new UpdateSignalNames("S-1-5-21-1", @"Local\Snapwell.Test." + Guid.NewGuid().ToString("N"));
        using var applied = UpdateSignalNames.Create(names.Applied);
        var ready = UpdateSignalNames.Create(names.Ready);
        using var signals = new UpdateSignals(names);
        signals.SignalApplied();
        ready.Set();
        ready.Dispose();
        Assert.True(signals.WaitReady(TimeSpan.FromSeconds(1)));
    }
}
