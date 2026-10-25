using Snapwell.App.Lifecycle;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>今回以外の作業フォルダーにある復旧資料の判定を確認する。</summary>
public sealed class UpdateModesTests
{
    /// <summary>保護された場所と利用者の場所のどちらの前回 journal も未解決として扱う。</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData(@"C:\ProgramData\Snapwell\update\current\journal.jsonl", false)]
    [InlineData(@"c:\PROGRAMDATA\Snapwell\update\CURRENT\journal.jsonl", false)]
    [InlineData(@"C:\ProgramData\Snapwell\update\previous\journal.jsonl", true)]
    [InlineData(@"C:\LocalAppData\Snapwell\update\work-previous\journal.jsonl", true)]
    public void DetectsOtherWorkDirectoryJournals(string journal, bool expected)
    {
        string[] journals = journal.Length == 0 ? [] : [journal];
        Assert.Equal(expected, UpdateModes.HasPreviousUpdateUnresolved(
            @"C:\ProgramData\Snapwell\update\current\", journals));
    }

    /// <summary>今回の journal が先頭にあっても、後続する前回の journal を見落とさない。</summary>
    [Fact]
    public void ChecksEveryJournal()
    {
        Assert.True(UpdateModes.HasPreviousUpdateUnresolved(@"C:\update\current",
            [@"C:\update\current\journal.jsonl", @"C:\update\previous\journal.jsonl"]));
    }
}
