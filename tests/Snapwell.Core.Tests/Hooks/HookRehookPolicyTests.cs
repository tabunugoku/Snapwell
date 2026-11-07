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
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 600_000, Start, hookSilent: false));
    }

    [Fact]
    public void NoRefireRightAfterRehook()
    {
        var policy = new HookRehookPolicy();
        var now = Start + 10_000;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, Start, hookSilent: true));
        // 基準を now に戻したあとも OS の入力が続いても、間隔が空くまで張り直さない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(now + 1_000, now, hookSilent: true));
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(now + HookRehookPolicy.InitialBackoffMs - 1, now, hookSilent: true));
    }

    [Fact]
    public void RepeatedSuspicionBacksOffExponentiallyUpToTheCap()
    {
        var policy = new HookRehookPolicy();
        var now = Start;
        var expected = HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, now - 5_000, hookSilent: true));
        for (var round = 0; round < 12; round++)
        {
            // フックは沈黙したまま、OS の入力だけが続く。
            var lastHook = now;
            now += expected - 1;
            Assert.Equal(HookRehookReason.None, policy.Evaluate(now, lastHook, hookSilent: true));
            now += 1;
            Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, lastHook, hookSilent: true));
            expected = Math.Min(expected * 2, HookRehookPolicy.MaxBackoffMs);
        }

        Assert.Equal(HookRehookPolicy.MaxBackoffMs, expected);
    }

    [Fact]
    public void CallbackAfterRehookResetsBackoff()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        policy.Evaluate(first, first - 5_000, hookSilent: true);
        // フックは沈黙したまま。二度目の疑いでバックオフが倍（20 秒）になる。
        var second = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(second, first, hookSilent: true));
        // 倍のバックオフの途中で、コールバックが来た。生きているので、バックオフは最初に戻る。
        var alive = second + 2_000;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(alive, alive, hookSilent: false));
        // 最初の間隔（10 秒）より短い 5 秒後でも、次の疑いは張り直せる。戻っていなければ 20 秒待たされる。
        var third = alive + 3_000;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(third, second, hookSilent: true));
    }

    [Fact]
    public void TickCounterWrapAroundIsHandled()
    {
        var policy = new HookRehookPolicy();
        // 一周する直前に張り直す。
        var rehook = uint.MaxValue - 50;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(rehook, rehook - 5_000, hookSilent: true));
        // 一周したあとにコールバックが来た。張り直しより新しいので、バックオフは最初に戻る。
        var wrapped = unchecked(rehook + 4_000);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(wrapped, 100, hookSilent: false));
        // 戻っていれば、最小の間隔 (3 秒) だけ空けば張り直せる。戻っていなければ 10 秒待たされる。
        Assert.Equal(HookRehookReason.InputMismatch,
            policy.Evaluate(unchecked(wrapped + 3_000), 100, hookSilent: true));
    }

    [Fact]
    public void RestoredIsApprovedImmediatelyAndOverlappingRestoresAreCoalesced()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start, Start, hookSilent: false));

        // 自動復帰・ユーザー操作での復帰・ロック解除が続けて届いても、張り直しは 1 回にまとめる。
        policy.Notify(HookEnvironmentChange.Resumed, Start + 100);
        policy.Notify(HookEnvironmentChange.Unlocked, Start + 2_000);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + HookRehookPolicy.MinIntervalMs, Start, hookSilent: false));

        // まとめる時間を過ぎたあとの復帰は、改めて張り直す。
        var later = Start + HookRehookPolicy.EnvironmentCoalesceMs;
        policy.Notify(HookEnvironmentChange.Unlocked, later);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(later, Start, hookSilent: false));
    }

    [Fact]
    public void PauseExpiresWhenTheMatchingNotificationNeverArrives()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        var input = Start + HookRehookPolicy.PauseLimitMs - 1;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(input, Start, hookSilent: true));
        // 復帰の通知が届かないまま上限を過ぎたら、検知に戻る。
        var expired = Start + HookRehookPolicy.PauseLimitMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(expired, Start, hookSilent: true));
    }

    [Fact]
    public void DetectionIsPausedWhileInterruptedAndResumesWithARehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Locked, Start);
        // ロック中は OS の入力があっても疑わない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, Start, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, Start, hookSilent: false));
    }

    [Fact]
    public void InterruptionCancelsAPendingRehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        policy.Evaluate(Start, Start, hookSilent: false);
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 10_000, Start, hookSilent: false));
    }

    [Fact]
    public void ResumeWhileStillLockedKeepsDetectionPausedUntilUnlock()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        // 復帰がロック画面の途中で届いても、解除までは入力があっても疑わない。
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, Start, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, Start, hookSilent: false));
    }

    [Fact]
    public void UnlockWhileStillSuspendedKeepsDetectionPausedUntilResume()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Suspended, Start);
        policy.Notify(HookEnvironmentChange.Locked, Start);
        policy.Notify(HookEnvironmentChange.Unlocked, Start);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, Start, hookSilent: true));
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, Start, hookSilent: false));
    }

    /// <summary>復帰の張り直しのあと、間を置いて 1 回だけ張り直す。</summary>
    [Fact]
    public void EnvironmentRehookIsFollowedByExactlyOneDelayedRetry()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Resumed, Start);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start, Start, hookSilent: false));
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 4_000, Start, hookSilent: false));
        var retry = Start + HookRehookPolicy.EnvironmentFollowUpMs;
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(retry, Start, hookSilent: false));
        Assert.Equal(HookRehookReason.None, policy.Evaluate(retry + 60_000, Start, hookSilent: false));
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
        Assert.Equal(HookRehookReason.None, policy.Evaluate(now, Start, hookSilent: true));
    }

    [Fact]
    public void OneSilentHookRehooksWithoutMismatchAndKeepsBackoffWhileOtherHookIsAlive()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        // 時刻は食い違わない（もう片方のフックが更新している）が、片方が黙っている。
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(first, first, hookSilent: true));
        // 張り直しで直らず、もう片方のコールバックだけが続く。バックオフは戻らず、10 秒は待つ。
        var soon = first + HookRehookPolicy.MinIntervalMs;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(soon, soon, hookSilent: true));
        var later = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(later, later, hookSilent: true));
    }
}
