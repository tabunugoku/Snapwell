using System.Collections.Concurrent;
using System.Threading.Channels;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class AppHostTests
{
    [Fact]
    public void StartupFollowsRecoverySettingsHooksDisableTrayOrder()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        Assert.True(fixture.Host.Start());
        Assert.Equal(new[] { "mutex", "recover", "portable", "device", "hooks.start", "disable", "tray.show" },
            fixture.Calls.ToArray());
    }

    [Fact]
    public void ExistingInstanceDoesNotRecoverStartHooksOrRestoreAnything()
    {
        using var fixture = new Fixture();
        fixture.Instance.Available = false;
        Assert.False(fixture.Host.Start());
        fixture.Host.Shutdown();
        Assert.Equal(new[] { "mutex" }, fixture.Calls.ToArray());
    }

    [Fact]
    public void MissingConsentPreventsDisableOnStartup()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true });
        fixture.Host.Start();
        Assert.DoesNotContain("disable", fixture.Calls);
    }

    [Fact]
    public void DeclinedConsentLeavesDeviceAndOsUnchanged()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Consent.Accept = false;
        fixture.Tray.RequestStandardSnap();
        Assert.Equal(1, fixture.Consent.Calls);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.Equal(new DeviceState(), fixture.Device.Value);
        Assert.Equal(0, fixture.Device.Saves);
    }

    [Fact]
    public void AcceptedConsentPersistsChoiceAndDisablesStandardSnap()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Tray.RequestStandardSnap();
        Assert.True(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.True(fixture.Device.Value.DisableStandardSnap);
        Assert.Contains("disable", fixture.Calls);
        fixture.Tray.RequestStandardSnap();
        Assert.False(fixture.Device.Value.DisableStandardSnap);
        Assert.Contains("restore", fixture.Calls);
        Assert.Equal(1, fixture.Consent.Calls);
    }

    [Theory]
    [InlineData("tray")]
    [InlineData("session")]
    [InlineData("unhandled")]
    [InlineData("twice")]
    public void EveryExitPathRestoresExactlyOnce(string path)
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        switch (path)
        {
            case "tray": fixture.Tray.RequestExit(); break;
            case "session": fixture.Tray.EndSession(); break;
            case "unhandled": fixture.Host.HandleUnhandledException(new Exception("test")); break;
            case "twice": fixture.Host.Shutdown(); fixture.Host.Shutdown(); break;
        }

        fixture.Host.Shutdown();
        Assert.Equal(1, fixture.Calls.Count(call => call == "restore"));
        Assert.Equal(1, fixture.Calls.Count(call => call == "corners"));
        Assert.Equal(new[] { "motion.stop", "hooks.stop", "restore", "corners", "tray.remove", "mutex.release" },
            fixture.Calls.SkipWhile(call => call != "motion.stop").ToArray());
    }

    [Fact]
    public void HookStopFailureDoesNotPreventRestorationOrRelease()
    {
        using var fixture = new Fixture();
        fixture.Hooks.ThrowOnStop = true;
        fixture.Host.Start();
        fixture.Host.Shutdown();
        Assert.Contains("restore", fixture.Calls);
        Assert.Contains("corners", fixture.Calls);
        Assert.Contains("mutex.release", fixture.Calls);
        Assert.Single(fixture.Log.Errors);
    }

    [Fact]
    public void PauseRestoresCancelsAndResumeDisablesWithConsent()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Host.Start();
        fixture.Tray.RequestToggle();
        Assert.False(fixture.Host.Enabled);
        Assert.False(fixture.Device.Value.Enabled);
        Assert.Contains("restore", fixture.Calls);
        Assert.Equal(1, fixture.Runtime.Cancels);
        fixture.Host.ToggleEnabled();
        Assert.True(fixture.Host.Enabled);
        Assert.Equal(2, fixture.Calls.Count(call => call == "disable"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HookStartupFailureKeepsStandardSnapAndShowsDiagnostic(bool throws)
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Hooks.Starts = false;
        fixture.Hooks.ThrowOnStart = throws;
        Assert.True(fixture.Host.Start());
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.NotNull(fixture.Tray.State?.Status);
        Assert.Contains("tray.show", fixture.Calls);
        Assert.False(fixture.Runtime.Enabled);
    }

    [Fact]
    public void ConcurrentShutdownAndExceptionPathsRunCleanupOnlyOnce()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        Parallel.For(0, 20, index =>
        {
            if (index % 2 == 0)
            {
                fixture.Host.Shutdown();
            }
            else
            {
                fixture.Host.HandleUnhandledException(new Exception("test"));
            }
        });
        Assert.Equal(1, fixture.Calls.Count(call => call == "restore"));
        Assert.Equal(1, fixture.Calls.Count(call => call == "corners"));
    }

    [Fact]
    public void FailedLoggingNeverEscapesUnhandledExceptionHandler()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Log.ThrowOnLog = true;
        fixture.Hooks.ThrowOnStop = true;
        fixture.Host.HandleUnhandledException(new Exception("test"));
        Assert.Contains("corners", fixture.Calls);
        Assert.Contains("mutex.release", fixture.Calls);
    }

    [Fact]
    public void UnobservedTaskExceptionIsLoggedWithoutStoppingHost()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Host.HandleUnobservedTaskException(new Exception("test"));
        Assert.Single(fixture.Log.Errors);
        Assert.DoesNotContain("restore", fixture.Calls);
    }

    [Fact]
    public void RestoreMenuClearsDisableChoiceAndRestoresImmediately()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Host.Start();
        fixture.Tray.RequestRestore();
        Assert.Contains("restore", fixture.Calls);
        Assert.False(fixture.Device.Value.DisableStandardSnap);
    }

    [Fact]
    public void PausedStartupNeverDisablesStandardSnap()
    {
        using var fixture = new Fixture(new()
        {
            Enabled = false,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = true,
        });
        fixture.Host.Start();
        Assert.DoesNotContain("disable", fixture.Calls);
    }

    [Fact]
    public void FutureDeviceFileIsNotSavedByPause()
    {
        using var fixture = new Fixture();
        fixture.Device.ReadOnly = true;
        fixture.Host.Start();
        fixture.Host.ToggleEnabled();
        Assert.Equal(0, fixture.Device.Saves);
    }

    [Fact]
    public void HookEventsAreDeliveredOffTheCallingThread()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Hooks.Channel.Writer.TryWrite(new MoveSizeStarted(42));
        Assert.True(SpinWait.SpinUntil(() => !fixture.Runtime.Events.IsEmpty, 5000));
        Assert.Equal(new MoveSizeStarted(42), Assert.Single(fixture.Runtime.Events));
    }

    private sealed class Fixture : IDisposable
    {
        internal ConcurrentQueue<string> Calls { get; } = new();
        internal FakeInstance Instance { get; }
        internal FakeGuard Guard { get; }
        internal FakeStore<DeviceState> Device { get; }
        internal FakeHooks Hooks { get; }
        internal FakeTray Tray { get; }
        internal FakeConsent Consent { get; } = new();
        internal FakeLog Log { get; } = new();
        internal FakeRuntime Runtime { get; }
        internal AppHost Host { get; }

        internal Fixture(DeviceState? state = null)
        {
            Instance = new(Calls);
            Guard = new(Calls);
            Device = new(state ?? new(), Calls, "device");
            Hooks = new(Calls);
            Tray = new(Calls);
            Runtime = new(Calls);
            Host = new(Instance, Guard, new FakeStore<PortableSettings>(new(), Calls, "portable"),
                Device, Hooks, Tray, Consent, Log, () => Calls.Enqueue("corners"), (_, _, _) => Runtime);
        }

        public void Dispose() => Host.Shutdown();
    }

    private sealed class FakeInstance(ConcurrentQueue<string> calls) : ISingleInstance
    {
        internal bool Available = true;
        public bool TryAcquire() { calls.Enqueue("mutex"); return Available; }
        public void Release() => calls.Enqueue("mutex.release");
    }

    private sealed class FakeGuard(ConcurrentQueue<string> calls) : ISnapGuard
    {
        public void RecoverOnStartup() => calls.Enqueue("recover");
        public bool Disable() { calls.Enqueue("disable"); return true; }
        public bool Restore() { calls.Enqueue("restore"); return true; }
    }

    private sealed class FakeStore<T>(T value, ConcurrentQueue<string> calls, string name) : ISettingsStore<T>
    {
        internal T Value = value;
        internal int Saves;
        internal bool ReadOnly;
        public SettingsLoadResult<T> Load() { calls.Enqueue(name); return new(Value, IsReadOnly: ReadOnly); }
        public void Save(T updated) { Value = updated; Saves++; }
    }

    private sealed class FakeHooks(ConcurrentQueue<string> calls) : IHookService
    {
        internal Channel<HookEvent> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<HookEvent>();
        internal bool Starts = true;
        internal bool ThrowOnStart;
        internal bool ThrowOnStop;
        public bool Start()
        {
            calls.Enqueue("hooks.start");
            if (ThrowOnStart) { throw new IOException("start"); }
            return Starts;
        }

        public void Stop()
        {
            calls.Enqueue("hooks.stop");
            if (ThrowOnStop) { throw new IOException("stop"); }
        }

        public ChannelReader<HookEvent> Events => Channel.Reader;
        public bool MouseTrackingEnabled { private get; set; }
        public Exception? Failure => null;
    }

    private sealed class FakeTray(ConcurrentQueue<string> calls) : ITrayService
    {
        public event Action? ToggleRequested;
        public event Action? StandardSnapRequested;
        public event Action? RestoreRequested;
        public event Action? ExitRequested;
        public event Action? SessionEnding;
        internal TrayState? State;
        public void Show(TrayState state) { State = state; calls.Enqueue("tray.show"); }
        public void Update(TrayState state) => State = state;
        public void Remove() => calls.Enqueue("tray.remove");
        public void Post(Action action) => action();
        public void RunMessageLoop() { }
        internal void RequestToggle() => ToggleRequested?.Invoke();
        internal void RequestStandardSnap() => StandardSnapRequested?.Invoke();
        internal void RequestRestore() => RestoreRequested?.Invoke();
        internal void RequestExit() => ExitRequested?.Invoke();
        internal void EndSession() => SessionEnding?.Invoke();
    }

    private sealed class FakeConsent : IConsentPrompt
    {
        internal bool Accept = true;
        internal int Calls;
        public bool ConfirmStandardSnapChange() { Calls++; return Accept; }
    }

    private sealed class FakeLog : IAppLog
    {
        internal ConcurrentQueue<Exception> Errors { get; } = new();
        internal bool ThrowOnLog;
        public void Error(Exception exception)
        {
            if (ThrowOnLog) { throw new IOException("log"); }
            Errors.Enqueue(exception);
        }
    }

    private sealed class FakeRuntime(ConcurrentQueue<string> calls) : IAppRuntime
    {
        internal bool Enabled;
        internal int Cancels;
        internal ConcurrentQueue<HookEvent> Events { get; } = new();
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void Handle(HookEvent value) => Events.Enqueue(value);
        public void CancelAll() => Cancels++;
        public void Dispose() => calls.Enqueue("motion.stop");
    }
}
