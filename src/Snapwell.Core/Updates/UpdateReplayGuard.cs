namespace Snapwell.Core.Updates;

/// <summary>署名済みの古いリリース情報への巻き戻りを防ぐ。</summary>
public static class UpdateReplayGuard
{
    /// <summary>これまでに受け入れた最高の版以上なら受け入れる。</summary>
    public static bool Accept(UpdateManifest manifest, ReleaseVersion? highestSeen) =>
        highestSeen is null || manifest.Version.CompareTo(highestSeen) >= 0;
}
