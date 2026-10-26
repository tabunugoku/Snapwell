using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;
using Snapwell.Settings.Updates;

namespace Snapwell.Settings.Pages;

/// <summary>一般ページの更新の確認を扱う。</summary>
public sealed partial class GeneralPage
{
    private static readonly string? _updateVersion = typeof(GeneralPage).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    private static readonly UpdateOsArchitecture _updateArchitecture = RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => UpdateOsArchitecture.X64,
        Architecture.Arm64 => UpdateOsArchitecture.Arm64,
        _ => UpdateOsArchitecture.Other,
    };
    private bool _syncingUpdates = true;
    private bool _automaticUpdateEnabled;
    private bool _updateCanWrite;
    private bool _updateBusy;
    private readonly UpdatePageSession _updateSession = UpdatePageSession.Shared;
    private CancellationTokenSource? _updatePageStop;
    private UpdateOffer? _updateOffer;
    private byte[]? _updateManifest;
    private byte[]? _updateSignature;
    private bool _updateInstalling;
    private bool _updateLoading = true;
    private CancellationTokenSource? _updateInstallStop;
    private Task<UpdateStartupResult>? _updateInstallTask;
    private AppWindow? _updateWindow;
    private bool _updateClosePending;
    private static string UpdateRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapwell", "update");
    private static string UpdatePending => Path.Combine(UpdateRoot, "pending");

    private static bool IsAnotherUpdateRunning()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new UpdateSignalNames(identity.User!.Value).IsUpdateRunning();
    }

    private async void Updates_Loaded(object sender, RoutedEventArgs args)
    {
        var stop = new CancellationTokenSource();
        _updatePageStop = stop;
        _updateLoading = true;
        _updateWindow = AppWindow.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _updateWindow.Closing += UpdateWindow_Closing;
        var session = _updateSession;
        _updateBusy = false;
        _updateCanWrite = false;
        UpdateCheckNowButton.IsEnabled = false;
        RefreshUpdateSaveControls();
        try
        {
            var resultPath = Path.Combine(UpdateRoot, "last-result.json");
            var lastResult = await Task.Run(() =>
                (Exists: File.Exists(resultPath), Result: UpdateResultReader.Read(resultPath)), stop.Token);
            if (stop.IsCancellationRequested) { return; }
            if (lastResult.Result is { } result) { ShowInstallResult(result); }
            // 表示後に消す。読み取れなかった壊れた結果も、次回に持ち越さない。
            if (lastResult.Exists || lastResult.Result is not null)
            {
                await Task.Run(() => UpdateResultReader.TryDelete(resultPath));
            }
            var restored = await session.ReadAsync(ReadUpdateState, stop.Token);
            if (!session.CanApply(restored.Generation, stop.IsCancellationRequested)) { return; }
            ApplyUpdateState(restored.Value.State, restored.Value.Offer);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            if (!stop.IsCancellationRequested)
            {
                ReportEnabledFailure(UiText.Current.Get("General.Device.Unreadable"));
            }
        }
        finally
        {
            if (!stop.IsCancellationRequested)
            {
                _updateLoading = false;
                RefreshUpdateSaveControls();
            }
        }
    }

    private void Updates_Unloaded(object sender, RoutedEventArgs args)
    {
        // 戻ってきた非同期処理は、古いページの UI を更新しない。
        _updatePageStop?.Cancel();
        _updateInstallStop?.Cancel();
        // ページ移動直後にウィンドウを閉じても、進行中の後始末を待つハンドラーを残す。
        if (_updateInstallTask is not { IsCompleted: false }) { DetachUpdateWindow(); }
        _updatePageStop?.Dispose();
        _updatePageStop = null;
    }

    private (SettingsLoadResult<DeviceState> State, UpdateOffer? Offer) ReadUpdateState()
    {
        var loaded = UpdatePageModel.ReadState(_deviceStore);
        var offer = loaded.IsReadOnly ? null : UpdateOfferResolver.Resolve(loaded.Value,
            _updateVersion, UpdatePublicKey.Current, _updateArchitecture);
        return (loaded, offer);
    }

    private void ApplyUpdateState(SettingsLoadResult<DeviceState> loaded, UpdateOffer? offer)
    {
        _updateManifest = loaded.Value.CachedUpdateManifest?.ToArray();
        _updateSignature = loaded.Value.CachedUpdateSignature?.ToArray();
        SetUpdateToggle(loaded.IsReadOnly ? _automaticUpdateEnabled : loaded.Value.UpdateCheckEnabled,
            !loaded.IsReadOnly);
        ShowUpdateOffer(offer, !loaded.IsReadOnly);
        if (loaded.IsReadOnly) { ReportEnabledFailure(UiText.Current.Get("General.Device.Unreadable")); }
        UpdateLastChecked.Text = loaded.Value.LastUpdateCheckUtc is { } time
            ? UiText.Current.Format("General_UpdateStatusLastChecked", ("time", time.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)))
            : "";
        UpdateLastChecked.Visibility = UpdateLastChecked.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetUpdateToggle(bool enabled, bool canWrite)
    {
        _syncingUpdates = true;
        try
        {
            _automaticUpdateEnabled = enabled;
            _updateCanWrite = canWrite;
            UpdateAutoToggle.IsOn = enabled;
            RefreshUpdateSaveControls();
        }
        finally { _syncingUpdates = false; }
    }

    private void RefreshUpdateSaveControls()
    {
        var idle = !_updateLoading && !_updateClosePending && !_updateInstalling
            && !_updateBusy && !_updateSession.IsSaving;
        UpdateAutoToggle.IsEnabled = _updateCanWrite && idle;
        UpdateBrowserDownloadButton.IsEnabled = idle && _updateOffer is not null;
        UpdateReleaseNotesButton.IsEnabled = idle && _updateOffer is not null;
        UpdateCheckNowButton.IsEnabled = idle;
        UpdateSkipButton.IsEnabled = _updateCanWrite && idle && _updateOffer is not null;
        RefreshInstallAvailability();
    }

    private async void UpdateAuto_Toggled(object sender, RoutedEventArgs args)
    {
        var session = _updateSession;
        if (_syncingUpdates || _updateInstalling || session.IsSaving || _updatePageStop is not { } stop) { return; }
        ErrorNotice.IsOpen = false;
        var enabled = UpdateAutoToggle.IsOn;
        var previous = _automaticUpdateEnabled;
        var generation = session.Generation + 1;
        var pending = session.SaveAsync(() => UpdatePageModel.SetAutomatic(_deviceStore, enabled, previous));
        RefreshUpdateSaveControls();
        try
        {
            var result = await pending;
            if (result is null || !session.CanApply(generation, stop.IsCancellationRequested)) { return; }
            SetUpdateToggle(result.Enabled, result.CanWrite);
            if (!result.Saved) { ReportEnabledFailure(UiText.Current.Get("General_UpdateSaveFailed")); }
        }
        finally
        {
            if (!stop.IsCancellationRequested) { RefreshUpdateSaveControls(); }
        }
    }

    private async void UpdateCheckNow_Click(object sender, RoutedEventArgs args)
    {
        if (_updateBusy || _updateInstalling || _updateSession.IsSaving || _updatePageStop is not { } stop) { return; }
        _updateBusy = true;
        RefreshUpdateSaveControls();
        UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusChecking");
        ErrorNotice.IsOpen = false;
        try
        {
            // Fetcher の寿命を一回の操作に限定し、ページを離れると取得を取り消す。
            using var fetcher = new UpdateFetcher(_updateVersion ?? "0.0.0");
            var checker = new UpdateChecker(fetcher, _deviceStore, _updateVersion,
                UpdatePublicKey.Current, _updateArchitecture);
            var token = stop.Token;
            var session = _updateSession;
            var checkGeneration = session.Generation;
            var result = await Task.Run(() => checker.CheckAsync(token), token);
            if (stop.IsCancellationRequested) { return; }
            var key = UpdatePageModel.StatusKey(result.Outcome);
            if (session.CanApply(checkGeneration, stop.IsCancellationRequested))
            {
                UpdateStatus.Text = key is null ? "" : UiText.Current.Get(key);
            }
            // 確認中の本体によるスキップや新しいキャッシュも最新値から再評価する。
            var restored = await session.ReadAsync(ReadUpdateState, token);
            if (session.CanApply(restored.Generation, stop.IsCancellationRequested))
            {
                ApplyUpdateState(restored.Value.State, restored.Value.Offer);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            if (!stop.IsCancellationRequested)
            {
                SetUpdateToggle(_automaticUpdateEnabled, false);
                UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusFailed");
                ReportEnabledFailure(UiText.Current.Get("General.Device.Unreadable"));
            }
        }
        finally
        {
            if (!stop.IsCancellationRequested)
            {
                _updateBusy = false;
                RefreshUpdateSaveControls();
            }
        }
    }

    private void ShowUpdateOffer(UpdateOffer? offer, bool canWrite)
    {
        _updateOffer = offer;
        var panel = UpdatePageModel.Panel(offer, UiText.Current.Language);
        UpdateOfferPanel.Visibility = Visibility.Visible;
        RefreshUpdateSaveControls();
        UpdateOfferTitle.Visibility = panel is null ? Visibility.Collapsed : Visibility.Visible;
        if (panel is null)
        {
            UpdateOfferRequired.Visibility = Visibility.Collapsed;
            UpdateOfferSummary.Visibility = Visibility.Collapsed;
            return;
        }
        UpdateOfferTitle.Text = UiText.Current.Format("General_UpdateOfferTitle", ("version", panel.Version));
        UpdateOfferRequired.Text = UiText.Current.Get("General_UpdateOfferRequired");
        UpdateOfferRequired.Visibility = panel.RequiresUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateOfferSummary.Text = panel.Summary;
        UpdateOfferSummary.Visibility = panel.Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSkipButton.IsEnabled = canWrite && !_updateSession.IsSaving && !_updateInstalling && !_updateBusy;
    }

    private async void UpdateSkip_Click(object sender, RoutedEventArgs args)
    {
        var session = _updateSession;
        if (_updateInstalling || _updateBusy || session.IsSaving || _updatePageStop is not { } stop
            || _updateOffer is not { } offer) { return; }
        ErrorNotice.IsOpen = false;
        var generation = session.Generation + 1;
        var version = offer.Manifest.Version.ToString();
        var pending = session.SaveAsync(() => UpdatePageModel.Skip(_deviceStore, version));
        RefreshUpdateSaveControls();
        try
        {
            var saved = await pending;
            if (saved is null || !session.CanApply(generation, stop.IsCancellationRequested)) { return; }
            ShowUpdateOffer(null, true);
            UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusSkipped");
        }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            if (session.CanApply(generation, stop.IsCancellationRequested))
            {
                ReportEnabledFailure(UiText.Current.Get("General_UpdateSaveFailed"));
            }
        }
        finally
        {
            if (!stop.IsCancellationRequested) { RefreshUpdateSaveControls(); }
        }
    }

    private void UpdateDownload_Click(object sender, RoutedEventArgs args) => OpenUpdateLink(_updateOffer?.Asset.Url);

    private void UpdateReleaseNotes_Click(object sender, RoutedEventArgs args) =>
        OpenUpdateLink(_updateOffer?.Manifest.ReleaseNotesUrl);

    private void UpdateReinstall_Click(object sender, RoutedEventArgs args) =>
        OpenUpdateLink(new Uri("https://github.com/tabunugoku/Snapwell/releases"));

    private void RefreshInstallAvailability()
    {
        UpdateInstallBlock block;
        try
        {
            block = UpdateInstallPlanner.Evaluate(_updateVersion, _updateOffer, _updateManifest, _updateSignature,
                _updateArchitecture, _executable, UpdatePending, File.Exists,
                path => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace, IsAnotherUpdateRunning);
        }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            block = UpdateInstallBlock.InsufficientSpace;
        }
        UpdateInstallButton.IsEnabled = block == UpdateInstallBlock.None && _updateCanWrite
            && !_updateLoading && !_updateClosePending && !_updateInstalling && !_updateBusy && !_updateSession.IsSaving;
        var key = UpdatePageModel.InstallBlockKey(block);
        UpdateInstallReason.Text = key is null ? "" : UiText.Current.Get(key);
        AutomationProperties.SetName(UpdateInstallReason, UpdateInstallReason.Text);
        UpdateInstallReason.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowInstallResult(UpdateReadResult result)
    {
        var key = UpdatePageModel.ResultKey(result);
        UpdateInstallResult.Text = UiText.Current.Format(key,
            ("version", result.Version ?? ""), ("code", result.Code));
        AutomationProperties.SetName(UpdateInstallResult, UpdateInstallResult.Text);
        UpdateInstallResult.Visibility = Visibility.Visible;
        UpdateReinstallLink.Visibility = key == "Update.Result.Reinstall" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs args)
    {
        if (_updateLoading || _updateClosePending || _updateInstalling || _updateBusy || _updateSession.IsSaving
            || _updatePageStop is not { } pageStop) { return; }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(pageStop.Token);
        var window = _updateWindow;
        _updateInstallStop = stop;
        _updateInstalling = true;
        ErrorNotice.IsOpen = false;
        RefreshUpdateSaveControls();
        try
        {
            // 案内を表示した後の本体によるキャッシュ変更も、開始直前に再評価する。
            var restored = await _updateSession.ReadAsync(ReadUpdateState, stop.Token);
            if (!_updateSession.CanApply(restored.Generation, stop.IsCancellationRequested)) { return; }
            ApplyUpdateState(restored.Value.State, restored.Value.Offer);
            var block = UpdateInstallPlanner.Evaluate(_updateVersion, _updateOffer, _updateManifest, _updateSignature,
                _updateArchitecture, _executable, UpdatePending, File.Exists,
                path => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace, IsAnotherUpdateRunning);
            if (block != UpdateInstallBlock.None || !_updateCanWrite) { return; }
            // 更新後に設定画面を閉じるため、編集中の設定を先に保存する。
            if (_session.HasChanges && !_session.TrySave(out var message))
            {
                ReportEnabledFailure(message);
                return;
            }
            var offer = _updateOffer!;
            using var identity = WindowsIdentity.GetCurrent();
            var request = new UpdateStartupRequest(UpdatePending, _executable, identity.User!.Value,
                Environment.ProcessId, offer.Asset, _updateManifest!, _updateSignature!, UpdatePublicKey.Current ?? "");
            UpdateDownloadProgress.Visibility = Visibility.Visible;
            UpdateCancelButton.IsEnabled = true;
            UpdateProgressBar.Maximum = offer.Asset.Size;
            ShowDownloadProgress(0, offer.Asset.Size);
            var progress = new UpdateProgressThrottle(TimeProvider.System,
                action => DispatcherQueue.TryEnqueue(() => action()), bytes =>
            {
                if (!stop.IsCancellationRequested && ReferenceEquals(_updateInstallStop, stop))
                {
                    ShowDownloadProgress(bytes, offer.Asset.Size);
                }
            });
            var startup = UpdateStartup.CreateForRelease(_updateVersion!, IsAnotherUpdateRunning,
                StartUpdateCoordinatorAsync);
            _updateInstallTask = Task.Run(() => startup.RunAsync(request, progress, stop.Token));
            var result = await _updateInstallTask;
            if (result.Failure is null)
            {
                // 起動後は取り消さず、進行役が待っている設定画面の PID を終了させる。
                if (!_updateClosePending) { window?.Destroy(); }
                return;
            }
            if (!pageStop.IsCancellationRequested)
            {
                var text = UiText.Current.Get(UpdatePageModel.StartupKey(result));
                if (result.Failure == UpdateStartupFailure.Cancelled
                    || result.DownloadFailure == UpdateDownloadFailure.Cancelled) { UpdateStatus.Text = text; }
                else { ReportEnabledFailure(text); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            if (!pageStop.IsCancellationRequested)
            {
                ReportEnabledFailure(UiText.Current.Get("Update.Result.IoError"));
            }
        }
        finally
        {
            _updateInstallTask = null;
            _updateInstallStop = null;
            _updateInstalling = false;
            if (!pageStop.IsCancellationRequested)
            {
                UpdateDownloadProgress.Visibility = Visibility.Collapsed;
                RefreshUpdateSaveControls();
            }
            else { DetachUpdateWindow(); }
        }
    }

    private Task<bool> StartUpdateCoordinatorAsync(ProcessStartInfo info, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                // 取得中にほかのページを編集した場合も、閉じる直前の設定を失わない。
                if (_session.HasChanges && !_session.TrySave(out var message))
                {
                    ReportEnabledFailure(message);
                    completion.SetResult(false);
                    return;
                }
                using var process = Process.Start(info);
                completion.SetResult(process is not null);
            }
            catch (OperationCanceledException) { completion.SetCanceled(token); }
            catch (Exception exception) { completion.SetException(exception); }
        })) { completion.SetCanceled(); }
        return completion.Task;
    }

    private void ShowDownloadProgress(long bytes, long total)
    {
        UpdateProgressBar.Value = Math.Clamp(bytes, 0, total);
        UpdateProgressText.Text = UiText.Current.Format("Update.Progress",
            ("downloaded", (bytes / 1_000_000d).ToString("0.0", CultureInfo.InvariantCulture)),
            ("total", (total / 1_000_000d).ToString("0.0", CultureInfo.InvariantCulture)));
        AutomationProperties.SetName(UpdateProgressText, UpdateProgressText.Text);
    }

    private void UpdateCancel_Click(object sender, RoutedEventArgs args)
    {
        UpdateCancelButton.IsEnabled = false;
        _updateInstallStop?.Cancel();
    }

    private async void UpdateWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (args.Cancel || _updateInstallTask is not { IsCompleted: false } task) { return; }
        // プロセスを先に終了するとワーカーの finally が走らないため、後始末を待ってから閉じる。
        args.Cancel = true;
        if (_updateClosePending) { return; }
        _updateClosePending = true;
        _updateInstallStop?.Cancel();
        await task;
        sender.Destroy();
    }

    private void DetachUpdateWindow()
    {
        if (_updateWindow is { } window) { window.Closing -= UpdateWindow_Closing; }
        _updateWindow = null;
    }

    private void OpenUpdateLink(Uri? uri)
    {
        if (uri is null || !UpdateLinks.IsAllowed(uri)) { return; }
        try { using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.Security.SecurityException)
        {
            ReportEnabledFailure(UiText.Current.Get("General_UpdateStatusFailed"));
        }
    }
}
