using System.Security;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Updates;

namespace Snapwell.Settings.Updates;

/// <summary>画面に表示する案内の値。</summary>
internal sealed record UpdatePanel(string Version, bool RequiresUpdate, string Summary);

/// <summary>保存結果に合わせたスイッチの状態。</summary>
internal sealed record UpdateToggleResult(bool Enabled, bool CanWrite, bool Saved);

/// <summary>ページごとの保存を直列化し、古い読み取りによる表示の巻き戻りを防ぐ。</summary>
internal sealed class UpdatePageSession
{
    internal int Generation { get; private set; }
    internal bool IsSaving { get; private set; }

    /// <summary>保存中と、保存やページの破棄より前の結果は反映しない。</summary>
    internal bool CanApply(int generation, bool cancelled) =>
        !cancelled && !IsSaving && generation == Generation;

    /// <summary>保存と失敗後の読み直しをワーカーへ渡し、連打は受け付けない。</summary>
    internal async Task<T?> SaveAsync<T>(Func<T> save) where T : class
    {
        if (IsSaving) { return null; }
        IsSaving = true;
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

    internal static UpdateToggleResult SetAutomatic(ISettingsStore<DeviceState> store, bool enabled, bool previous)
    {
        try
        {
            var saved = DeviceStateChanges.Update(store, state => state with { UpdateCheckEnabled = enabled });
            return new(saved.UpdateCheckEnabled, true, true);
        }
        catch (Exception exception) when (IsSaveFailure(exception))
        {
            try
            {
                var actual = store.TryReload() ?? store.Load();
                return new(actual.Value.UpdateCheckEnabled, !actual.IsReadOnly, false);
            }
            catch (Exception reloadException) when (IsSaveFailure(reloadException))
            {
                return new(previous, false, false);
            }
        }
    }

    internal static DeviceState Skip(ISettingsStore<DeviceState> store, string version) =>
        DeviceStateChanges.Update(store, state => state with { SkippedVersion = version });

    internal static bool IsSaveFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or SecurityException or System.Text.Json.JsonException or FutureSettingsVersionException;
}
