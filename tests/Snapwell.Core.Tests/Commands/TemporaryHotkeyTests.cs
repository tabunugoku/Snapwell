using Snapwell.Core.Commands;

namespace Snapwell.Core.Tests.Commands;

/// <summary>一時キーが既存のキーを上書きしないことを検証する。</summary>
public sealed class TemporaryHotkeyTests
{
    /// <summary>修飾キーと仮想キーの両方が同じときだけ一時キーを外す。</summary>
    [Theory]
    [InlineData(ChordModifiers.Control, 0x31u, 1)]
    [InlineData(ChordModifiers.Alt, 0x31u, 2)]
    [InlineData(ChordModifiers.Control | ChordModifiers.Shift, 0x31u, 2)]
    [InlineData(ChordModifiers.Control, 0x32u, 1)]
    [InlineData(ChordModifiers.Control, 0x33u, 2)]
    public void OnlyExactExistingChordsAreExcluded(ChordModifiers modifiers, uint key, int expectedCount)
    {
        KeyChordSpec[] temporary = [new(ChordModifiers.Control, 0x31), new(ChordModifiers.Control, 0x32)];
        var existing = new KeyChordSpec(modifiers, key);
        var result = HotkeyCatalog.AvailableTemporaryChords(temporary, [existing]);
        Assert.Equal(expectedCount, result.Count);
        Assert.DoesNotContain(existing, result);
        Assert.Equal(temporary.Where(chord => chord != existing), result);
    }

    /// <summary>既存キーがなければ順序を保ってすべての一時キーを残す。</summary>
    [Fact]
    public void NoExistingChordsPreservesAllTemporaryChords()
    {
        KeyChordSpec[] temporary = [new(ChordModifiers.Control, 0x31), new(ChordModifiers.Control, 0x32)];
        Assert.Equal(temporary, HotkeyCatalog.AvailableTemporaryChords(temporary, []));
    }
}
