using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;

namespace Snapwell.Settings.Dialogs;

/// <summary>取り込みの差分を一行で表示する値。</summary>
public sealed record ImportDifferenceRow(SettingsTransferKey Key, string Name, string Change);

/// <summary>差分と取り込む項目を確認するダイアログ。</summary>
public sealed partial class ImportDialog : ContentDialog
{
    /// <summary>Core が返した差分だけを表示し、取り込みの判断を行わない。</summary>
    public ImportDialog(IReadOnlyList<SettingsDifference> differences)
    {
        InitializeComponent();
        DifferenceList.ItemsSource = differences.Select(difference => new ImportDifferenceRow(
            difference.Key, difference.Name, $"{difference.CurrentValue} → {difference.ImportedValue}")).ToArray();
    }

    public IReadOnlyList<SettingsTransferKey> SelectedKeys => DifferenceList.SelectedItems
        .OfType<ImportDifferenceRow>().Select(row => row.Key).ToArray();

    private void Difference_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsSecondaryButtonEnabled = DifferenceList.SelectedItems.Count > 0;
}
