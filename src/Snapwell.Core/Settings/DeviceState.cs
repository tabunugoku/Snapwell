using Snapwell.Core.Localization;

namespace Snapwell.Core.Settings;

/// <summary>同期しない端末固有の選択。OS 設定の退避値は別ファイルで管理する。</summary>
public sealed record DeviceState
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool Enabled { get; set; } = true;
    public bool DisableStandardSnap { get; set; }
    public bool ReplaceWinArrow { get; set; }
    public bool StandardSnapConsentGiven { get; set; }
    public bool SetupCompleted { get; set; }
    public string? SyncFolder { get; set; }
    public bool SyncEnabled { get; set; }
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? LastSyncedHash { get; set; }

    // 列挙型は UseStringEnumConverter が未知の値を JsonException にするため、文字列で正規化する。
    /// <summary>この PC の表示言語。未知の値は自動として扱う。</summary>
    public string Language { get; set; } = "auto";

    /// <summary>初回セットアップ追加前の端末では、案内を再表示しない。</summary>
    public static DeviceState Migrate(DeviceState value, double version) => value with
    {
        SchemaVersion = CurrentSchemaVersion,
        SetupCompleted = version < 2 || value.SetupCompleted,
        ReplaceWinArrow = version >= 3 && value.ReplaceWinArrow,
        SyncFolder = version < 3 ? null : value.SyncFolder,
        SyncEnabled = version >= 3 && value.SyncEnabled,
        LastSyncUtc = version < 3 ? null : value.LastSyncUtc,
        LastSyncedHash = version < 3 ? null : value.LastSyncedHash,
        Language = version < 4 ? "auto" : UiLanguageTags.NormalizePreference(value.Language),
    };
}
