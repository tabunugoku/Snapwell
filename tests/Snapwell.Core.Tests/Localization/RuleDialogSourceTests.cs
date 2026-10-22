using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class RuleDialogSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Dialogs/RuleDialog.xaml",
            "src/Snapwell.Settings/Dialogs/RuleDialog.xaml.cs");
    }

    /// <summary>追加と編集のタイトルと主要ボタンを、両言語で区別する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "ルールを追加", "ルールを編集", "追加", "保存")]
    [InlineData(UiLanguage.English, "Add rule", "Edit rule", "Add", "Save")]
    public void AddAndEditUseLocalizedTitlesAndButtons(UiLanguage language,
        string addTitle, string editTitle, string addButton, string saveButton)
    {
        var source = File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(),
            "src/Snapwell.Settings/Dialogs/RuleDialog.xaml.cs"));
        var catalog = TextCatalog.Load(language);
        foreach (var (property, add, edit) in new[]
        {
            ("Title", addTitle, editTitle), ("PrimaryButtonText", addButton, saveButton),
        })
        {
            var match = Regex.Match(source, property + " = rule is null \\? UiText\\.Current\\.Get\\(\"([^\"]+)\"\\)"
                + " : UiText\\.Current\\.Get\\(\"([^\"]+)\"\\)");
            Assert.True(match.Success);
            Assert.Equal(add, catalog.Get(match.Groups[1].Value));
            Assert.Equal(edit, catalog.Get(match.Groups[2].Value));
        }
        Assert.Equal(language == UiLanguage.Japanese ? "取り消す" : "Cancel", catalog.Get("Rule_Dialog.CloseButtonText"));
    }
}
