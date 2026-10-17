using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;

namespace Snapwell.Settings.Dialogs;

/// <summary>取り込みの差分を一行で表示する値。</summary>
public sealed record ImportDifferenceRow(SettingsTransferKey Key, string Name, string Change)
{
    /// <summary>読み上げ用に設定名と変更内容を返す。</summary>
    public override string ToString() => $"{Name}: {Change}";
}

/// <summary>差分と取り込む項目を確認するダイアログ。</summary>
public sealed partial class ImportDialog : ContentDialog
{
    /// <summary>Core が返した差分だけを表示し、取り込みの判断を行わない。</summary>
    public ImportDialog(IReadOnlyList<SettingsDifference> differences)
    {
        InitializeComponent();
        // WinUI 2.3.9 では .NET のコレクションを ItemsSource に渡すと E_INVALIDARG になるため、Items に追加する。
        DifferenceList.Items.Clear();
        foreach (var difference in differences)
        {
            DifferenceList.Items.Add(new ImportDifferenceRow(
                difference.Key, difference.Name, $"{difference.CurrentValue} → {difference.ImportedValue}"));
        }
    }

    public IReadOnlyList<SettingsTransferKey> SelectedKeys => DifferenceList.SelectedItems
        .OfType<ImportDifferenceRow>().Select(row => row.Key).ToArray();

    private void Difference_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsSecondaryButtonEnabled = DifferenceList.SelectedItems.Count > 0;
}
