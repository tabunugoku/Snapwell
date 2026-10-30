using System.Text.RegularExpressions;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Localization;

/// <summary>共有する表示言語の変更を直列にする。</summary>
[CollectionDefinition("SetupWizardLocalization", DisableParallelization = true)]
public sealed class SetupWizardLocalizationCollection;

/// <summary>保存の判断を変えず、案内とキー図を言語に合わせることを確かめる。</summary>
[Collection("SetupWizardLocalization")]
public sealed class SetupWizardLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>ほかのテストの表示言語を保つ。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>図の位置・省略・繰り返しと未知の名前を安全に扱う。</summary>
    [Theory]
    [InlineData("{keys} after", "[keys] after")]
    [InlineData("before {keys} after", "before [keys] after")]
    [InlineData("before {keys}", "before [keys]")]
    [InlineData("no keys", "no keys")]
    [InlineData("{keys}{keys}", "[keys][keys]")]
    [InlineData("{shift} + {keys}", "[shift] + [keys]")]
    [InlineData("{unknown} {keys} {", "{unknown} [keys] {")]
    [InlineData("", "")]
    public void KeyTemplatesPreserveTextAndPlaceholderOrder(string template, string expected)
    {
        var parts = SetupWizard.SplitKeyTemplate(template, "keys", "shift");
        Assert.Equal(expected, string.Concat(parts.Select(part =>
            part.Placeholder is { } name ? $"[{name}]" : part.Text)));
        Assert.All(parts, part => Assert.True(part.Placeholder is null ? part.Text.Length > 0 : part.Text.Length == 0));
    }

    /// <summary>全ページ・状態・ボタンの静的参照に、日本語と英語を用意する。</summary>
    [Fact]
    public void EveryWizardReferenceHasJapaneseAndEnglishText()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/Setup");
        var sources = string.Join("\n", Directory.EnumerateFiles(folder, "SetupWindow*.cs").Select(File.ReadAllText));
        sources += File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Core/Settings/SetupWizard.cs"));
        var references = Regex.Matches(sources, "UiText\\.Current\\.(?:Get|Format)\\(\\s*\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).Append("Setup_Back.Content").ToArray();
        Assert.True(references.Length > 40);
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            var catalog = TextCatalog.Load(language);
            foreach (var key in references)
            {
                Assert.Contains(key, catalog.Keys);
                Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)));
            }
        }
    }

    /// <summary>一度案を表示したあとでも、次の表示は切り替えた言語を使う。</summary>
    [Fact]
    public void PlansAndTemplatesFollowCurrentLanguage()
    {
        UiText.Use(UiLanguage.Japanese);
        Assert.Equal(["おすすめ。半分・四分割を作れます", "1/3 や 2/3 も作れます", "大きな区画で手早く"],
            SetupWizard.Plans.Select(plan => plan.Description));
        var japanese = UiText.Current.Get("Setup.Welcome.LauncherTemplate");
        Assert.StartsWith("{keys}、", japanese);
        UiText.Use(UiLanguage.English);
        Assert.Equal("Recommended. Halves and quarters.", SetupWizard.Plans[0].Description);
        var english = UiText.Current.Get("Setup.Welcome.LauncherTemplate");
        Assert.StartsWith("Open the launcher with {keys}", english);
        Assert.Equal("Open the launcher with ", SetupWizard.SplitKeyTemplate(english, "keys")[0].Text);
    }

    /// <summary>保存例外の機密な Message を表示せず、種類ごとに翻訳する。</summary>
    [Theory]
    [InlineData(false, "Couldn't save settings. Check the file location and permissions, then try again.")]
    [InlineData(true, "These settings are read-only. Update Snapwell and reopen setup to save.")]
    public void SaveFailuresUseLocalizedMessagesWithoutExceptionDetails(bool future, string expected)
    {
        UiText.Use(UiLanguage.English);
        var wizard = new SetupWizard(new FailingStore<PortableSettings>(new(), future),
            new FailingStore<DeviceState>(new(), future));
        Assert.True(wizard.TryNext(true, out _));
        Assert.False(wizard.TryNext(true, out var error));
        Assert.Equal(expected, error);
        Assert.Equal(SetupStep.StandardFeatures, wizard.Step);
        Assert.False(wizard.IsCompleted);
    }

    /// <summary>日本語の保存失敗は移行前と同じ文を返す。</summary>
    [Theory]
    [InlineData(false, "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。")]
    [InlineData(true, "この設定は読み取り専用のため、保存できません。Snapwell を更新して開き直してください。")]
    public void JapaneseSaveErrorsKeepTheirOriginalText(bool future, string expected)
    {
        UiText.Use(UiLanguage.Japanese);
        var wizard = new SetupWizard(new FailingStore<PortableSettings>(new(), future),
            new FailingStore<DeviceState>(new(), future));
        Assert.True(wizard.TryNext(true, out _));
        Assert.False(wizard.TryNext(true, out var error));
        Assert.Equal(expected, error);
    }

    private sealed class FailingStore<T>(T value, bool future) : ISettingsStore<T>
    {
        public SettingsLoadResult<T> Load() => new(value, IsReadOnly: future);
        public SettingsLoadResult<T>? TryReload() => Load();
        public void Save(T settings) => throw new IOException("private path");
    }
}
