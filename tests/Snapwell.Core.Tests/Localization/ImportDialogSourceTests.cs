using System.Xml.Linq;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class ImportDialogSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Dialogs/ImportDialog.xaml",
            "src/Snapwell.Settings/Dialogs/ImportDialog.xaml.cs",
            "src/Snapwell.Core/Settings/SettingsTransfer.cs");
    }

    /// <summary>全体・一部・取り消しの選択を、日英の文言で区別する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "取り込む設定の確認", "全体を置き換える", "選んだ項目だけ", "取り消す")]
    [InlineData(UiLanguage.English, "Review settings to import", "Replace all", "Selected items only", "Cancel")]
    public void ImportChoicesUseCatalogText(UiLanguage language, string title, string all, string selected, string close)
    {
        var root = XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(),
            "src/Snapwell.Settings/Dialogs/ImportDialog.xaml")).Root!;
        XNamespace localization = "using:Snapwell.Settings.Localization";
        var uid = root.Attribute(localization + "Loc.Uid")!.Value;
        var catalog = TextCatalog.Load(language);
        Assert.Equal(title, catalog.Get(uid + ".Title"));
        Assert.Equal(all, catalog.Get(uid + ".PrimaryButtonText"));
        Assert.Equal(selected, catalog.Get(uid + ".SecondaryButtonText"));
        Assert.Equal(close, catalog.Get(uid + ".CloseButtonText"));
    }
}
