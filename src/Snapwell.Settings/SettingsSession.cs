using System.Text.Json;
using Microsoft.UI.Dispatching;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Windows;

namespace Snapwell.Settings;

/// <summary>保存済みの値と編集中の値を分けて保持する。</summary>
public sealed class SettingsSession
{
    private static readonly TimeSpan _saveLockTimeout = TimeSpan.FromMilliseconds(200);
    private readonly SettingsStore<PortableSettings> _store = SettingsStores.Portable();
    private PortableSettings _saved = new();
    private bool _isTransferInProgress;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _reloading;
    private bool _reloadAgain;
    private bool _refreshing;

    /// <summary>起動時に一度だけ設定を読み込み、読み取れない場合は保存を止める。</summary>
    public SettingsSession()
    {
        try
        {
            var result = _store.Load();
            _saved = SettingsTransfer.Normalize(result.Value);
            Current = SettingsTransfer.Normalize(_saved);
            IsReadOnly = result.IsReadOnly;
            LoadMessage = result.IsReadOnly
                ? "新しい版の設定のため、この画面では保存できません。Snapwell を更新してください。"
                : result.ResetToDefaults ? "設定ファイルが壊れていたため、既定値を表示しています。" : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            IsReadOnly = true;
            LoadMessage = "設定ファイルを読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。";
        }
        StartWatching();
        // セッションは画面全体で共有するため、ページ遷移ではなく UI スレッドの終了まで監視する。
        _dispatcher.ShutdownStarting += (_, _) =>
        {
            _watcher?.Dispose();
            _reloadTimer?.Dispose();
        };
    }

    public PortableSettings Current { get; private set; } = new();
    /// <summary>ルールの入力支援に使う、開いているウィンドウの取得元。</summary>
    public IOpenWindowSource OpenWindows { get; } =
        new Win32OpenWindowSource(new Win32WindowInspector(), new Win32ProcessNameSource(), (uint)Environment.ProcessId);
    public bool HasChanges { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool IsTransferInProgress
    {
        get => _isTransferInProgress;
        set
        {
            _isTransferInProgress = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public string? LoadMessage { get; }
    public string? SyncMessage { get; private set; }
    public event EventHandler? Changed;

    /// <summary>再読み込みで値または読み取り専用状態を差し替えたとき、ページの再生成を求める。</summary>
    public event EventHandler? Reloaded;

    /// <summary>検証は Core に任せ、保存せずに画面の値だけを更新する。</summary>
    public void Update(PortableSettings value)
    {
        if (IsReadOnly || _refreshing)
        {
            return;
        }

        Current = SettingsTransfer.Normalize(value);
        HasChanges = SettingsTransfer.HasChanges(_saved, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>編集中の値を既存ストアで保存し、失敗理由を表示用に返す。</summary>
    public bool TrySave(out string message)
    {
        if (IsReadOnly)
        {
            message = "この設定は読み取り専用のため、保存できません。";
            return false;
        }

        try
        {
            if (!SettingsSync.WithLock(SettingsStores.PortablePath(), () => _store.Save(Current), _saveLockTimeout))
            {
                message = "同期の実行中のため、少し待ってからもう一度保存してください";
                return false;
            }
            _saved = SettingsTransfer.Normalize(Current);
            HasChanges = false;
            SyncMessage = null;
            StartWatching();
            message = "保存しました。Snapwell が起動していれば、すぐに反映されます。";
        }
        catch (FutureSettingsVersionException)
        {
            IsReadOnly = true;
            message = "新しい版の設定に変わったため、保存できません。Snapwell を更新して開き直してください。";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException)
        {
            message = "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。";
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>取り込み後の値を読み直し、編集中の値があれば保存時の上書きを知らせる。</summary>
    public async Task ReloadAfterSyncAsync()
    {
        if (_reloading) { _reloadAgain = true; return; }
        _reloading = true;
        try
        {
            var loaded = await Task.Run(_store.TryReload);
            if (loaded is null) { return; }
            var wasReadOnly = IsReadOnly;
            var replaced = false;
            var decision = SettingsSync.DecideReload(_saved, loaded, HasChanges, IsReadOnly);
            if (decision == SettingsSyncReloadKind.Unchanged) { return; }
            if (decision == SettingsSyncReloadKind.ReadOnly)
            {
                IsReadOnly = true;
                SyncMessage = "新しい版の設定のため同期できません。Snapwell を更新してください。";
            }
            else if (decision == SettingsSyncReloadKind.KeepEdits)
            {
                IsReadOnly = false;
                SyncMessage = "同期先の設定を取り込みました。未保存の変更は、保存すると取り込んだ内容を上書きします";
            }
            else
            {
                _saved = SettingsTransfer.Normalize(loaded.Value);
                Current = SettingsTransfer.Normalize(_saved);
                IsReadOnly = false;
                SyncMessage = "同期先の設定を取り込みました。";
                replaced = true;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            if (replaced || wasReadOnly != IsReadOnly)
            {
                // 古いページのフォーカス解除と新しいコントロールの初期化で編集値を戻さない。
                _refreshing = true;
                try { Reloaded?.Invoke(this, EventArgs.Empty); }
                finally { _refreshing = false; }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            SyncMessage = "取り込んだ設定を再読み込みできませんでした。画面を開き直してください。";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _reloading = false;
            if (_reloadAgain) { _reloadAgain = false; _ = ReloadAfterSyncAsync(); }
        }
    }

    private void StartWatching()
    {
        if (_watcher is not null) { return; }
        var path = SettingsStores.PortablePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 原子的な置換が発生しても、読み込みは収束後にバックグラウンドで行う。
            _reloadTimer = new(_ => _dispatcher.TryEnqueue(async () => await ReloadAfterSyncAsync()),
                null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => QueueReload();
            _watcher.Created += (_, _) => QueueReload();
            _watcher.Renamed += (_, _) => QueueReload();
            _watcher.Error += (_, _) => QueueReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _watcher?.Dispose();
            _watcher = null;
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }
    }

    private void QueueReload()
    {
        try { _reloadTimer?.Change(300, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }
}
