namespace Snapwell.Core.Updates;

/// <summary>現在の版と公開された版の比較結果。</summary>
public enum UpdateCheckStatus
{
    /// <summary>新しい版がない。</summary>
    UpToDate,
    /// <summary>案内する新しい版がある。</summary>
    Available,
    /// <summary>利用者がスキップした版。</summary>
    Skipped,
    /// <summary>確認しない開発用ビルド。</summary>
    DevelopmentBuild,
}

/// <summary>更新の案内とサポート対象外の判定。</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateManifest? Manifest = null,
    bool RequiresUpdate = false);

/// <summary>更新の案内と自動確認の時機を判断する。</summary>
public static class UpdateCheckPolicy
{
    /// <summary>起動から最初の自動確認までの待ち時間。</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    /// <summary>自動確認の間隔。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>新しい版だけを案内し、スキップした版とサポート下限を考慮する。</summary>
    public static UpdateCheckResult Evaluate(ReleaseVersion current, UpdateManifest manifest,
        ReleaseVersion? skippedVersion = null)
    {
        if (current.IsDevelopmentBuild) { return new(UpdateCheckStatus.DevelopmentBuild); }
        if (manifest.Version.CompareTo(current) <= 0) { return new(UpdateCheckStatus.UpToDate); }
        if (manifest.Version.Equals(skippedVersion)) { return new(UpdateCheckStatus.Skipped); }
        return new(UpdateCheckStatus.Available, manifest,
            manifest.MinimumSupported is not null && current.CompareTo(manifest.MinimumSupported) < 0);
    }

    /// <summary>起動待ちと確認間隔を満たすか、時計が巻き戻った場合に確認する。</summary>
    public static bool IsDue(DateTimeOffset now, DateTimeOffset? lastCheck, DateTimeOffset startedAt, bool enabled)
    {
        // 加算せず差を取ることで DateTimeOffset の上下限でも桁あふれを避ける。
        if (!enabled || now - startedAt < StartupDelay) { return false; }
        return lastCheck is null || lastCheck.Value > now || now - lastCheck.Value >= CheckInterval;
    }
}
