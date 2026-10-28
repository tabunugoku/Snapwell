using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>低レベルフックで使う純粋なキー状態の判定を確認する。</summary>
public sealed class HotkeyMatcherTests
{
    /// <summary>左・右・汎用 Ctrl のいずれも Alt と合わせて判定する。</summary>
    [Theory]
    [InlineData(0x11u)]
    [InlineData(0xA2u)]
    [InlineData(0xA3u)]
    public void CtrlAltSpaceRequiresBothModifiers(uint control)
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(control, true);
        Assert.False(matcher.ProcessKey(0x20, true).Matched);
        matcher.ProcessKey(0xA4, true);
        Assert.Equal(new HotkeyMatch(true, 42, true), matcher.ProcessKey(0x20, true));
        Assert.Equal(new HotkeyMatch(false, 0, true), matcher.ProcessKey(0x20, false));
        matcher.ProcessKey(control, false);
        Assert.False(matcher.ProcessKey(0x20, true).Matched);
    }

    /// <summary>左右を同時に押したときは片方の解放だけで修飾状態を失わない。</summary>
    [Fact]
    public void ReleasingOneSidePreservesOtherModifierAndResetClearsState()
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA3, true);
        matcher.ProcessKey(0xA5, true);
        matcher.ProcessKey(0xA2, false);
        Assert.True(matcher.ProcessKey(0x20, true).Matched);
        matcher.ProcessKey(0x20, false);
        matcher.ProcessKey(0xA5, false);
        Assert.False(matcher.ProcessKey(0x20, true).Matched);
        matcher.ProcessKey(0xA5, true);
        matcher.Reset();
        Assert.False(matcher.ProcessKey(0x20, true).Matched);
    }

    /// <summary>Shift と左右 Win も修飾キーとして区別し、余分な修飾キーでは一致しない。</summary>
    [Theory]
    [InlineData(0xA0u, KeyModifiers.Shift)]
    [InlineData(0xA1u, KeyModifiers.Shift)]
    [InlineData(0x5Bu, KeyModifiers.Win)]
    [InlineData(0x5Cu, KeyModifiers.Win)]
    public void ShiftAndWinAreMatchedExactly(uint key, KeyModifiers modifier)
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(1, modifier, 0x41, false)]);
        matcher.ProcessKey(key, true);
        Assert.Equal(new HotkeyMatch(true, 1, false), matcher.ProcessKey(0x41, true));
        matcher.ProcessKey(0xA2, true);
        Assert.False(matcher.ProcessKey(0x41, true).Matched);
    }

    /// <summary>設定元の配列の変更を入力スレッドへ漏らさず、キー解放まで握りつぶす。</summary>
    [Fact]
    public void SnapshotIsCopiedAndSuppressedKeyUpSurvivesReplacement()
    {
        var bindings = new[] { new HotkeyBinding(1, KeyModifiers.None, 0x41) };
        var matcher = new HotkeyMatcher();
        matcher.SetBindings(bindings);
        bindings[0] = new(2, KeyModifiers.None, 0x42);
        Assert.Equal(1, matcher.ProcessKey(0x41, true).Id);
        matcher.SetBindings([]);
        Assert.True(matcher.ProcessKey(0x41, true).Suppress);
        Assert.True(matcher.ProcessKey(0x41, false).Suppress);
        Assert.False(matcher.ProcessKey(0x41, false).Suppress);
    }

    /// <summary>差し替えは別スレッド、キー状態は単一スレッドという契約を確認する。</summary>
    [Fact]
    public async Task ConcurrentSnapshotReplacementDoesNotInterruptMatching()
    {
        var matcher = CreateMatcher();
        var updating = Task.Run(() =>
        {
            for (var index = 0; index < 10000; index++)
            {
                matcher.SetBindings([new(index, KeyModifiers.Control | KeyModifiers.Alt, 0x20)]);
            }
        });
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA4, true);
        for (var index = 0; index < 10000; index++)
        {
            Assert.True(matcher.ProcessKey(0x20, true).Matched);
            matcher.ProcessKey(0x20, false);
        }

        await updating;
    }

    /// <summary>入力判定の定常経路でマネージド割り当てをしない。</summary>
    [Fact]
    public void MatchingDoesNotAllocate()
    {
        var matcher = CreateMatcher();
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA4, true);
        for (var index = 0; index < 100; index++)
        {
            matcher.ProcessKey(0x20, true);
            matcher.ProcessKey(0x20, false);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            matcher.ProcessKey(0x20, true);
            matcher.ProcessKey(0x20, false);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>OS の押下状態は、フックが今処理しているキーについてはまだ更新されていない。</summary>
    [Fact]
    public void ModifiersAreKeptWhileOsStateLagsBehindTheCurrentKey()
    {
        var down = new HashSet<uint>();
        var matcher = new HotkeyMatcher(isPhysicallyDown: down.Contains);
        matcher.SetBindings([new(42, KeyModifiers.Control | KeyModifiers.Alt, 0x0D)]);
        foreach (var key in new uint[] { 0xA2, 0xA4 })
        {
            matcher.ProcessKey(key, true);
            down.Add(key);
        }

        Assert.Equal(new HotkeyMatch(true, 42, true), matcher.ProcessKey(0x0D, true));
    }

    /// <summary>ロック画面などで解放を取り逃した修飾キーは、次の入力で OS の状態に合わせて捨てる。</summary>
    [Fact]
    public void ModifierReleasedWhileUnobservedIsDroppedOnNextKey()
    {
        var down = new HashSet<uint> { 0xA2 };
        var matcher = new HotkeyMatcher(isPhysicallyDown: down.Contains);
        matcher.SetBindings([new(42, KeyModifiers.Control | KeyModifiers.Alt, 0x0D)]);
        matcher.ProcessKey(0xA2, true);
        matcher.ProcessKey(0xA4, true);
        down.Remove(0xA4);
        Assert.False(matcher.ProcessKey(0x0D, true).Matched);
    }

    private static HotkeyMatcher CreateMatcher()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([new(42, KeyModifiers.Control | KeyModifiers.Alt, 0x20)]);
        return matcher;
    }
}
