using Snapwell.Core.Hooks;

namespace Snapwell.Core.Tests.Hooks;

/// <summary>張り直しの判断。時計とフック・OS の入力時刻はテストが渡す。</summary>
public sealed class HookRehookPolicyTests
{
    private const uint Start = 1_000_000;

    [Fact]
    public void NoSilenceEvidenceMeansNoRehook()
    {
        var policy = new HookRehookPolicy();
        // フックが長く呼ばれていなくても、黙っている証拠がなければ疑わない（タッチ・ペンの操作は OS の入力だけを進める）。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 600_000, hookSilent: false));
    }

    [Fact]
    public void NoRefireRightAfterRehook()
    {
        var policy = new HookRehookPolicy();
        var now = Start + 10_000;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, hookSilent: true));
        // 基準を now に戻したあとも OS の入力が続いても、間隔が空くまで張り直さない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(now + 1_000, hookSilent: true));
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(now + HookRehookPolicy.InitialBackoffMs - 1, hookSilent: true));
    }

    [Fact]
    public void RepeatedSuspicionBacksOffExponentiallyUpToTheCap()
    {
        var policy = new HookRehookPolicy();
        var now = Start;
        var expected = HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, hookSilent: true));
        for (var round = 0; round < 12; round++)
        {
            // フックは沈黙したまま、OS の入力だけが続く。
            var lastHook = now;
            now += expected - 1;
            Assert.Equal(HookRehookReason.None, policy.Evaluate(now, hookSilent: true));
            now += 1;
            Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, hookSilent: true));
            expected = Math.Min(expected * 2, HookRehookPolicy.MaxBackoffMs);
        }

        Assert.Equal(HookRehookPolicy.MaxBackoffMs, expected);
    }

    [Fact]
    public void BackoffIsResetOnlyAfterSuspicionStaysAwayForAQuietPeriod()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        policy.Evaluate(first, hookSilent: true);
        // 疑いが続く。二度目の疑いでバックオフが倍（20 秒）になる。
        var second = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(second, hookSilent: true));
        // 疑いが静かな期間の途中で戻っても、バックオフは戻らない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(second + HookRehookPolicy.QuietResetMs - 1, hookSilent: false));
        // 静かな期間が過ぎたら戻る。10 秒後の疑いで張り直せる（戻っていなければ 20 秒待たされる）。
        var quiet = second + HookRehookPolicy.QuietResetMs;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(quiet, hookSilent: false));
        var third = quiet + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(third, hookSilent: true));
    }

    /// <summary>管理者権限のウィンドウのように、片方が黙り続けて証拠が途切れ途切れに出る場面でも、間隔が縮まない。</summary>
    [Fact]
    public void IntermittentSuspicionDoesNotCollapseTheBackoff()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(first, hookSilent: true));
        // 4 秒後に、証拠が一瞬なくなり (もう片方のコールバックは来ている)、また成り立つ。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(first + 3_000, hookSilent: false));
        Assert.Equal(HookRehookReason.None, policy.Evaluate(first + 4_000, hookSilent: true));
        // 10 秒たつまで張り直さない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(first + HookRehookPolicy.InitialBackoffMs - 1, hookSilent: true));
        var second = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(second, hookSilent: true));
        // 次は 20 秒。
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(second + (2 * HookRehookPolicy.InitialBackoffMs) - 1, hookSilent: true));
        Assert.Equal(HookRehookReason.InputMismatch,
            policy.Evaluate(second + (2 * HookRehookPolicy.InitialBackoffMs), hookSilent: true));
    }

    [Fact]
    public void TickCounterWrapAroundIsHandled()
    {
        var policy = new HookRehookPolicy();
        // 一周する直前に張り直す。
        var rehook = uint.MaxValue - 50;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(rehook, hookSilent: true));
        // 二度目の疑い (10 秒後。時計は一周している) でバックオフが倍になる。
        var second = unchecked(rehook + HookRehookPolicy.InitialBackoffMs);
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(second, hookSilent: true));
        // 静かな期間が一周をまたいで過ぎると、バックオフは最初に戻る。
        var quiet = unchecked(second + HookRehookPolicy.QuietResetMs);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(quiet, hookSilent: false));
        Assert.Equal(HookRehookReason.InputMismatch,
            policy.Evaluate(unchecked(quiet + HookRehookPolicy.InitialBackoffMs), hookSilent: true));
    }

    [Fact]
    public void RestoredIsApprovedImmediatelyAndOverlappingRestoresAreCoalesced()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start, hookSilent: false));

        // 自動復帰・ユーザー操作での復帰・ロック解除が続けて届いても、張り直しは 1 回にまとめる。
        policy.Notify(HookEnvironmentChange.Resumed, Start + 100);
        policy.Notify(HookEnvironmentChange.Unlocked, Start + 2_000);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + HookRehookPolicy.MinIntervalMs, hookSilent: false));

        // まとめる時間を過ぎたあとの復帰は、改めて張り直す。
        var later = Start + HookRehookPolicy.EnvironmentCoalesceMs;
        policy.Notify(HookEnvironmentChange.Unlocked, later);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(later, hookSilent: false));
    }

    [Fact]
    public void PauseExpiresWhenTheMatchingNotificationNeverArrives()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        var input = Start + HookRehookPolicy.PauseLimitMs - 1;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(input, hookSilent: true));
        // 復帰の通知が届かないまま上限を過ぎたら、検知に戻る。
        var expired = Start + HookRehookPolicy.PauseLimitMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(expired, hookSilent: true));
    }

    [Fact]
    public void DetectionIsPausedWhileInterruptedAndResumesWithARehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Locked, Start);
        // ロック中は OS の入力があっても疑わない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, hookSilent: false));
    }

    [Fact]
    public void InterruptionCancelsAPendingRehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        policy.Evaluate(Start, hookSilent: false);
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 10_000, hookSilent: false));
    }

    [Fact]
    public void ResumeWhileStillLockedKeepsDetectionPausedUntilUnlock()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        // 復帰がロック画面の途中で届いても、解除までは入力があっても疑わない。
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, hookSilent: false));
    }

    [Fact]
    public void UnlockWhileStillSuspendedKeepsDetectionPausedUntilResume()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, hookSilent: false));
    }

    /// <summary>復帰の張り直しのあと、間を置いて 1 回だけ張り直す。</summary>
    [Fact]
    public void EnvironmentRehookIsFollowedByExactlyOneDelayedRetry()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start, hookSilent: false));
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 4_000, hookSilent: false));
        var retry = Start + HookRehookPolicy.EnvironmentFollowUpMs;
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(retry, hookSilent: false));
        Assert.Equal(HookRehookReason.None, policy.Evaluate(retry + 60_000, hookSilent: false));
    }

    /// <summary>ロックとサスペンドの停止の上限は別々に数える。</summary>
    [Fact]
    public void PauseLimitIsCountedPerSource()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Locked, Start);
        var suspendedAt = Start + HookRehookPolicy.PauseLimitMs - 10_000;
        policy.Notify(HookEnvironmentChange.Suspended, suspendedAt);
        var now = Start + HookRehookPolicy.PauseLimitMs;
        // ロックは上限に達したが、サスペンドは入ったばかりなので、まだ止めておく。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(now, hookSilent: true));
    }

    [Fact]
    public void SilentHookRehooksAndWaitsForTheBackoffWhileTheEvidenceContinues()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        // 片方が黙っている（もう片方のフックのコールバックは来ている）。
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(first, hookSilent: true));
        // 張り直しで直らず、黙っている証拠が続く。10 秒は待つ。
        var soon = first + HookRehookPolicy.MinIntervalMs;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(soon, hookSilent: true));
        var later = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(later, hookSilent: true));
    }
}
