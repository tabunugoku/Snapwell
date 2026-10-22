using Snapwell.App.Lifecycle;
using Snapwell.Core.Localization;

namespace Snapwell.App.Tests.Localization;

/// <summary>OS に触れず、同意の本文と結果の扱いを確かめる。</summary>
[Collection("UI language")]
public sealed class ConsentLanguageTests
{
    /// <summary>表示のたびに本文を翻訳し、拒否を同意に変えない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptTranslatesAtDisplayTimeAndPreservesAnswer(bool answer)
    {
        var previous = UiText.Current.Language;
        try
        {
            string? body = null;
            string? title = null;
            var prompt = new WindowsConsentPrompt((message, caption) =>
            {
                body = message;
                title = caption;
                return answer;
            });
            UiText.Use(UiLanguage.Japanese);
            Assert.Equal(answer, prompt.ConfirmStandardSnapChange());
            Assert.Equal("Windows の設定アプリの「ウィンドウのスナップ」を、Snapwell が有効な間だけ停止します。\n" +
                "Snapwell を無効にしたとき・終了したとき・異常終了のあとに起動したときに、変更前の設定に戻します。\n\n" +
                "停止してよいですか？", body);
            Assert.Equal("Snapwell — Windows の設定変更", title);
            UiText.Use(UiLanguage.English);
            Assert.Equal(answer, prompt.ConfirmStandardSnapChange());
            Assert.Contains("Choose Yes", body);
            Assert.Contains("or No", body);
            Assert.Equal("Snapwell — Change Windows settings", title);
        }
        finally { UiText.Use(previous); }
    }
}
