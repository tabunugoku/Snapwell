using Snapwell.Core.Commands;

namespace Snapwell.App.Tests.Hotkeys;

public sealed class HotkeyCaptureSessionTests
{
    [Theory]
    [InlineData(0x1Bu, ChordModifiers.None, HotkeyCaptureInput.Cancelled)]
    [InlineData(0x08u, ChordModifiers.None, HotkeyCaptureInput.Cleared)]
    [InlineData(0x2Eu, ChordModifiers.None, HotkeyCaptureInput.Cleared)]
    [InlineData(0x27u, ChordModifiers.Control | ChordModifiers.Alt, HotkeyCaptureInput.Accepted)]
    public void TerminalKeyEndsSignalBeforeUiAssignmentOrConfirmation(uint key, ChordModifiers modifiers,
        HotkeyCaptureInput expected)
    {
        var lease = new FakeLease();
        using var capture = new HotkeyCaptureSession(() => lease);
        Assert.True(capture.Begin(WindowCommand.LeftHalf));
        Assert.Equal(expected, capture.ReadKey(key, modifiers));
        Assert.Null(capture.Command);
        Assert.Equal(1, lease.Disposals);
        capture.End();
        Assert.Equal(1, lease.Disposals);
        Assert.Equal(HotkeyCaptureInput.Ignored, capture.ReadKey(key, modifiers));
    }

    [Theory]
    [InlineData(0x11u, HotkeyCaptureInput.Modifier)]
    [InlineData(0x41u, HotkeyCaptureInput.Invalid)]
    public void IncompleteInputKeepsSignalUntilCancellation(uint key, HotkeyCaptureInput expected)
    {
        var lease = new FakeLease();
        using var capture = new HotkeyCaptureSession(() => lease);
        capture.Begin(WindowCommand.LeftHalf);
        Assert.Equal(expected, capture.ReadKey(key, ChordModifiers.None));
        Assert.Equal(WindowCommand.LeftHalf, capture.Command);
        Assert.Equal(0, lease.Disposals);
        capture.End();
        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public void StartingAnotherFieldEndsPreviousLeaseAndRepeatedEndOrDisposeIsSafe()
    {
        var leases = new List<FakeLease>();
        using var capture = new HotkeyCaptureSession(() =>
        {
            var lease = new FakeLease();
            leases.Add(lease);
            return lease;
        });
        capture.Begin(WindowCommand.LeftHalf);
        capture.Begin(WindowCommand.RightHalf);
        Assert.Equal(1, leases[0].Disposals);
        Assert.Equal(0, leases[1].Disposals);
        Assert.Equal(WindowCommand.RightHalf, capture.Command);
        capture.End();
        capture.End();
        capture.Dispose();
        Assert.All(leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public void PageDisposalEndsActiveCapture()
    {
        var lease = new FakeLease();
        var capture = new HotkeyCaptureSession(() => lease);
        capture.Begin(WindowCommand.LeftHalf);
        capture.Dispose();
        Assert.Null(capture.Command);
        Assert.Equal(1, lease.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalCreationFailureDoesNotPreventCaptureOrRepeatedEnd(bool denied)
    {
        using var capture = new HotkeyCaptureSession(() =>
            throw (denied ? new UnauthorizedAccessException() : new IOException()));
        Assert.True(capture.Begin(WindowCommand.LeftHalf));
        Assert.Equal(WindowCommand.LeftHalf, capture.Command);
        Assert.Equal(HotkeyCaptureInput.Accepted, capture.ReadKey(0x27, ChordModifiers.Control));
        capture.End();
    }

    [Fact]
    public void BusySignalDoesNotStartAnUnprotectedSecondCapture()
    {
        using var capture = new HotkeyCaptureSession(() => null);
        Assert.False(capture.Begin(WindowCommand.LeftHalf));
        Assert.Null(capture.Command);
        capture.End();
    }

    [Fact]
    public void CaptureNamesAreStableLocalAndSeparatedByUser()
    {
        var name = HotkeyCaptureSession.MutexName("S-1-5-21-100");
        Assert.StartsWith(@"Local\Snapwell.HotkeyCapture.", name);
        Assert.Equal(name, HotkeyCaptureSession.MutexName("S-1-5-21-100"));
        Assert.NotEqual(name, HotkeyCaptureSession.MutexName("S-1-5-21-101"));
    }

    private sealed class FakeLease : IDisposable
    {
        internal int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
