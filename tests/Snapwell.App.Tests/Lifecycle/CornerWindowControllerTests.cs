using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class CornerWindowControllerTests
{
    [Fact]
    public void EnablingEnumeratesExistingWindowsAndPauseRestoresOriginal()
    {
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), new ExistingWindows(),
            () => new() { CornerPreference = WindowCornerPreference.DoNotRound }, 999, new Log());
        controller.SetEnabled(true);
        Assert.Equal(CornerStyle.DoNotRound, Assert.Single(api.Calls));
        controller.SetEnabled(false);
        Assert.Equal(CornerStyle.RoundSmall, api.Calls.Last());
    }

    [Theory]
    [InlineData(false, 123u, 0u, true)]
    [InlineData(true, 123u, 0u, false)]
    [InlineData(false, 999u, 0u, false)]
    [InlineData(false, 123u, 128u, false)]
    public void ShownWindowReceivesPreferenceOnlyWhenEligible(bool maximized, uint processId,
        uint extendedStyle, bool expected)
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with
        {
            IsMaximized = maximized,
            ProcessId = processId,
            ExtendedStyle = extendedStyle,
        };
        var api = new CornerApi();
        var controller = new CornerWindowController(new(api), inspector,
            () => new() { CornerPreference = WindowCornerPreference.Round }, 999, new Log());
        controller.SetEnabled(true);
        controller.Handle(new WindowShown(1));
        Assert.Equal(expected ? new[] { CornerStyle.Round } : [], api.Calls);
        controller.SetEnabled(false);
        if (expected)
        {
            Assert.Equal(CornerStyle.RoundSmall, api.Calls.Last());
        }
    }

    private sealed class CornerApi : ICornerApi
    {
        internal List<CornerStyle> Calls { get; } = [];
        public CornerState? Read(nint handle) => new((long)handle, 123, 1, CornerStyle.RoundSmall);
        public bool IsWindow(nint handle) => true;
        public bool TrySet(nint handle, CornerStyle style) { Calls.Add(style); return true; }
    }

    private sealed class Log : IAppLog
    {
        public void Error(Exception exception) => throw new InvalidOperationException("Unexpected failure", exception);
    }

    private sealed class ExistingWindows : IWindowInspector
    {
        private readonly FakeInspector _inspector = new();
        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot) =>
            _inspector.TryInspect(handle, out snapshot);
        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
        public IReadOnlyList<nint> GetTopLevelWindows() => [1];
    }
}
