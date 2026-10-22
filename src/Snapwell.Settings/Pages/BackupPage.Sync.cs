using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Windows.Storage.Pickers;

namespace Snapwell.Settings.Pages;

public sealed partial class BackupPage
{
    private readonly SettingsStore<DeviceState> _syncStore = SettingsStores.Device();
    private SettingsSession _syncSession = null!;
    private DeviceState _syncDevice = new();
    private SettingsSyncResult? _syncResult;
    private FileSystemWatcher? _deviceWatcher;
    private Timer? _deviceTimer;
    private bool _syncRefreshing;
    private bool _syncBusy;
    private bool _syncDeviceReadOnly = true;
    private bool _syncActive;
    private int _syncGeneration;
    private int _syncRefreshVersion;

    partial void ConfigureSync(SettingsSession session, nint owner)
    {
        _syncSession = session;
        SyncFolderButton.Click += async (_, _) => await ChooseSyncFolderAsync(owner);
        SyncToggle.Toggled += async (_, _) =>
        {
            if (!_syncRefreshing && !_syncBusy)
            {
                var enabled = SyncToggle.IsOn;
                await SaveSyncChoiceAsync(current => current with { SyncEnabled = enabled });
            }
        };
        SyncNowButton.Click += async (_, _) => await SyncNowAsync();
        Loaded += async (_, _) =>
        {
            _syncActive = true;
            var generation = ++_syncGeneration;
            _syncSession.Changed += SyncSessionChanged;
            StartDeviceWatcher(generation);
            await RefreshSyncAsync(generation);
            SyncSessionChanged(this, EventArgs.Empty);
        };
        Unloaded += (_, _) =>
        {
            _syncActive = false;
            _syncGeneration++;
            _syncSession.Changed -= SyncSessionChanged;
            _deviceWatcher?.Dispose();
            _deviceWatcher = null;
            _deviceTimer?.Dispose();
            _deviceTimer = null;
        };
    }

    private async Task RefreshSyncAsync(int generation)
    {
        var version = ++_syncRefreshVersion;
        try
        {
            var loaded = await Task.Run(() => _syncStore.TryReload() ?? _syncStore.Load());
            if (!Active(generation) || version != _syncRefreshVersion) { return; }
            if (_syncDevice.LastSyncUtc != loaded.Value.LastSyncUtc) { _syncResult = null; }
            _syncDevice = loaded.Value;
            _syncDeviceReadOnly = loaded.IsReadOnly;
            RenderSync();
            if (loaded.IsReadOnly)
            {
                ShowNotice(UiText.Current.Get("Backup.Error.FutureDevice"), InfoBarSeverity.Warning);
            }
        }
        catch (Exception)
        {
            if (!Active(generation) || version != _syncRefreshVersion) { return; }
            _syncDeviceReadOnly = true;
            RenderSync();
            ShowNotice(UiText.Current.Get("Backup.Error.LoadDevice"), InfoBarSeverity.Error);
        }
    }

    private void RenderSync()
    {
        _syncRefreshing = true;
        try
        {
            SyncFolderText.Text = _syncDevice.SyncFolder ?? UiText.Current.Get("Backup.NotSet");
            SyncToggle.IsOn = _syncDevice.SyncEnabled;
            var folderSet = !string.IsNullOrWhiteSpace(_syncDevice.SyncFolder);
            SyncFolderButton.IsEnabled = !_syncBusy && !_syncDeviceReadOnly;
            SyncToggle.IsEnabled = !_syncBusy && !_syncDeviceReadOnly && folderSet;
            SyncNowButton.IsEnabled = !_syncBusy && !_syncDeviceReadOnly && folderSet &&
                !_syncSession.IsReadOnly && !_syncSession.IsTransferInProgress;
            var timestamp = _syncDevice.LastSyncUtc?.ToLocalTime()
                .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
            LastSyncText.Text = timestamp is null
                ? _syncResult?.ToDisplayText() ?? UiText.Current.Get("Backup.NotSynced")
                : UiText.Current.Format("Backup.SyncWithTimestamp", ("timestamp", timestamp),
                    ("status", _syncResult?.ToDisplayText() ?? UiText.Current.Get("Backup.Synced")));
        }
        finally { _syncRefreshing = false; }
    }

