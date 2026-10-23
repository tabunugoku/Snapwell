namespace Snapwell.Core.Updates;

/// <summary>プロセスではなく OS のアーキテクチャ。</summary>
public enum UpdateOsArchitecture
{
    /// <summary>x64 の OS。</summary>
    X64,
    /// <summary>ARM64 の OS。</summary>
    Arm64,
    /// <summary>対応する配布ファイルがない OS。</summary>
    Other,
}

/// <summary>OS に一致する配布ファイルだけを選ぶ。</summary>
public static class UpdateAssetSelector
{
    /// <summary>対応する RID がなければ別の RID に切り替えず null を返す。</summary>
    public static UpdateAsset? Select(UpdateOsArchitecture osArchitecture, UpdateManifest manifest)
        => osArchitecture switch
        {
            UpdateOsArchitecture.X64 => manifest.Files.GetValueOrDefault("win-x64"),
            UpdateOsArchitecture.Arm64 => manifest.Files.GetValueOrDefault("win-arm64"),
            _ => null,
        };
}
