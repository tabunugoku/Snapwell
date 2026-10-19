using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Snapwell.Platform.Hooks;

/// <summary>左右を区別しないホットキーの修飾キー。</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>ID・修飾キー・仮想キーコードと握りつぶしの指定。</summary>
public readonly record struct HotkeyBinding(int Id, KeyModifiers Modifiers, uint VirtualKey, bool Suppress = true,
    bool AllowRepeat = true);

/// <summary>割り当てを伴わないキー判定の結果。</summary>
public readonly record struct HotkeyMatch(bool Matched, int Id, bool Suppress);

/// <summary>単一の入力スレッドでキー状態を追い、設定だけを別スレッドから差し替える。</summary>
public sealed class HotkeyMatcher
{
    private HotkeyBinding[] _bindings = [];
    private readonly bool[] _suppressedKeys = new bool[256];
    private readonly bool[] _downKeys = new bool[256];
    private uint _pressedModifiers;

    /// <summary>列挙元から独立した不変の設定スナップショットを公開する。</summary>
    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        var snapshot = bindings.ToArray();
        foreach (var binding in snapshot)
        {
            if (binding.VirtualKey > 255 || (binding.Modifiers & ~AllModifiers) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bindings));
            }
        }

        Volatile.Write(ref _bindings, snapshot);
    }

    private const KeyModifiers AllModifiers =
        KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win;

    /// <summary>割り当て・ロックなしで判定し、握りつぶしたキーの解放も対にする。</summary>
    public HotkeyMatch ProcessKey(uint virtualKey, bool isDown)
    {
        var modifierBit = GetModifierBit(virtualKey);
        if (modifierBit != 0)
        {
            _pressedModifiers = isDown ? _pressedModifiers | modifierBit : _pressedModifiers & ~modifierBit;
        }

        if (virtualKey > 255)
        {
            return default;
        }

        if (!isDown)
        {
            var suppress = _suppressedKeys[virtualKey];
            _suppressedKeys[virtualKey] = false;
            _downKeys[virtualKey] = false;
            return new HotkeyMatch(false, 0, suppress);
        }

        var modifiers = GetModifiers();
        var repeated = _downKeys[virtualKey];
        _downKeys[virtualKey] = true;
        var snapshot = Volatile.Read(ref _bindings);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var binding = snapshot[index];
            if (binding.VirtualKey == virtualKey && binding.Modifiers == modifiers)
            {
                if (repeated && !binding.AllowRepeat)
                {
                    // 押しっぱなしの繰り返しは実行せず、キーだけ握りつぶす。
                    _suppressedKeys[virtualKey] |= binding.Suppress;
                    return new HotkeyMatch(false, 0, _suppressedKeys[virtualKey]);
                }

                _suppressedKeys[virtualKey] |= binding.Suppress;
                // Win の解放を握りつぶす場合は、その前に VK_NONAME (0xFC) などを送る必要がある。
                // スタートメニューが開く副作用の抑止は実機未確認。ダミーキーの送信は今回は実装しない。
                return new HotkeyMatch(true, binding.Id, binding.Suppress || _suppressedKeys[virtualKey]);
            }
        }

        return new HotkeyMatch(false, 0, _suppressedKeys[virtualKey]);
    }

    /// <summary>入力スレッドの起動前に押下状態を初期化する。</summary>
    public void Reset()
    {
        _pressedModifiers = 0;
        Array.Clear(_suppressedKeys);
        Array.Clear(_downKeys);
    }

    private KeyModifiers GetModifiers()
    {
        var result = KeyModifiers.None;
        if ((_pressedModifiers & 0b0000000111) != 0)
        {
            result |= KeyModifiers.Control;
        }

        if ((_pressedModifiers & 0b0000111000) != 0)
        {
            result |= KeyModifiers.Alt;
        }

        if ((_pressedModifiers & 0b0111000000) != 0)
        {
            result |= KeyModifiers.Shift;
        }

        if ((_pressedModifiers & 0b11000000000) != 0)
        {
            result |= KeyModifiers.Win;
        }

        return result;
    }

    private static uint GetModifierBit(uint key) => key switch
    {
        (uint)VIRTUAL_KEY.VK_CONTROL => 1u << 0,
        (uint)VIRTUAL_KEY.VK_LCONTROL => 1u << 1,
        (uint)VIRTUAL_KEY.VK_RCONTROL => 1u << 2,
        (uint)VIRTUAL_KEY.VK_MENU => 1u << 3,
        (uint)VIRTUAL_KEY.VK_LMENU => 1u << 4,
        (uint)VIRTUAL_KEY.VK_RMENU => 1u << 5,
        (uint)VIRTUAL_KEY.VK_SHIFT => 1u << 6,
        (uint)VIRTUAL_KEY.VK_LSHIFT => 1u << 7,
        (uint)VIRTUAL_KEY.VK_RSHIFT => 1u << 8,
        (uint)VIRTUAL_KEY.VK_LWIN => 1u << 9,
        (uint)VIRTUAL_KEY.VK_RWIN => 1u << 10,
        _ => 0,
    };
}
