namespace Snapwell.Core.Commands;

/// <summary>端末の選択で追加する、Win と矢印の固定の割り当て。</summary>
public static class WinArrowBindings
{
    private static readonly KeyValuePair<WindowCommand, KeyChordSpec>[] _bindings =
    [
        new(WindowCommand.LeftHalf, new(ChordModifiers.Win, 0x25)),
        new(WindowCommand.RightHalf, new(ChordModifiers.Win, 0x27)),
        new(WindowCommand.TopHalf, new(ChordModifiers.Win, 0x26)),
        new(WindowCommand.BottomHalf, new(ChordModifiers.Win, 0x28)),
    ];

    /// <summary>利用者が指定したキーの組を優先し、重ならない固定の割り当てを返す。</summary>
    public static IReadOnlyList<KeyValuePair<WindowCommand, KeyChordSpec>> WithoutConflicts(
        IEnumerable<KeyChordSpec> userChords)
    {
        var occupied = userChords.ToHashSet();
        return [.. _bindings.Where(binding => !occupied.Contains(binding.Value))];
    }
}
