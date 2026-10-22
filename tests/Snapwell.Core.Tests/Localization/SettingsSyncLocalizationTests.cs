using Snapwell.Core.Localization;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Localization;

/// <summary>表示言語の切り替えをほかのコレクションと並列に実行しない。</summary>
[CollectionDefinition("SettingsSyncLocalization", DisableParallelization = true)]
public sealed class SettingsSyncLocalizationCollection;

/// <summary>同期の状態と拒否理由だけが言語に従うことを確かめる。</summary>
[Collection("SettingsSyncLocalization")]
public sealed class SettingsSyncLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>共有する言語をテストの前へ戻す。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>すべての結果の値に、各言語自身のキーがある。</summary>
    [Fact]
    public void EverySyncKindHasJapaneseAndEnglishKeys()
    {
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            var catalog = TextCatalog.Load(language);
            UiText.Use(language);
            foreach (var kind in Enum.GetValues<SettingsSyncKind>())
            {
                var key = $"Backup.Sync.{kind}";
                Assert.Contains(key, catalog.Keys);
                Assert.Equal(catalog.Format(key, ("message", "{path}")),
                    new SettingsSyncResult(kind, "{path}").ToDisplayText());
            }
        }
    }

    /// <summary>表示は現在の言語を使い、任意の理由やパスの中身を書き換えない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "変更なし", "書き出し済み", "取り込み済み", "見送り: ", "失敗: ")]
    [InlineData(UiLanguage.English, "No changes", "Exported", "Imported", "Skipped: ", "Failed: ")]
    public void StatusAndReasonUseLocalizedTemplates(UiLanguage language, string unchanged, string pushed,
        string pulled, string skipped, string failed)
    {
        UiText.Use(language);
        const string message = @"C:\user data\{count}\settings.json";
        Assert.Equal(unchanged, new SettingsSyncResult(SettingsSyncKind.NoChange).ToDisplayText());
        Assert.Equal(pushed, new SettingsSyncResult(SettingsSyncKind.Pushed).ToDisplayText());
        Assert.Equal(pulled, new SettingsSyncResult(SettingsSyncKind.Pulled).ToDisplayText());
        Assert.Equal(skipped + message, new SettingsSyncResult(SettingsSyncKind.Skipped, message).ToDisplayText());
        Assert.Equal(failed + message, new SettingsSyncResult(SettingsSyncKind.Failed, message).ToDisplayText());
        Assert.Equal(failed + message, new SettingsSyncResult(SettingsSyncKind.Rejected, message).ToDisplayText());
        Assert.Equal(skipped, new SettingsSyncResult(SettingsSyncKind.Skipped).ToDisplayText());
    }

    /// <summary>フォルダーの判断は同じまま、重なりと無効なパスの理由を翻訳する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "設定ファイルと同じフォルダー、その中、または親フォルダーは選べません。", "同期フォルダーのパスが正しくありません。")]
    [InlineData(UiLanguage.English, "Choose a folder outside the settings folder, its subfolders, and its parents.", "The sync folder path is invalid.")]
    public void FolderValidationPreservesBoundaries(UiLanguage language, string overlap, string invalid)
    {
        UiText.Use(language);
        var root = Path.Combine(Path.GetTempPath(), "Snapwell-localization");
        var folder = Path.Combine(root, "local");
        var local = Path.Combine(folder, "settings.json");
        Assert.Equal(overlap, SettingsSync.ValidateFolder(local, folder));
        Assert.Equal(overlap, SettingsSync.ValidateFolder(local, Path.Combine(folder, "child")));
        Assert.Equal(overlap, SettingsSync.ValidateFolder(local, root));
        Assert.Equal(invalid, SettingsSync.ValidateFolder(local, "\0"));
        Assert.Null(SettingsSync.ValidateFolder(local, Path.Combine(root, "local-other")));
    }
}
