using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
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
    private bool _updateBusy;
    private CancellationTokenSource? _updatePageStop;
    private UpdateOffer? _updateOffer;

    private async void Updates_Loaded(object sender, RoutedEventArgs args)
    {
        var stop = new CancellationTokenSource();
        _updatePageStop = stop;
        UpdateCheckNowButton.IsEnabled = false;
        UpdateAutoToggle.IsEnabled = false;
        try
        {
            var restored = await Task.Run(() => ReadUpdateState(), stop.Token);
            if (stop.IsCancellationRequested) { return; }
            ApplyUpdateState(restored.State, restored.Offer);
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
            if (!stop.IsCancellationRequested) { UpdateCheckNowButton.IsEnabled = true; }
        }
    }

    private void Updates_Unloaded(object sender, RoutedEventArgs args)
    {
        // 戻ってきた非同期処理は、古いページの UI を更新しない。
        _updatePageStop?.Cancel();
        _updatePageStop?.Dispose();
        _updatePageStop = null;
    }

    private (SettingsLoadResult<DeviceState> State, UpdateOffer? Offer) ReadUpdateState()
    {
        var loaded = _deviceStore.TryReload() ?? _deviceStore.Load();
        var offer = loaded.IsReadOnly ? null : UpdateOfferResolver.Resolve(loaded.Value,
            _updateVersion, UpdatePublicKey.Current, _updateArchitecture);
        return (loaded, offer);
    }

    private void ApplyUpdateState(SettingsLoadResult<DeviceState> loaded, UpdateOffer? offer)
    {
        SetUpdateToggle(loaded.Value.UpdateCheckEnabled, !loaded.IsReadOnly);
        ShowUpdateOffer(offer, !loaded.IsReadOnly);
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
            UpdateAutoToggle.IsOn = enabled;
            UpdateAutoToggle.IsEnabled = canWrite;
        }
        finally { _syncingUpdates = false; }
    }

    private void UpdateAuto_Toggled(object sender, RoutedEventArgs args)
    {
        if (_syncingUpdates) { return; }
        ErrorNotice.IsOpen = false;
        var result = UpdatePageModel.SetAutomatic(_deviceStore, UpdateAutoToggle.IsOn, _automaticUpdateEnabled);
        SetUpdateToggle(result.Enabled, result.CanWrite);
        if (!result.Saved) { ReportEnabledFailure(UiText.Current.Get("General_UpdateSaveFailed")); }
    }

    private async void UpdateCheckNow_Click(object sender, RoutedEventArgs args)
    {
        if (_updateBusy || _updatePageStop is not { } stop) { return; }
        _updateBusy = true;
        UpdateCheckNowButton.IsEnabled = false;
        UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusChecking");
        ErrorNotice.IsOpen = false;
        try
        {
            // Fetcher の寿命を一回の操作に限定し、ページを離れると取得を取り消す。
            using var fetcher = new UpdateFetcher(_updateVersion ?? "0.0.0");
            var checker = new UpdateChecker(fetcher, _deviceStore, _updateVersion,
                UpdatePublicKey.Current, _updateArchitecture);
            var token = stop.Token;
            var result = await Task.Run(() => checker.CheckAsync(token), token);
            if (stop.IsCancellationRequested) { return; }
            var key = UpdatePageModel.StatusKey(result.Outcome);
            UpdateStatus.Text = key is null ? "" : UiText.Current.Get(key);
            // 確認中の本体によるスキップや新しいキャッシュも最新値から再評価する。
            var restored = await Task.Run(() => ReadUpdateState(), token);
            if (!stop.IsCancellationRequested) { ApplyUpdateState(restored.State, restored.Offer); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            if (!stop.IsCancellationRequested) { UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusFailed"); }
        }
        finally
        {
            _updateBusy = false;
            if (!stop.IsCancellationRequested) { UpdateCheckNowButton.IsEnabled = true; }
        }
    }

    private void ShowUpdateOffer(UpdateOffer? offer, bool canWrite)
    {
        _updateOffer = offer;
        var panel = UpdatePageModel.Panel(offer, UiText.Current.Language);
        UpdateOfferPanel.Visibility = panel is null ? Visibility.Collapsed : Visibility.Visible;
        if (panel is null) { return; }
        UpdateOfferTitle.Text = UiText.Current.Format("General_UpdateOfferTitle", ("version", panel.Version));
        UpdateOfferRequired.Text = UiText.Current.Get("General_UpdateOfferRequired");
        UpdateOfferRequired.Visibility = panel.RequiresUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateOfferSummary.Text = panel.Summary;
        UpdateOfferSummary.Visibility = panel.Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSkipButton.IsEnabled = canWrite;
    }

    private void UpdateSkip_Click(object sender, RoutedEventArgs args)
    {
        if (_updateOffer is not { } offer) { return; }
        ErrorNotice.IsOpen = false;
        try
        {
            UpdatePageModel.Skip(_deviceStore, offer.Manifest.Version.ToString());
            ShowUpdateOffer(null, true);
            UpdateStatus.Text = UiText.Current.Get("General_UpdateStatusSkipped");
        }
        catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
        {
            ReportEnabledFailure(UiText.Current.Get("General_UpdateSaveFailed"));
        }
    }

    private void UpdateDownload_Click(object sender, RoutedEventArgs args) => OpenUpdateLink(_updateOffer?.Asset.Url);

    private void UpdateReleaseNotes_Click(object sender, RoutedEventArgs args) =>
        OpenUpdateLink(_updateOffer?.Manifest.ReleaseNotesUrl);

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
