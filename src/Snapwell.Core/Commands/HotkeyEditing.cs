namespace Snapwell.Core.Commands;

/// <summary>同じキーを別の操作が持っていたときの割り当て方。</summary>
public enum AssignMode
{
    /// <summary>重複を残したまま割り当てる。</summary>
    Keep,

    /// <summary>同じキーを持っていた操作へ、割り当てる操作の元のキーを渡す。</summary>
    Swap,
}

/// <summary>設定画面でのホットキーの編集。元の辞書は変えず、新しい辞書を返す。</summary>
public static class HotkeyEditing
{
    private const ChordModifiers AllModifiers =
        ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift | ChordModifiers.Win;

    /// <summary>修飾キーを 1 つ以上含み、修飾キー自体ではないキーの組だけを割り当てられる。</summary>
    public static bool IsAssignable(KeyChordSpec chord) =>
        // 修飾キーなしの割り当てはグローバルなフックが通常の入力を握りつぶすので認める範囲から外す。
        // PortableSettings の検証と同じ条件にして、画面で割り当てたキーが保存時に捨てられないようにする。
        chord.Modifiers != ChordModifiers.None && (chord.Modifiers & ~AllModifiers) == 0 &&
        chord.VirtualKey is >= 1 and <= 255 &&
        chord.VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5));

    /// <summary>指定した操作以外で同じキーを持つ操作を、表示順で返す。</summary>
    public static IReadOnlyList<WindowCommand> FindOthers(IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys,
        WindowCommand command, KeyChordSpec chord) => hotkeys
        .Where(pair => pair.Key != command && pair.Value == chord)
        .Select(pair => pair.Key)
        .OrderBy(HotkeyCatalog.GetDisplayOrder)
        .ToArray();

    /// <summary>操作にキーを割り当てる。入れ替えでは、同じキーを持っていた操作へ元のキーを渡す。</summary>
    public static Dictionary<WindowCommand, KeyChordSpec> Assign(
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys,
        WindowCommand command, KeyChordSpec chord, AssignMode mode)
    {
        var result = hotkeys.ToDictionary();
        if (mode == AssignMode.Swap)
        {
            var hadOriginal = hotkeys.TryGetValue(command, out var original);
            foreach (var other in FindOthers(hotkeys, command, chord))
            {
                // 元のキーが無かった操作を入れ替えると、相手は未割り当てになる。
                if (hadOriginal)
                {
                    result[other] = original!;
                }
                else
                {
                    result.Remove(other);
                }
            }
        }

        result[command] = chord;
        return result;
    }

    /// <summary>操作のキーを外して未割り当てにする。</summary>
    public static Dictionary<WindowCommand, KeyChordSpec> Clear(
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys, WindowCommand command)
    {
        var result = hotkeys.ToDictionary();
        result.Remove(command);
        return result;
    }

    /// <summary>操作のキーを既定へ戻す。既定が無い操作は外す。</summary>
    public static Dictionary<WindowCommand, KeyChordSpec> Reset(
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys, WindowCommand command)
    {
        var result = hotkeys.ToDictionary();
        if (DefaultHotkeys.Create().TryGetValue(command, out var chord))
        {
            result[command] = chord;
        }
        else
        {
            result.Remove(command);
        }

        return result;
    }

    /// <summary>操作のキーが既定と同じか。既定が無い操作は、未割り当てなら既定と同じ。</summary>
    public static bool IsDefault(IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys, WindowCommand command)
    {
        var hasDefault = DefaultHotkeys.Create().TryGetValue(command, out var defaultChord);
        var assigned = hotkeys.TryGetValue(command, out var chord);
        return hasDefault ? assigned && chord == defaultChord : !assigned;
    }
}
