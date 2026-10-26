using System.Security;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Updates;

namespace Snapwell.Settings.Updates;

/// <summary>画面に表示する案内の値。</summary>
internal sealed record UpdatePanel(string Version, bool RequiresUpdate, string Summary);

/// <summary>保存結果に合わせたスイッチの状態。</summary>
internal sealed record UpdateToggleResult(bool Enabled, bool CanWrite, bool Saved);

/// <summary>読み込みの成否と書き込み可否を区別したスイッチの状態。</summary>
internal sealed record UpdateToggleState(bool Enabled, bool Succeeded, bool CanWrite);

/// <summary>設定ウィンドウの保存を共有し、ページを開き直しても古い読み取りを反映しない。</summary>
internal sealed class UpdatePageSession
{
    internal static UpdatePageSession Shared { get; } = new();

    private Task? _pendingSave;
    internal int Generation { get; private set; }
    internal bool IsSaving { get; private set; }

    /// <summary>保存中と、保存やページの破棄より前の結果は反映しない。</summary>
    internal bool CanApply(int generation, bool cancelled) =>
        !cancelled && !IsSaving && generation == Generation;

    /// <summary>保存と失敗後の読み直しをワーカーへ渡し、連打は受け付けない。</summary>
    internal Task<T?> SaveAsync<T>(Func<T> save) where T : class
    {
        if (IsSaving) { return Task.FromResult<T?>(null); }
        IsSaving = true;
        var pending = SaveCoreAsync(save);
        _pendingSave = pending;
        return pending;
    }

    /// <summary>前ページの保存と世代の更新を待ち、最新値の読み取りをワーカーへ渡す。</summary>
    internal async Task<(int Generation, T Value)> ReadAsync<T>(Func<T> read,
        CancellationToken cancellationToken = default)
    {
        if (_pendingSave is { } pending)
        {
            try { await pending.WaitAsync(cancellationToken); }
            catch (Exception exception) when (UpdatePageModel.IsSaveFailure(exception))
            {
                // スキップ保存が失敗しても、実際に残った値を読み直す。
            }
        }
        var generation = Generation;
        return (generation, await Task.Run(read, cancellationToken));
    }

    private async Task<T?> SaveCoreAsync<T>(Func<T> save) where T : class
    {
        try { return await Task.Run(save); }
        finally
        {
            // 失敗後に読み直した実際の値も、保存前の読み取りで上書きさせない。
            Generation++;
            IsSaving = false;
        }
    }
}

/// <summary>WinUI に依存しない、更新画面の表示と保存の判断。</summary>
internal static class UpdatePageModel
{
    internal static string? InstallBlockKey(UpdateInstallBlock reason) => reason == UpdateInstallBlock.None
        ? null : "Update.InstallBlock." + reason;

    internal static string ResultKey(UpdateReadResult result) => result.Result switch
    {
        "installed" => "Update.Result.Installed",
        "cancelled" => "Update.Result.Cancelled",
        "rolled-back" => "Update.Result.RolledBack",
        _ => result.Code switch
        {
            10 or 11 => "Update.Result.InvalidManifest",
            12 => "Update.Result.NotNewer",
            13 => "Update.Result.Architecture",
            14 or 15 => "Update.Result.InvalidPackage",
            20 => "Update.Result.ApplyRolledBack",
            21 or 23 or 24 => "Update.Result.Reinstall",
            30 => "Update.Result.IoError",
            31 => "Update.Result.Busy",
            _ => "Update.Result.Failed",
        },
    };

    internal static string StartupKey(UpdateStartupResult result) => result.Failure switch
    {
        UpdateStartupFailure.Download => "Update.Download." + result.DownloadFailure,
        UpdateStartupFailure.SignatureInvalid => "Update.Result.InvalidManifest",
        UpdateStartupFailure.ProcessStart => "Update.StartFailed",
        UpdateStartupFailure.AnotherUpdateRunning => "Update.Startup.AnotherUpdateRunning",
        UpdateStartupFailure.IoError => "Update.Result.IoError",
        UpdateStartupFailure.Cancelled => "Update.Download.Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    /// <summary>表示だけでは破損ファイルを退避せず、復旧された既定値も正常値として返さない。</summary>
    internal static SettingsLoadResult<DeviceState> ReadState(ISettingsStore<DeviceState> store)
    {
        var loaded = store.TryReload();
        if (loaded is null || loaded.ResetToDefaults)
        {
            throw new IOException("更新の設定を読み取れない。");
        }
        return loaded;
    }

    internal static string? StatusKey(UpdateCheckOutcome outcome) => outcome switch
    {
        UpdateCheckOutcome.Available => null,
        UpdateCheckOutcome.UpToDate => "General_UpdateStatusUpToDate",
        UpdateCheckOutcome.Failed => "General_UpdateStatusFailed",
        UpdateCheckOutcome.Skipped => "General_UpdateStatusSkipped",
        UpdateCheckOutcome.DevelopmentBuild => "General_UpdateStatusDevelopmentBuild",
        UpdateCheckOutcome.NoPublicKey => "General_UpdateStatusNoPublicKey",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    internal static UpdatePanel? Panel(UpdateOffer? offer, UiLanguage language) => offer is null ? null
        : new(offer.Manifest.Version.ToString(), offer.RequiresUpdate, offer.Manifest.GetSummary(language));

    /// <summary>読めない場合と未来の版では、最後に確認できた値を保って書き込みを止める。</summary>
    internal static UpdateToggleState ReadAutomatic(ISettingsStore<DeviceState> store, bool previous = false)
    {
        try
        {
            var loaded = ReadState(store);
            return new(loaded.IsReadOnly ? previous : loaded.Value.UpdateCheckEnabled, true, !loaded.IsReadOnly);
        }
        catch (Exception exception) when (IsSaveFailure(exception))
        {
            return new(previous, false, false);
        }
    }

    internal static UpdateToggleResult SetAutomatic(ISettingsStore<DeviceState> store, bool enabled, bool previous)
    {
        try
        {
            var saved = DeviceStateChanges.Update(store, state => state with { UpdateCheckEnabled = enabled });
            return new(saved.UpdateCheckEnabled, true, true);
        }
        catch (Exception exception) when (IsSaveFailure(exception))
        {
            var actual = ReadAutomatic(store, previous);
            return new(actual.Enabled, actual.CanWrite, false);
        }
    }

    internal static DeviceState Skip(ISettingsStore<DeviceState> store, string version) =>
        DeviceStateChanges.Update(store, state => state with { SkippedVersion = version });

    internal static bool IsSaveFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or SecurityException or System.Text.Json.JsonException or FutureSettingsVersionException;
}

/// <summary>単調時計で進捗の UI 配送を 100 ms 以上あける。</summary>
internal sealed class UpdateProgressThrottle(TimeProvider clock, Action<Action> dispatch, Action<long> report)
    : IProgress<long>
{
    private readonly object _gate = new();
    private long? _last;

    /// <summary>初回と前回から十分に経過した値だけを配送する。</summary>
    public void Report(long value) => dispatch(() => Apply(value));

    private void Apply(long value)
    {
        lock (_gate)
        {
            var now = clock.GetTimestamp();
            if (_last is { } last && clock.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(100)) { return; }
            _last = now;
            report(value);
        }
    }
}
