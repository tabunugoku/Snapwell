using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>Win を含む操作の最初の握りつぶしで、一度だけダミー入力を要求する。</summary>
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
        Assert.False(matcher.ProcessKey(win, true).SendDummyKey);
        for (var i = 0; i < 20; i++)
        {
            var down = matcher.ProcessKey(0x25, true);
            Assert.True(down.Suppress);
            Assert.Equal(i == 0, down.SendDummyKey);
            Assert.Equal(i == 0 || allowRepeat, down.Matched);
        }
        Assert.True(matcher.ProcessKey(0x25, false).Suppress);
        Assert.Equal(default, matcher.ProcessKey(win, false));
        Assert.False(matcher.ProcessKey(win, false).SendDummyKey);
        matcher.ProcessKey(win, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(win, false).SendDummyKey);
    }

    /// <summary>片方を離して押し直しても記録を保ち、両方を離すと次の要求を許可する。</summary>
    [Theory]
    [InlineData(0x5Bu, 0x5Cu)]
    [InlineData(0x5Cu, 0x5Bu)]
    public void BothWinKeysMustBeReleased(uint first, uint second)
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(first, true);
        matcher.ProcessKey(second, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(first, false).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(first, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.Equal(default, matcher.ProcessKey(second, false));
        Assert.False(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.Equal(default, matcher.ProcessKey(first, false));
        matcher.ProcessKey(0x25, false);
        matcher.ProcessKey(second, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
    }

    /// <summary>矢印以外と複数修飾も対象にし、握りつぶさなければ要求しない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnySuppressedWinBindingRequestsDummyOnKeyDown(bool suppress)
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(7, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, 0x41, suppress)]);
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA4, true);
        matcher.ProcessKey(0x5B, true);
        var match = matcher.ProcessKey(0x41, true);
        Assert.True(match.Matched);
        Assert.Equal(suppress, match.SendDummyKey);
        Assert.False(matcher.ProcessKey(0xA2, false).SendDummyKey);
        Assert.False(matcher.ProcessKey(0xA4, false).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x5B, false).SendDummyKey);
    }

    /// <summary>通常のシェル操作には余分な入力を送らない。</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(0x44u)]
    [InlineData(0x4Cu)]
    public void WinAloneAndUnmatchedShortcutsDoNotRequestDummy(uint key)
    {
        var matcher = CreateMatcher();
        Assert.False(matcher.ProcessKey(0x5B, true).SendDummyKey);
        if (key != 0)
        {
            Assert.Equal(default, matcher.ProcessKey(key, true));
            Assert.Equal(default, matcher.ProcessKey(key, false));
        }
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
    }

    /// <summary>次のフック起動へ前回の押下状態を持ち越さない。</summary>
    [Fact]
    public void ResetClearsDummyRequestRecord()
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
        matcher.Reset();
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
    }

    /// <summary>途中で取り込みや一時停止へ変わっても解放時に再送しない。</summary>
    [Fact]
    public void ReleaseDuringCaptureClearsRecordWithoutResending()
    {
        var capturing = false;
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
        matcher.SetBindings([]);
        capturing = true;
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
        capturing = false;
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        matcher.ProcessKey(0x25, false);
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
    }

    /// <summary>取り込み中や割り当てのない一時停止中は、押下でも解放でも要求しない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureAndPauseDoNotRequestDummy(bool capturing)
    {
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings(capturing ? [new(1, KeyModifiers.Win, 0x25)] : []);
        Assert.Equal(default, matcher.ProcessKey(0x5B, true));
        Assert.Equal(default, matcher.ProcessKey(0x25, true));
        Assert.Equal(default, matcher.ProcessKey(0x25, false));
        Assert.Equal(default, matcher.ProcessKey(0x5B, false));
    }

    /// <summary>取り込んだキーのリピートでは送らず、同じ Win 押下内の次の押下で送る。</summary>
    [Fact]
    public void CapturedKeyDoesNotConsumeRequestForLaterPress()
    {
        var capturing = true;
        var matcher = new HotkeyMatcher(() => capturing);
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        matcher.ProcessKey(0x5B, true);
        Assert.Equal(default, matcher.ProcessKey(0x25, true));
        capturing = false;
        Assert.Equal(default, matcher.ProcessKey(0x25, true));
        Assert.Equal(default, matcher.ProcessKey(0x25, false));
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
    }

    /// <summary>連打と別の矢印も同じ Win 押下につき一度だけ要求する。</summary>
    [Fact]
    public void DifferentArrowsAndRepeatedPressesShareOneRequest()
    {
        var matcher = CreateMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25), new(2, KeyModifiers.Win, 0x27)]);
        matcher.ProcessKey(0x5B, true);
        Assert.True(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x25, false).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x25, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x27, true).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x27, false).SendDummyKey);
        Assert.False(matcher.ProcessKey(0x5B, false).SendDummyKey);
    }

    private static HotkeyMatcher CreateMatcher()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, KeyModifiers.Win, 0x25)]);
        return matcher;
    }
}
