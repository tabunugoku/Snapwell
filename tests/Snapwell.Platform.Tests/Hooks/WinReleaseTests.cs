using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>Win を含む操作の後だけ、最後の Win 解放で一度ダミー入力を要求する。</summary>
public sealed class WinReleaseTests
{
    /// <summary>繰り返しの実行可否によらず、一回の Win 押下につき一度だけ送る。</summary>
    [Theory]
    [InlineData(0x5Bu, true)]
    [InlineData(0x5Cu, true)]
    [InlineData(0x5Bu, false)]
    [InlineData(0x5Cu, false)]
    public void RepeatedSuppressionRequestsDummyOnlyOnceAndPassesWinRelease(uint win, bool allowRepeat)
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25, AllowRepeat: allowRepeat)]);
        matcher.ProcessKey(win, true);
        for (var i = 0; i < 20; i++)
        {
            var down = matcher.ProcessKey(0x25, true);
            Assert.True(down.Suppress);
            Assert.False(down.SendDummyKey);
        }
        Assert.True(matcher.ProcessKey(0x25, false).Suppress);
        Assert.Equal(new HotkeyMatch(false, 0, false, true), matcher.ProcessKey(win, false));
        Assert.False(matcher.ProcessKey(win, false).SendDummyKey);
        matcher.ProcessKey(win, true);
        Assert.False(matcher.ProcessKey(win, false).SendDummyKey);
    }

    /// <summary>先に離した Win では送らず、最後の Win 解放を OS に渡す。</summary>
    [Theory]
    [InlineData(0x5Bu, 0x5Cu)]
    [InlineData(0x5Cu, 0x5Bu)]
    public void BothWinKeysMustBeReleased(uint first, uint second)
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(first, true);
        matcher.ProcessKey(second, true);
        matcher.ProcessKey(0x25, true);
        Assert.False(matcher.ProcessKey(first, false).SendDummyKey);
        Assert.True(matcher.ProcessKey(second, false).SendDummyKey);
    }

    /// <summary>矢印以外と複数修飾も対象にし、握りつぶさなければ要求しない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnySuppressedWinBindingRequestsDummyAfterOtherModifiersAreReleased(bool suppress)
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(7, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, 0x41, suppress)]);
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA4, true);
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x41, true).Matched);
        matcher.ProcessKey(0xA2, false);
        matcher.ProcessKey(0xA4, false);
        Assert.Equal(suppress, matcher.ProcessKey(0x5B, false).SendDummyKey);
    }

    /// <summary>通常のシェル操作には余分な入力を送らない。</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(0x44u)]
    [InlineData(0x4Cu)]
    public void WinAloneAndUnmatchedShortcutsDoNotRequestDummy(uint key)
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(0x5B, true);
        if (key != 0)
        {
            Assert.False(matcher.ProcessKey(key, true).Suppress);
            matcher.ProcessKey(key, false);
        }
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
    }

    /// <summary>次のフック起動へ前回の押下状態を持ち越さない。</summary>
    [Fact]
    public void ResetClearsPendingDummyRequest()
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(0x5B, true);
        matcher.ProcessKey(0x25, true);
        matcher.Reset();
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
    }

    /// <summary>途中で取り込みや一時停止へ変わっても、既に奪った入力の副作用を抑える。</summary>
    [Fact]
    public void RebindingAndCaptureDoNotLosePreviouslyRequiredDummy()
    {
        var capturing = false;
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        matcher.ProcessKey(0x5B, true);
        matcher.ProcessKey(0x25, true);
        matcher.SetBindings([]);
        capturing = true;
        Assert.Equal(new HotkeyMatch(false, 0, false, true), matcher.ProcessKey(0x5B, false));
    }

    /// <summary>送信失敗後もフックを使え、注入入力で物理キー状態を壊さない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SenderFailureDoesNotSuppressWinReleaseAndInjectedInputDoesNotReenterMatcher(bool success)
    {
        var matcher = CreateMatcher();
        var sender = new FakeDummySender(success);
        // Start を呼ばないので、実際のフックや API は使わない。
        using var hooks = new HookThread(matcher, 8, new UnusedHookApi(), sender);
        hooks.ProcessKey(0x5B, true);
        Assert.False(hooks.ProcessKey(0x25, true, injected: true));
        Assert.False(hooks.ProcessKey(0x5B, false));
        Assert.Equal(0, sender.Calls);
        hooks.ProcessKey(0x5B, true);
        Assert.True(hooks.ProcessKey(0x25, true));
        // 注入した Win 解放は、物理キーの修飾状態も消さない。
        Assert.False(hooks.ProcessKey(0x5B, false, injected: true));
        Assert.True(hooks.ProcessKey(0x25, true));
        Assert.False(hooks.ProcessKey(0x5B, false));
        Assert.Equal(1, sender.Calls);
        Assert.False(hooks.ProcessKey(0x5B, false));
        Assert.Equal(1, sender.Calls);
        Assert.Null(hooks.Failure);
    }

    private static HotkeyMatcher CreateMatcher()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        return matcher;
    }

    private sealed class FakeDummySender(bool success) : IDummyKeySender
    {
        internal int Calls;
        public bool Send() { Calls++; return success; }
    }

    private sealed class UnusedHookApi : IHookApi
    {
        public uint GetThreadId() => throw new InvalidOperationException();
        public void CreateMessageQueue() => throw new InvalidOperationException();
        public nint RegisterEvent(uint eventId, nint callback) => throw new InvalidOperationException();
        public nint RegisterMouse(nint callback) => throw new InvalidOperationException();
        public nint RegisterKeyboard(nint callback) => throw new InvalidOperationException();
        public bool UnregisterEvent(nint hook) => throw new InvalidOperationException();
        public bool UnregisterLowLevel(nint hook) => throw new InvalidOperationException();
        public int ReadAndDispatchMessage() => throw new InvalidOperationException();
        public bool PostQuit(uint threadId) => throw new InvalidOperationException();
    }
}