    private void SyncSessionChanged(object? sender, EventArgs args)
    {
        if (!_syncActive) { return; }
        RenderSync();
        if (_syncSession.SyncMessage is { } message)
        {
            ShowNotice(message, _syncSession.HasChanges || _syncSession.IsReadOnly
                ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
        }
        else if (_syncSession.IsReadOnly)
        {
            ShowNotice(UiText.Current.Get("Backup.Error.FutureSettings"), InfoBarSeverity.Warning);
        }
    }

    private async Task ChooseSyncFolderAsync(nint owner)
    {
        var generation = _syncGeneration;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null || !Active(generation)) { return; }
            var rejection = SettingsSync.ValidateFolder(SettingsStores.PortablePath(), folder.Path);
            if (rejection is not null)
            {
                ShowNotice(rejection, InfoBarSeverity.Error);
                return;
            }
            await SaveSyncChoiceAsync(current => string.Equals(current.SyncFolder, folder.Path,
                StringComparison.OrdinalIgnoreCase) ? current : current with
                {
                    SyncFolder = folder.Path,
                    LastSyncUtc = null,
                    LastSyncedHash = null,
                });
        }
        catch (Exception)
        {
            if (Active(generation))
            {
                ShowNotice(UiText.Current.Get("Backup.Error.ChooseFolder"), InfoBarSeverity.Error);
            }
        }
    }

    private async Task SaveSyncChoiceAsync(Func<DeviceState, DeviceState> update)
    {
        var generation = _syncGeneration;
        _syncRefreshVersion++;
        _syncBusy = true;
        RenderSync();
        try
        {
            var state = await Task.Run(() => DeviceStateChanges.Update(_syncStore, update));
            if (!Active(generation)) { return; }
            _syncDevice = state;
            _syncResult = null;
            if (_deviceWatcher is null) { StartDeviceWatcher(generation); }
        }
        catch (Exception)
        {
            if (Active(generation))
            {
                ShowNotice(UiText.Current.Get("Backup.Error.SaveChoice"), InfoBarSeverity.Error);
            }
        }
        finally
        {
            _syncBusy = false;
            if (Active(generation)) { RenderSync(); }
        }
    }

    private async Task SyncNowAsync()
    {
        if (_syncBusy || _syncSession.IsReadOnly || _syncSession.IsTransferInProgress) { return; }
        var generation = _syncGeneration;
        _syncRefreshVersion++;
        _syncBusy = true;
        _syncSession.IsTransferInProgress = true;
        RenderSync();
        try
        {
            var outcome = await Task.Run(() =>
            {
                var local = SettingsStores.PortablePath();
                var sync = new SettingsSync(local,
                    Path.Combine(Path.GetDirectoryName(SettingsStores.DevicePath())!, "sync-backups"),
                    TimeProvider.System, SettingsSync.MutexName(local));
                return sync.Run(_syncStore);
            });
            // ページを離れても取り込んだ内容と編集中の値の関係はセッションで保つ。
            if (outcome.Pulled) { await _syncSession.ReloadAfterSyncAsync(); }
            if (!Active(generation)) { return; }
            await RefreshSyncAsync(generation);
            if (!Active(generation)) { return; }
            _syncResult = outcome;
            if (_syncSession.SyncMessage is null)
            {
                ShowNotice(_syncResult.ToDisplayText(), _syncResult.Retryable ||
                    _syncResult.Kind is SettingsSyncKind.Rejected or SettingsSyncKind.Skipped
                    ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
        }
        catch (Exception)
        {
            if (Active(generation))
            {
                _syncResult = new(SettingsSyncKind.Failed, UiText.Current.Get("Backup.SyncReason.FileAccess"));
                ShowNotice(_syncResult.ToDisplayText(), InfoBarSeverity.Error);
            }
        }
        finally
        {
            _syncBusy = false;
            _syncSession.IsTransferInProgress = false;
            if (Active(generation)) { RenderSync(); }
        }
    }

    private bool Active(int generation) => _syncActive && generation == _syncGeneration;

    private void StartDeviceWatcher(int generation)
    {
        var path = SettingsStores.DevicePath();
        if (!Directory.Exists(Path.GetDirectoryName(path))) { return; }
        try
        {
            _deviceTimer = new(_ => DispatcherQueue.TryEnqueue(async () =>
            {
                if (Active(generation) && !_syncBusy) { await RefreshSyncAsync(generation); }
            }), null, Timeout.Infinite, Timeout.Infinite);
            _deviceWatcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _deviceWatcher.Changed += (_, _) => QueueDeviceRefresh();
            _deviceWatcher.Created += (_, _) => QueueDeviceRefresh();
            _deviceWatcher.Renamed += (_, _) => QueueDeviceRefresh();
            _deviceWatcher.Error += (_, _) => QueueDeviceRefresh();
            _deviceWatcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _deviceWatcher?.Dispose();
            _deviceWatcher = null;
            _deviceTimer?.Dispose();
            _deviceTimer = null;
        }
    }

    private void QueueDeviceRefresh()
    {
        try { _deviceTimer?.Change(300, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }
}
