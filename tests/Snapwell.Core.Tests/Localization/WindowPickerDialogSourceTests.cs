using System.Xml.Linq;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class WindowPickerDialogSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Dialogs/WindowPickerDialog.xaml",
            "src/Snapwell.Settings/Dialogs/WindowPickerDialog.xaml.cs");
    }

    /// <summary>選択画面のタイトルと取り消しボタンを、日英のカタログから表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "ウィンドウを選んで追加", "取り消す")]
    [InlineData(UiLanguage.English, "Add from window", "Cancel")]
    public void PickerTitleAndCloseButtonAreLocalized(UiLanguage language, string title, string close)
    {
        var root = XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(),
            "src/Snapwell.Settings/Dialogs/WindowPickerDialog.xaml")).Root!;
        XNamespace localization = "using:Snapwell.Settings.Localization";
        var uid = root.Attribute(localization + "Loc.Uid")!.Value;
        var catalog = TextCatalog.Load(language);
        Assert.Equal(title, catalog.Get(uid + ".Title"));
        Assert.Equal(close, catalog.Get(uid + ".CloseButtonText"));
        Assert.Equal(language == UiLanguage.Japanese ? "title、user.exe、UserClass" : "title, user.exe, UserClass",
            catalog.Format("Dialogs.Picker.WindowName", ("title", "title"), ("process", "user.exe"), ("class", "UserClass")));
    }
}
