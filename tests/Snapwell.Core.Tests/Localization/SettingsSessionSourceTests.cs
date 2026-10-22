using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>保存と同期の通知をカタログへ移し、例外の詳細を画面に漏らさないことを確かめる。</summary>
public sealed class SettingsSessionSourceTests
{
    /// <summary>失敗時の固定文を翻訳し、例外の Message を表示に使わない。</summary>
    [Fact]
    public void SessionNoticesHaveNoJapaneseLiteralsOrRawExceptionMessages()
    {
        const string path = "src/Snapwell.Settings/SettingsSession.cs";
        SourceScan.AssertNoJapaneseLiterals(path);
        var source = File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(), path));
        Assert.DoesNotContain("exception.Message", source);
    }

    /// <summary>既定の日本語の保存完了通知を変更しない。</summary>
    [Fact]
    public void JapaneseSaveNoticeMatchesExistingMessage()
    {
        Assert.Equal("保存しました。Snapwell が起動していれば、すぐに反映されます。",
            TextCatalog.Load(UiLanguage.Japanese).Get("Session.Save.Saved"));
    }
}
