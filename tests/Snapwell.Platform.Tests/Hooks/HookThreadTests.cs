using System.Collections.Concurrent;
using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>実際のフックを登録せず、スレッドの起動・解除・二重停止を確認する。</summary>
public sealed class HookThreadTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapturePassesKeysWithoutDispatchAndRestoresNextPressWithoutRebinding(bool allowRepeat)
    {
        var capturing = false;
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings([new(42, KeyModifiers.Control | KeyModifiers.Alt, 0x27, true, allowRepeat)]);
        using var api = new FakeHookApi();
        using var hooks = new HookThread(matcher, 8, api);
        Assert.True(hooks.Start());
        hooks.ProcessKey(0xA2, true);
        hooks.ProcessKey(0xA4, true);
        Assert.True(hooks.ProcessKey(0x27, true));
        Assert.True(hooks.ProcessKey(0x27, false));

        capturing = true;
        Assert.False(hooks.ProcessKey(0x27, true));
        Assert.False(hooks.ProcessKey(0x27, true));
        capturing = false;
        Assert.False(hooks.ProcessKey(0x27, true));
        Assert.False(hooks.ProcessKey(0x27, false));
        Assert.True(hooks.ProcessKey(0x27, true));
        Assert.True(hooks.ProcessKey(0x27, false));
        Assert.True(hooks.Stop());

        var delivered = new List<HookEvent>();
        while (hooks.Events.TryRead(out var value)) { delivered.Add(value); }
        Assert.Equal<HookEvent>([new KeyChord(42), new KeyChord(42)], delivered);
    }

    [Fact]
    public void CaptureTracksModifierReleasesAndPassesPreviouslySuppressedKeyUp()
    {
        var capturing = false;
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings([new(1, KeyModifiers.Control, 0x41)]);
        matcher.ProcessKey(0xA2, true);
        Assert.True(matcher.ProcessKey(0x41, true).Suppress);
        capturing = true;
        Assert.Equal(default, matcher.ProcessKey(0x41, false));
        Assert.Equal(default, matcher.ProcessKey(0xA2, false));
        capturing = false;
        Assert.Equal(default, matcher.ProcessKey(0x41, true));
        matcher.ProcessKey(0x41, false);
        matcher.ProcessKey(0xA2, true);
        Assert.Equal(new HotkeyMatch(true, 1, true), matcher.ProcessKey(0x41, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LifecycleFloodNeverDisplacesCriticalEventsAtEitherDeliveryStage(bool callbackStage)
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 4, api);
        void Send(RawHookEvent value)
        {
            if (callbackStage)
            {
                hooks.Enqueue(value);
            }
            else
            {
                hooks.Publish(value);
            }
        }

        RawHookEvent[] critical =
        [
            new(RawHookKind.MoveStart, 42),
            new(RawHookKind.MouseButton, Detail: (int)MouseButtonKind.Left, IsDown: true),
            new(RawHookKind.MoveEnd, 42, Time: 123),
            new(RawHookKind.MouseButton, Detail: (int)MouseButtonKind.Left, IsDown: false),
            new(RawHookKind.Key, Detail: 7),
        ];
        foreach (var value in critical)
        {
            Send(value);
            for (var index = 0; index < 2048; index++)
            {
                Send(new(index % 2 == 0 ? RawHookKind.WindowShown : RawHookKind.WindowDestroyed, index + 100));
            }
        }

        if (callbackStage)
        {
            Assert.True(hooks.Start());
            Assert.True(hooks.Stop());
        }

        var delivered = new List<HookEvent>();
        while (hooks.Events.TryRead(out var value))
        {
            delivered.Add(value);
        }

        HookEvent[] expected =
        [
            new MoveSizeStarted(42),
            new MouseButton(MouseButtonKind.Left, true, 0, 0, 0),
            new MoveSizeEnded(42, 123),
            new MouseButton(MouseButtonKind.Left, false, 0, 0, 0),
            new KeyChord(7),
        ];
        Assert.Equal(expected, delivered.Where(value => value is not (WindowShown or WindowDestroyed)));
        Assert.InRange(delivered.Count(value => value is WindowShown or WindowDestroyed), 1, 4);
    }

    [Fact]
    public void ConsumingLifecycleEventsReleasesCapacityForLaterNotifications()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 1, api);
        hooks.Publish(new(RawHookKind.WindowShown, 1));
        hooks.Publish(new(RawHookKind.WindowDestroyed, 2));
        Assert.True(hooks.Events.TryRead(out var first));
        Assert.Equal(new WindowShown(1), first);
        Assert.False(hooks.Events.TryRead(out _));
        hooks.Publish(new(RawHookKind.WindowShown, 3));
        Assert.True(hooks.Events.TryRead(out var later));
        Assert.Equal(new WindowShown(3), later);
    }

    [Fact]
    public async Task CriticalCallbackEventsRemainOrderedDuringConcurrentDelivery()
    {
        var buffer = new HookInputBuffer(4);
        var producer = Task.Run(() =>
        {
            for (var index = 1; index <= 10000; index++)
            {
                buffer.Write(new(RawHookKind.Key, Detail: index));
            }
        });
        var last = 0;
        while (!producer.IsCompleted)
        {
            if (buffer.TryRead(out var value))
            {
                Assert.Equal(++last, value.Detail);
            }
        }

        await producer;
        while (buffer.TryRead(out var remaining))
        {
            Assert.Equal(++last, remaining.Detail);
        }

        Assert.Equal(10000, last);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WindowLifecycleEventsAreDeliveredOutsideCallback(bool shown)
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        hooks.Publish(new RawHookEvent(shown ? RawHookKind.WindowShown : RawHookKind.WindowDestroyed, 42));
        Assert.True(hooks.Events.TryRead(out var value));
        if (shown)
        {
            Assert.Equal((nint)42, Assert.IsType<WindowShown>(value).Handle);
        }
        else
        {
            Assert.Equal((nint)42, Assert.IsType<WindowDestroyed>(value).Handle);
        }
    }

    [Theory]
    [InlineData(123u)]
    [InlineData(4294967280u)]
    [InlineData(0u)]
    public void PublishedMoveEndPreservesOsTimestamp(uint time)
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        hooks.Publish(new RawHookEvent(RawHookKind.MoveEnd, 42, Time: time));
        Assert.True(hooks.Events.TryRead(out var value));
        var end = Assert.IsType<MoveSizeEnded>(value);
        Assert.Equal((nint)42, end.Handle);
        Assert.Equal(time, end.TimeMs);
    }

    /// <summary>登録したスレッドで解除し、別スレッドからの二重停止を直列化する。</summary>
    [Fact]
    public async Task ConcurrentStopsUnregisterAllHooksOnOwningThread()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        Assert.True(hooks.Start());
        var results = await Task.WhenAll(Task.Run(hooks.Stop), Task.Run(hooks.Stop));
        Assert.All(results, Assert.True);
        Assert.Equal(7, api.RegisterCount);
        Assert.Equal(7, api.UnregisterCount);
        Assert.Equal(1, api.PostCount);
        Assert.Single(api.RegistrationThreads.Distinct());
        Assert.Equal(api.RegistrationThreads.First(), Assert.Single(api.RemovalThreads.Distinct()));
        Assert.True(hooks.Events.Completion.IsCompletedSuccessfully);
        Assert.Null(hooks.Failure);
        Assert.False(hooks.Start());
    }

    /// <summary>途中の登録失敗でも成功したフックだけを解除して配送を閉じる。</summary>
    [Fact]
    public void PartialRegistrationFailureCleansUpSuccessfulHooks()
    {
        using var api = new FakeHookApi { FailedRegistration = 2 };
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.False(hooks.Start());
        Assert.NotNull(hooks.Failure);
        Assert.Equal(6, api.UnregisterCount);
        Assert.True(hooks.Stop());
        Assert.True(hooks.Events.Completion.IsCompletedSuccessfully);
    }

    /// <summary>起動前の停止と二重破棄で API を呼ばず、停止後に再登録しない。</summary>
    [Fact]
    public void StopBeforeStartClosesLifetimeWithoutRegistering()
    {
        using var api = new FakeHookApi();
        var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Stop());
        Assert.False(hooks.Start());
        hooks.Dispose();
        hooks.Dispose();
        Assert.Equal(0, api.RegisterCount);
        Assert.True(hooks.Events.Completion.IsCompletedSuccessfully);
    }

    /// <summary>メッセージ送信の失敗後も状態を保持して停止を再試行できる。</summary>
    [Fact]
    public void FailedQuitCanBeRetriedWithoutReleasingLiveState()
    {
        using var api = new FakeHookApi { PostSucceeds = false };
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        Assert.False(hooks.Stop());
        Assert.Equal(0, api.UnregisterCount);
        api.PostSucceeds = true;
        Assert.True(hooks.Stop());
        Assert.Equal(7, api.UnregisterCount);
    }

    /// <summary>送信がブロックしても入力判定とフック解除・破棄は待たされない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BlockedDummySendDoesNotBlockProcessingOrShutdown(bool stopFirst)
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var finished = new ManualResetEventSlim(false);
        var callbackThread = 0;
        var senderThread = 0;
        var sender = new CallbackDummySender(() =>
        {
            senderThread = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait();
            finished.Set();
            return true;
        });
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        using var api = new FakeHookApi();
        using var hooks = new HookThread(matcher, 1, api, sender);
        Assert.True(hooks.Start());
        try
        {
            var processing = Task.Run(() =>
            {
                callbackThread = Environment.CurrentManagedThreadId;
                hooks.ProcessKey(0x5B, true);
                return hooks.ProcessKey(0x25, true);
            });
            Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(callbackThread, senderThread);

            var laterKeys = Task.Run(() =>
            {
                Assert.True(hooks.ProcessKey(0x25, true));
                Assert.True(hooks.ProcessKey(0x25, false));
                Assert.False(hooks.ProcessKey(0x5B, false));
                hooks.ProcessKey(0x5B, true);
                Assert.True(hooks.ProcessKey(0x25, true));
            });
            await laterKeys.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() =>
            {
                if (stopFirst) { Assert.True(hooks.Stop()); }
                hooks.Dispose();
                hooks.Dispose();
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, api.UnregisterCount);
            Assert.Null(hooks.Failure);
        }
        finally
        {
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    /// <summary>送信の失敗や例外でも次の要求を処理し、注入入力で修飾状態を変えない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DummySendFailureAndInjectedInputDoNotDisableHotkeys(int outcome)
    {
        using var sent = new SemaphoreSlim(0);
        var calls = 0;
        var sender = new CallbackDummySender(() =>
        {
            Interlocked.Increment(ref calls);
            sent.Release();
            if (outcome == 2) { throw new InvalidOperationException(); }
            return outcome == 1;
        });
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        using var api = new FakeHookApi();
        using var hooks = new HookThread(matcher, 8, api, sender);
        Assert.True(hooks.Start());
        hooks.ProcessKey(0x5B, true);
        Assert.False(hooks.ProcessKey(0x25, true, injected: true));
        Assert.True(hooks.ProcessKey(0x25, true));
        Assert.True(await sent.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.False(hooks.ProcessKey(0x5B, false, injected: true));
        Assert.True(hooks.ProcessKey(0x25, true));
        Assert.True(hooks.ProcessKey(0x25, false));
        Assert.False(hooks.ProcessKey(0x5B, false));
        hooks.ProcessKey(0x5B, true);
        Assert.True(hooks.ProcessKey(0x25, true));
        Assert.True(await sent.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Null(hooks.Failure);
    }

    private sealed class CallbackDummySender(Func<bool> send) : IDummyKeySender
    {
        public bool Send() => send();
    }

    private sealed class FakeHookApi : IHookApi, IDisposable
    {
        private readonly ManualResetEventSlim _quit = new(false);
        internal int RegisterCount { get; private set; }
        internal int UnregisterCount { get; private set; }
        internal int PostCount { get; private set; }
        internal int FailedRegistration { get; init; }
        internal bool PostSucceeds { get; set; } = true;
        internal ConcurrentBag<int> RegistrationThreads { get; } = [];
        internal ConcurrentBag<int> RemovalThreads { get; } = [];

        public uint GetThreadId() => 123;
        public void CreateMessageQueue() { }
        public nint RegisterEvent(uint eventId, nint callback) => Register();
        public nint RegisterMouse(nint callback) => Register();
        public nint RegisterKeyboard(nint callback) => Register();
        public bool UnregisterEvent(nint hook) => Unregister();
        public bool UnregisterLowLevel(nint hook) => Unregister();

        public int ReadAndDispatchMessage()
        {
            _quit.Wait();
            return 0;
        }

        public bool PostQuit(uint threadId)
        {
            PostCount++;
            if (PostSucceeds)
            {
                _quit.Set();
            }

            return PostSucceeds;
        }

        public void Dispose() => _quit.Dispose();

        private nint Register()
        {
            RegistrationThreads.Add(Environment.CurrentManagedThreadId);
            RegisterCount++;
            return RegisterCount == FailedRegistration ? 0 : RegisterCount;
        }

        private bool Unregister()
        {
            RemovalThreads.Add(Environment.CurrentManagedThreadId);
            UnregisterCount++;
            return true;
        }
    }
}
