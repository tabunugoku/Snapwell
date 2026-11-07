using Snapwell.Core.Hooks;

namespace Snapwell.Core.Tests.Hooks;

/// <summary>フックの証拠に使うキーの選別。</summary>
public sealed class HookKeyClassesTests
{
    [Theory]
    [InlineData(0x41)] // A
    [InlineData(0x30)] // 0
    [InlineData(0x20)] // Space
    [InlineData(0x25)] // 左矢印
    [InlineData(0x0D)] // Enter
    [InlineData(0x08)] // Backspace
    [InlineData(0x70)] // F1
    [InlineData(0xBC)] // ,
    [InlineData(0xE2)] // JIS/ISO の \ キー
    public void OrdinaryKeysRepeat(int virtualKey) => Assert.True(HookKeyClasses.IsAutoRepeating(virtualKey));

    [Theory]
    [InlineData(0x10)] // Shift
    [InlineData(0x11)] // Ctrl
    [InlineData(0x12)] // Alt
    [InlineData(0x14)] // CapsLock
    [InlineData(0x5B)] // 左 Win
    [InlineData(0x90)] // NumLock
    [InlineData(0xA0)] // 左 Shift
    [InlineData(0xE5)] // IME 処理中
    [InlineData(0xF4)] // 半角/全角まわりの OEM キー。押下状態が続くことがある
    [InlineData(0xF0)]
    [InlineData(0xF6)]
    [InlineData(0x01)] // マウスボタン
    [InlineData(0x7C)] // F13 以降
    public void ModifiersImeAndOemKeysAreNotEvidence(int virtualKey) =>
        Assert.False(HookKeyClasses.IsAutoRepeating(virtualKey));
}
