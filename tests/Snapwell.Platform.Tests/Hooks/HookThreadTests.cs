using System.Collections.Concurrent;
using Snapwell.Core.Hooks;
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

    /// <summary>張り直しの依頼で、二つの低レベルフックを同じスレッドで登録し直し、古い登録を外す。</summary>
    [Fact]
    public void RehookRegistersNewLowLevelHooksBeforeRemovingOldOnesOnOwningThread()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        var reports = new BlockingCollection<HookRehookReport>();
        hooks.Rehooked += reports.Add;
        Assert.True(hooks.Start());
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);

        hooks.Notify(HookEnvironmentChange.Unlocked);
        Assert.True(reports.TryTake(out var report, TimeSpan.FromSeconds(5)));
        Assert.Equal(HookRehookReason.Environment, report.Reason);
        Assert.True(report.Succeeded);
        Assert.Equal(1, report.Count);
        Assert.Equal(HookRehookEvidence.None, report.Evidence);
        Assert.Equal(9, api.RegisterCount);
        Assert.Equal(2, api.UnregisterCount);
        Assert.Equal(["register", "register", "unregister", "unregister"], api.LowLevelOrder.Skip(2).Take(4));
        Assert.Single(api.RegistrationThreads.Distinct());
        Assert.Equal(api.RegistrationThreads.First(), Assert.Single(api.RemovalThreads.Distinct()));

        // 張り直したあとの停止は、新しい二つを含めて外し、成功を返す。古いハンドルの解除失敗は混ぜない。
        Assert.True(hooks.Stop());
        Assert.Equal(2 + 7, api.UnregisterCount);
        Assert.True(hooks.Events.Completion.IsCompletedSuccessfully);
    }

    /// <summary>OS に外されて古いハンドルの解除が失敗しても、停止の成否には影響しない。</summary>
    [Fact]
    public void FailedUnhookOfOldHandleDuringRehookDoesNotFailStop()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        var reports = new BlockingCollection<HookRehookReport>();
        hooks.Rehooked += reports.Add;
        Assert.True(hooks.Start());
        api.FailUnregisterOf.Add(6);
        api.FailUnregisterOf.Add(7);
        hooks.Notify(HookEnvironmentChange.Unlocked);
        Assert.True(reports.TryTake(out _, TimeSpan.FromSeconds(5)));
        Assert.True(hooks.Stop());
    }

    /// <summary>片方しか登録できなかったときは古いフックを残し、成功した分を外す。</summary>
    [Fact]
    public void PartialRehookFailureKeepsOldHooksAndRemovesNewOnes()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        var reports = new BlockingCollection<HookRehookReport>();
        hooks.Rehooked += reports.Add;
        Assert.True(hooks.Start());
        api.FailedRegistration = 9;
        hooks.Notify(HookEnvironmentChange.Unlocked);
        Assert.True(reports.TryTake(out var report, TimeSpan.FromSeconds(5)));
        Assert.False(report.Succeeded);
        Assert.Equal(1, api.UnregisterCount);
        Assert.True(hooks.Stop());
        Assert.Equal(1 + 7, api.UnregisterCount);
    }

    /// <summary>構築から登録までに入力があっても、登録の完了を基準にするので疑わない。</summary>
    [Fact]
    public void InputBeforeRegistrationDoesNotLookLikeADeadHook()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        var now = unchecked((uint)Environment.TickCount);
        api.TickCount = now + 10_000;
        api.LastInputTick = now + 5_000;
        Assert.True(hooks.Start());
        // 登録完了の時刻は偽の時計の now + 10_000。その前の入力は疑いの対象にならない。
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>起動前の通知は予約を残さない。起動直後に不要な張り直しが走らないようにする。</summary>
    [Fact]
    public void NotifyBeforeStartDoesNotScheduleARehook()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        hooks.Notify(HookEnvironmentChange.Unlocked);
        Assert.True(hooks.Start());
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>張り直しの途中で例外が出ても、メッセージループを止めず、後始末で全フックを外せる。</summary>
    [Fact]
    public void ExceptionDuringRehookKeepsTheMessageLoopAlive()
    {
        using var api = new FakeHookApi { ThrowOnRegisterAfter = 7 };
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        hooks.Notify(HookEnvironmentChange.Unlocked);
        Assert.True(SpinWait.SpinUntil(() => api.RegisterCount > 7, TimeSpan.FromSeconds(5)));
        Assert.Null(hooks.Failure);
        Assert.True(hooks.Stop());
        Assert.Null(hooks.Failure);
    }

    /// <summary>OS の入力だけが進み（タッチ・ペン・タッチパッド）コールバックが来なくても、証拠がなければ張り直さない。</summary>
    [Fact]
    public void OsInputWithoutCallbacksAloneDoesNotRehook()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var start = unchecked((uint)Environment.TickCount);
        api.TickCount = start + 60_000;
        api.LastInputTick = start + 59_000;
        for (var i = 0; i < 5; i++)
        {
            api.TickCount += 2_000;
            api.LastInputTick = api.TickCount;
            hooks.CheckHealth();
        }

        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>張り直しの記録に、疑いの証拠の種類と各経過が残る（入力の中身は残さない）。</summary>
    [Fact]
    public void RehookReportCarriesEvidenceAndElapsedTimes()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        var reports = new BlockingCollection<HookRehookReport>();
        hooks.Rehooked += reports.Add;
        Assert.True(hooks.Start());
        var start = api.TickCount;
        api.TickCount = start + 60_000;
        api.LastInputTick = start + 59_000;
        api.KeyHeld = true;
        hooks.CheckHealth();
        Assert.True(reports.TryTake(out var report, TimeSpan.FromSeconds(5)));
        Assert.Equal(HookRehookReason.InputMismatch, report.Reason);
        Assert.Equal(HookRehookEvidence.KeyboardSilent, report.Evidence);
        Assert.Equal(59_000, report.GapMs);
        Assert.Equal(60_000, report.SinceKeyboardMs);
        Assert.Equal(1_000, report.SinceInputMs);
        Assert.Equal(HookRehookPolicy.InitialBackoffMs, report.BackoffMs);
        Assert.True(hooks.Stop());
    }

    /// <summary>マウスが生きていても、キーが押されているのにキーボードのコールバックが来なければ張り直す。</summary>
    [Fact]
    public void KeyHeldWithSilentKeyboardHookRehooksEvenWhileMouseIsAlive()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var now = api.TickCount;
        api.TickCount = now + 5_000;
        api.LastInputTick = now + 5_000;
        // マウスのコールバックは来ている（時刻の比較だけでは見えない）が、キーボードは来ない。
        hooks.TouchMouse();
        api.KeyHeld = true;
        hooks.CheckHealth();
        Assert.Equal(1, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>キーが押されていなければ、キーボードが静かでも疑わない。</summary>
    [Fact]
    public void SilentKeyboardWithoutHeldKeyIsNotSuspicious()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var now = api.TickCount;
        api.TickCount = now + 5_000;
        api.LastInputTick = now;
        hooks.TouchMouse();
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>キーボードが生きていても、カーソルが動いたのにマウスのコールバックが来なければ張り直す。</summary>
    [Fact]
    public void CursorMovedWithSilentMouseHookRehooksEvenWhileKeyboardIsAlive()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var now = api.TickCount;
        // 最初の観測。カーソルの位置を覚えるだけ。
        api.TickCount = now + 2_000;
        api.LastInputTick = now;
        hooks.CheckHealth();
        // 次の観測までにカーソルが動いたが、マウスのコールバックはない。キーボードは来ている。
        api.TickCount = now + 4_000;
        api.CursorX = 50;
        api.LastInputTick = now + 4_000;
        hooks.TouchKeyboard();
        hooks.CheckHealth();
        Assert.Equal(1, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>SetCursorPos のようにカーソルだけが動き、OS の入力がなければ疑わない。</summary>
    [Fact]
    public void CursorMovedWithoutOsInputIsNotSuspicious()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var now = api.TickCount;
        api.TickCount = now + 2_000;
        api.LastInputTick = now;
        hooks.CheckHealth();
        api.TickCount = now + 4_000;
        api.CursorX = 50;
        hooks.TouchKeyboard();
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    /// <summary>カーソルが動き、その間にマウスのコールバックも来ていれば疑わない。</summary>
    [Fact]
    public void CursorMovedWithMouseCallbacksIsNotSuspicious()
    {
        using var api = new FakeHookApi();
        using var hooks = new HookThread(new HotkeyMatcher(), 8, api);
        Assert.True(hooks.Start());
        var now = api.TickCount;
        api.LastInputTick = now;
        hooks.CheckHealth();
        // コールバックは前回の観測のあとに来たことにする。
        api.TickCount = now + 1_000;
        api.CursorX = 50;
        hooks.TouchMouse();
        hooks.CheckHealth();
        Assert.Equal(0, api.PostRehookCount);
        Assert.True(hooks.Stop());
    }

    private sealed class FakeHookApi : IHookApi, IDisposable
    {
        private readonly BlockingCollection<uint> _messages = [];
        internal uint TickCount { get; set; } = unchecked((uint)Environment.TickCount);
        internal uint LastInputTick { get; set; } = unchecked((uint)Environment.TickCount);
        internal int PostRehookCount { get; private set; }
        internal ConcurrentBag<nint> FailUnregisterOf { get; } = [];
        internal ConcurrentQueue<string> LowLevelOrder { get; } = [];
        internal int RegisterCount { get; private set; }
        internal int UnregisterCount { get; private set; }
        internal int PostCount { get; private set; }
        internal int FailedRegistration { get; set; }
        internal bool PostSucceeds { get; set; } = true;
        internal ConcurrentBag<int> RegistrationThreads { get; } = [];
        internal ConcurrentBag<int> RemovalThreads { get; } = [];

        public uint GetThreadId() => 123;
        public void CreateMessageQueue() { }
        public nint RegisterEvent(uint eventId, nint callback) => Register();
        public nint RegisterMouse(nint callback) => RegisterLowLevel();
        public nint RegisterKeyboard(nint callback) => RegisterLowLevel();
        public bool UnregisterEvent(nint hook) => Unregister(hook);

        public bool UnregisterLowLevel(nint hook)
        {
            LowLevelOrder.Enqueue("unregister");
            return Unregister(hook);
        }

        public int ReadAndDispatchMessage(out uint messageId)
        {
            messageId = _messages.Take();
            return messageId == 0 ? 0 : 1;
        }

        public uint GetTickCount() => TickCount;
        internal bool KeyHeld { get; set; }
        internal int ThrowOnRegisterAfter { get; init; } = int.MaxValue;
        internal int CursorX { get; set; }
        public bool IsNonModifierKeyDown() => KeyHeld;

        public bool TryGetCursorPosition(out int x, out int y)
        {
            x = CursorX;
            y = 0;
            return true;
        }

        public bool TryGetLastInputTick(out uint tick)
        {
            tick = LastInputTick;
            return true;
        }

        public bool PostRehook(uint threadId)
        {
            PostRehookCount++;
            _messages.Add(HookMessages.Rehook);
            return true;
        }

        public bool PostQuit(uint threadId)
        {
            PostCount++;
            if (PostSucceeds)
            {
                _messages.Add(0);
            }

            return PostSucceeds;
        }

        public void Dispose() => _messages.Dispose();

        private nint RegisterLowLevel()
        {
            if (RegisterCount >= ThrowOnRegisterAfter)
            {
                RegisterCount++;
                throw new InvalidOperationException();
            }

            LowLevelOrder.Enqueue("register");
            return Register();
        }

        private nint Register()
        {
            RegistrationThreads.Add(Environment.CurrentManagedThreadId);
            RegisterCount++;
            return RegisterCount == FailedRegistration ? 0 : RegisterCount;
        }

        private bool Unregister(nint hook)
        {
            RemovalThreads.Add(Environment.CurrentManagedThreadId);
            UnregisterCount++;
            return !FailUnregisterOf.Contains(hook);
        }
    }
}
