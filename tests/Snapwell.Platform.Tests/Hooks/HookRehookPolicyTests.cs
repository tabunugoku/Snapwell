using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>張り直しの判断。時計とフック・OS の入力時刻はテストが渡す。</summary>
public sealed class HookRehookPolicyTests
{
    private const uint Start = 1_000_000;

    [Fact]
    public void NoInputMeansNoRehook()
    {
        var policy = new HookRehookPolicy();
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 600_000, Start, Start - 1));
    }

    [Fact]
    public void InputThatReachedTheHookMeansNoRehook()
    {
        var policy = new HookRehookPolicy();
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 10_000, Start + 9_990, Start + 10_000));
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(Start + 10_000, Start + 9_000, Start + 9_000 + HookRehookPolicy.InputGraceMs));
    }

    [Fact]
    public void OsInputWithoutCallbackBeyondGraceRehooks()
    {
        var policy = new HookRehookPolicy();
        var reason = policy.Evaluate(Start + 10_000, Start, Start + HookRehookPolicy.InputGraceMs + 1);
        Assert.Equal(HookRehookReason.InputMismatch, reason);
    }

    [Fact]
    public void NoRefireRightAfterRehook()
    {
        var policy = new HookRehookPolicy();
        var now = Start + 10_000;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, Start, now - 100));
        // 基準を now に戻したあとも OS の入力が続いても、間隔が空くまで張り直さない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(now + 1_000, now, now + 1_000));
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(now + HookRehookPolicy.InitialBackoffMs - 1, now, now + 9_000));
    }

    [Fact]
    public void RepeatedSuspicionBacksOffExponentiallyUpToTheCap()
    {
        var policy = new HookRehookPolicy();
        var now = Start;
        var expected = HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, now - 5_000, now));
        for (var round = 0; round < 12; round++)
        {
            // フックは沈黙したまま、OS の入力だけが続く。
            var lastHook = now;
            now += expected - 1;
            Assert.Equal(HookRehookReason.None, policy.Evaluate(now, lastHook, now));
            now += 1;
            Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(now, lastHook, now));
            expected = Math.Min(expected * 2, HookRehookPolicy.MaxBackoffMs);
        }

        Assert.Equal(HookRehookPolicy.MaxBackoffMs, expected);
    }

    [Fact]
    public void CallbackAfterRehookResetsBackoff()
    {
        var policy = new HookRehookPolicy();
        var first = Start;
        policy.Evaluate(first, first - 5_000, first);
        // フックは沈黙したまま。二度目の疑いでバックオフが倍（20 秒）になる。
        var second = first + HookRehookPolicy.InitialBackoffMs;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(second, first, second));
        // 倍のバックオフの途中で、コールバックが来た。生きているので、バックオフは最初に戻る。
        var alive = second + 2_000;
        Assert.Equal(HookRehookReason.None, policy.Evaluate(alive, alive, alive));
        // 最初の間隔（10 秒）より短い 5 秒後でも、次の疑いは張り直せる。戻っていなければ 20 秒待たされる。
        var third = alive + 3_000;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(third, second, third));
    }

    [Fact]
    public void TickCounterWrapAroundIsHandled()
    {
        var policy = new HookRehookPolicy();
        // 一周する直前にフックが最後に呼ばれ、一周した直後に OS の入力があった。
        var lastHook = uint.MaxValue - 100;
        var lastInput = 5_000u;
        Assert.Equal(HookRehookReason.InputMismatch, policy.Evaluate(6_000, lastHook, lastInput));

        var other = new HookRehookPolicy();
        // 一周をまたいでも、フックが新しければ疑わない。
        Assert.Equal(HookRehookReason.None, other.Evaluate(6_000, 5_000, uint.MaxValue - 100));
    }

    [Fact]
    public void RestoredIsApprovedImmediatelyAtFirstAndDebouncedAfterwards()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Restored);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start, Start, Start));

        // 復帰とロック解除が続けて来ても、連打せず、間隔が空いてから一度だけ張り直す。
        policy.Notify(HookEnvironmentChange.Restored);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 1_000, Start, Start));
        Assert.Equal(HookRehookReason.Environment,
            policy.Evaluate(Start + HookRehookPolicy.MinIntervalMs, Start, Start));
        Assert.Equal(HookRehookReason.None,
            policy.Evaluate(Start + HookRehookPolicy.MinIntervalMs * 3, Start, Start));
    }

    [Fact]
    public void DetectionIsPausedWhileInterruptedAndResumesWithARehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Interrupted);
        // ロック中は OS の入力があっても疑わない。
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 60_000, Start, Start + 60_000));
        policy.Notify(HookEnvironmentChange.Restored);
        Assert.Equal(HookRehookReason.Environment, policy.Evaluate(Start + 61_000, Start, Start));
    }

    [Fact]
    public void InterruptionCancelsAPendingRehook()
    {
        var policy = new HookRehookPolicy();
        policy.Notify(HookEnvironmentChange.Restored);
        policy.Evaluate(Start, Start, Start);
        policy.Notify(HookEnvironmentChange.Restored);
        policy.Notify(HookEnvironmentChange.Interrupted);
        Assert.Equal(HookRehookReason.None, policy.Evaluate(Start + 10_000, Start, Start));
    }
}
