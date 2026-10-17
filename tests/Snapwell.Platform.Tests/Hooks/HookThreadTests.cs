using System.Collections.Concurrent;
using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>実際のフックを登録せず、スレッドの起動・解除・二重停止を確認する。</summary>
public sealed class HookThreadTests
{
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
        Assert.Equal(5, api.RegisterCount);
        Assert.Equal(5, api.UnregisterCount);
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
        Assert.Equal(4, api.UnregisterCount);
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
        Assert.Equal(5, api.UnregisterCount);
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
