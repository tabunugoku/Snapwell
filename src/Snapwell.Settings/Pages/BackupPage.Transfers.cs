using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;
using Snapwell.Settings.Dialogs;
using Windows.Storage.Pickers;

namespace Snapwell.Settings.Pages;

public sealed partial class BackupPage
{
    partial void ConfigureTransfers(SettingsSession session, nint owner)
    {
        ExportButton.IsEnabled = !session.IsReadOnly;
        ImportButton.IsEnabled = !session.IsReadOnly;
        ExportButton.Click += async (_, _) => await RunTransferAsync(session, () => ExportAsync(session, owner));
        ImportButton.Click += async (_, _) => await RunTransferAsync(session, () => ImportAsync(session, owner));
    }

    private async Task RunTransferAsync(SettingsSession session, Func<Task> action)
    {
        if (session.IsTransferInProgress || session.IsReadOnly)
        {
            return;
        }

        session.IsTransferInProgress = true;
        ExportButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        TransferNotice.IsOpen = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or COMException or NotSupportedException or ArgumentException)
        {
            ShowNotice("ファイルの操作を完了できませんでした。ファイルの場所とアクセス権を確認してください。",
                InfoBarSeverity.Error);
        }
        finally
        {
            session.IsTransferInProgress = false;
            ExportButton.IsEnabled = !session.IsReadOnly;
            ImportButton.IsEnabled = !session.IsReadOnly;
        }
    }

    private async Task ExportAsync(SettingsSession session, nint owner)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "Snapwell-settings",
        };
        picker.FileTypeChoices.Add("JSON ファイル", [".json"]);
        picker.DefaultFileExtension = ".json";
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var path = file.Path;
        var current = session.Current;
        await Task.Run(() => SettingsTransfer.Export(path, current));
        ShowNotice("設定を書き出しました。", InfoBarSeverity.Success);
    }

    private async Task ImportAsync(SettingsSession session, nint owner)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var path = file.Path;
        var current = session.Current;
        var preview = await Task.Run(() => SettingsTransfer.Read(path, current));
        if (!preview.IsSuccess)
        {
            ShowNotice(preview.Error!, InfoBarSeverity.Error);
            return;
        }

        if (preview.Differences.Count == 0)
        {
            ShowNotice("いまの設定との違いはありません。", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ImportDialog(preview.Differences) { XamlRoot = XamlRoot };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None)
        {
            return;
        }

        session.Update(SettingsTransfer.Apply(current, preview,
            choice == ContentDialogResult.Primary ? null : dialog.SelectedKeys));
        ShowNotice("画面に取り込みました。画面下の「保存」で確定してください。", InfoBarSeverity.Success);
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        TransferNotice.Message = message;
        TransferNotice.Severity = severity;
        TransferNotice.IsOpen = true;
    }
}
