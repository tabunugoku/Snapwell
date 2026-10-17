namespace Snapwell.Core.Settings;

/// <summary>同期しない端末固有の選択。OS 設定の退避値は別ファイルで管理する。</summary>
public sealed record DeviceState
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public bool DisableStandardSnap { get; init; }
    public bool StandardSnapConsentGiven { get; init; }
}
