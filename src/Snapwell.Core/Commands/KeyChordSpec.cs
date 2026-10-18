namespace Snapwell.Core.Commands;

/// <summary>OS に依存しない修飾キー。</summary>
[Flags]
public enum ChordModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>修飾キーと零から二百五十五までの仮想キーコード。</summary>
public sealed record KeyChordSpec(ChordModifiers Modifiers, uint VirtualKey);

/// <summary>今回実装した操作の既定のキー割り当て。</summary>
public static class DefaultHotkeys
{
    /// <summary>呼び出し元が編集できる独立した一覧を返す。</summary>
    public static Dictionary<WindowCommand, KeyChordSpec> Create()
    {
        const ChordModifiers plain = ChordModifiers.Control | ChordModifiers.Alt;
        const ChordModifiers shifted = plain | ChordModifiers.Shift;
        // Win 解放の抑止でスタートメニューが開く問題が未解決のため、Win は割り当てない。
        return new()
        {
            [WindowCommand.LeftHalf] = new(plain, 0x25),
            [WindowCommand.RightHalf] = new(plain, 0x27),
            [WindowCommand.TopHalf] = new(plain, 0x26),
            [WindowCommand.BottomHalf] = new(plain, 0x28),
            [WindowCommand.TopLeftQuarter] = new(plain, 0x55),
            [WindowCommand.TopRightQuarter] = new(plain, 0x49),
            [WindowCommand.BottomLeftQuarter] = new(plain, 0x4A),
            [WindowCommand.BottomRightQuarter] = new(plain, 0x4B),
            [WindowCommand.Maximize] = new(plain, 0x0D),
            [WindowCommand.GrowRight] = new(shifted, 0x27),
            [WindowCommand.GrowDown] = new(shifted, 0x28),
            [WindowCommand.ShrinkLeft] = new(shifted, 0x25),
            [WindowCommand.ShrinkUp] = new(shifted, 0x26),
            [WindowCommand.NextMonitor] = new(plain, 0x22),
            [WindowCommand.PreviousMonitor] = new(plain, 0x21),
            [WindowCommand.ToggleEnabled] = new(plain, 0x13),
            [WindowCommand.OpenLauncher] = new(plain, 0x20),
            [WindowCommand.ArrangeGrid] = new(plain, 0x41),
            [WindowCommand.ArrangeColumns] = new(shifted, 0x48),
            [WindowCommand.ArrangeRows] = new(shifted, 0x56),
            [WindowCommand.ArrangeMainAndStack] = new(shifted, 0x4D),
            [WindowCommand.ArrangeAllMonitors] = new(shifted, 0x41),
            [WindowCommand.SnapAdjacentLeft] = new(shifted, 0x4A),
            [WindowCommand.SnapAdjacentRight] = new(shifted, 0x4C),
            [WindowCommand.SnapAdjacentUp] = new(shifted, 0x49),
            [WindowCommand.SnapAdjacentDown] = new(shifted, 0x4B),
        };
    }
}
