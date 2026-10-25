using System.Security;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Updates;

namespace Snapwell.Settings.Updates;

/// <summary>画面に表示する案内の値。</summary>
internal sealed record UpdatePanel(string Version, bool RequiresUpdate, string Summary);

/// <summary>保存結果に合わせたスイッチの状態。</summary>
internal sealed record UpdateToggleResult(bool Enabled, bool CanWrite, bool Saved);

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
