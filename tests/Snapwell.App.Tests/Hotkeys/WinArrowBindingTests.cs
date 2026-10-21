using Snapwell.App.Hotkeys;
using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Tests.Hotkeys;

/// <summary>置き換えの追加と、通常・一時停止・ランチャー間の切り替えを確かめる。</summary>
public sealed class WinArrowBindingTests
{
    /// <summary>通常のキーを残したまま、追加する半分操作だけを繰り返せる。</summary>
    [Fact]
    public void ReplacementAddsFourRepeatableBindingsWithoutChangingDefaults()
    {
        var hotkeys = DefaultHotkeys.Create();
        var normal = HotkeyBindingsBuilder.Build(hotkeys);
        var replaced = HotkeyBindingsBuilder.Build(hotkeys, true);
        Assert.Equal(normal.Bindings.Count + 4, replaced.Bindings.Count);
        Assert.Empty(replaced.Dropped);
        Assert.All(replaced.Bindings.Where(binding => binding.Modifiers == KeyModifiers.Win), binding =>
        {
            Assert.True(binding.AllowRepeat);
            Assert.True(binding.Suppress);
        });
        Assert.Equal(normal.Bindings, HotkeyBindingsBuilder.Build(hotkeys, false).Bindings);
    }

    /// <summary>同じキーは利用者の操作を優先し、修飾が違う操作とは共存する。</summary>
    [Fact]
    public void UserBindingWinsAndAdditionalModifiersRemainSeparate()
    {
        var hotkeys = DefaultHotkeys.Create();
        hotkeys[WindowCommand.Maximize] = new(ChordModifiers.Win, 0x25);
        hotkeys[WindowCommand.GrowRight] = new(ChordModifiers.Control | ChordModifiers.Win, 0x27);
        var replaced = HotkeyBindingsBuilder.Build(hotkeys, true);
        var left = Assert.Single(replaced.Bindings, binding =>
            binding.Modifiers == KeyModifiers.Win && binding.VirtualKey == 0x25);
        Assert.Equal((int)WindowCommand.Maximize, left.Id);
        Assert.Equal(hotkeys.Count + 3, replaced.Bindings.Count);
        Assert.Empty(replaced.Dropped);
    }

    /// <summary>一時停止とランチャーの入力を保ち、通常に戻った時点の選択を使う。</summary>
    [Fact]
    public void UpdaterReadsLatestChoiceOnlyForNormalBindings()
    {
        var normal = HotkeyBindingsBuilder.Build(DefaultHotkeys.Create()).Bindings;
        var paused = HotkeyBindingsBuilder.Paused(normal);
        var launcher = HotkeyBindingsBuilder.Launcher(normal);
        var replace = false;
        IReadOnlyList<HotkeyBinding> current = [];
        var apply = HotkeyBindingsBuilder.CreateUpdater(normal, () => replace, value => current = value);
        apply(normal);
        Assert.Same(normal, current);
        replace = true;
        apply(normal);
        Assert.Equal(normal.Count + 4, current.Count);
        apply(paused);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(current).Id);
        apply(launcher);
        Assert.Same(launcher, current);
        Assert.DoesNotContain(current, binding => binding.Modifiers == KeyModifiers.Win);
        apply(normal);
        Assert.Equal(normal.Count + 4, current.Count);
        replace = false;
        apply(normal);
        Assert.Same(normal, current);
    }
}
