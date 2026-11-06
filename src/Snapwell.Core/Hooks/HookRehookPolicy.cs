namespace Snapwell.Core.Hooks;

/// <summary>セッションや電源の状態の変化。フックが外されうる前後を表す。</summary>
public enum HookEnvironmentChange
{
    /// <summary>ロックまたは切断。入力がフックへ届かない期間の始まり。</summary>
    Locked,

    /// <summary>ロック解除または接続。フックを張り直す契機。</summary>
    Unlocked,

    /// <summary>サスペンド。</summary>
    Suspended,

    /// <summary>スリープからの復帰。ロック画面へ戻ることが多いので、ロックの状態は別に持つ。</summary>
    Resumed,
}

/// <summary>張り直しを求めた理由。</summary>
public enum HookRehookReason
{
    /// <summary>張り直さない。</summary>
    None,

    /// <summary>OS には入力があるのに、フックのコールバックが来ない。</summary>
    InputMismatch,

    /// <summary>ロック解除・接続・復帰のあと。</summary>
    Environment,
}

/// <summary>
/// 低レベルフックを張り直すかどうかの判断。Win32 に触れず、時刻は呼び出し側が渡す。
/// 時刻は GetTickCount 相当のミリ秒（uint）で、約 49.7 日で一周するため差は符号つきで比べる。
/// </summary>
/// <remarks>
/// 低レベルフックは、コールバックが LowLevelHooksTimeout を超えたときなどに、通知なく OS に外される。
/// 外されたことを直接知る手段はないので、「OS が見た最後の入力」と「自分のコールバックが最後に呼ばれた時刻」の
/// 食い違いで疑う。昇格したウィンドウへの入力のように、フックに届かない正常な場合にも食い違うため、
/// 疑いによる張り直しには指数バックオフをかける。コールバックが再び来たらバックオフを戻す。
/// </remarks>
public sealed class HookRehookPolicy
{
    /// <summary>OS の最後の入力が、フックの最後のコールバックよりこれ以上新しければ疑う。</summary>
    public const uint InputGraceMs = 1500;

    /// <summary>どの理由でも、張り直しの間隔の下限。連打を避ける。</summary>
    public const uint MinIntervalMs = 3000;

    /// <summary>疑いによる張り直しの、最初のバックオフ。</summary>
    public const uint InitialBackoffMs = 10_000;

    /// <summary>疑いによる張り直しのバックオフの上限。</summary>
    public const uint MaxBackoffMs = 600_000;

    /// <summary>ロックやサスペンドで検知を止めておく上限。対の通知が届かなくても、この時間で検知に戻る。</summary>
    public const uint PauseLimitMs = 600_000;

    /// <summary>復帰の通知は重なって届く（自動復帰・ユーザー操作での復帰・ロック解除）。この間は 1 回にまとめる。</summary>
    public const uint EnvironmentCoalesceMs = 10_000;

    /// <summary>復帰の張り直しの直後は、入力の仕組みがまだ整っていないかもしれない。この時間のあとに 1 回だけ張り直す（実機未確認）。</summary>
    public const uint EnvironmentFollowUpMs = 5_000;

    private readonly object _gate = new();
    private bool _locked;
    private bool _suspended;
    private bool _pending;
    private bool _hasRehooked;
    private bool _lastWasEnvironment;
    private bool _followUp;
    private uint _lockedSince;
    private uint _suspendedSince;
    private uint _lastRehookTick;
    private uint _backoffMs;

    /// <summary>環境の変化を受ける。復帰は張り直しの予約になり、間隔が空くまで次の判断が持ち越す。</summary>
    public void Notify(HookEnvironmentChange change, uint now)
    {
        lock (_gate)
        {
            // ロックとサスペンドは別々に持つ。復帰がロック画面の途中で届いても、解除まで検知を止める。
            switch (change)
            {
                case HookEnvironmentChange.Locked:
                    // 停止の上限は、ロックとサスペンドで別々に数える。
                    _lockedSince = _locked ? _lockedSince : now;
                    _locked = true;
                    break;
                case HookEnvironmentChange.Suspended:
                    _suspendedSince = _suspended ? _suspendedSince : now;
                    _suspended = true;
                    break;
                case HookEnvironmentChange.Unlocked: _locked = false; break;
                case HookEnvironmentChange.Resumed: _suspended = false; break;
            }

            var restored = change is HookEnvironmentChange.Unlocked or HookEnvironmentChange.Resumed;
            var coalesced = _hasRehooked && _lastWasEnvironment && unchecked(now - _lastRehookTick) < EnvironmentCoalesceMs;
            _pending = restored && !coalesced;
        }
    }

    /// <summary>今張り直すべきかを判断し、張り直すならその時刻を記録する。</summary>
    /// <param name="now">現在の時刻。</param>
    /// <param name="lastHookTick">キーボードとマウスのどちらかのコールバックが最後に呼ばれた時刻。</param>
    /// <param name="lastOsInputTick">OS が見た最後の入力の時刻。</param>
    /// <param name="oneHookSilent">
    /// 片方のフックだけ黙っている証拠がある。もう片方が生きていると、時刻の比較では見えないため。
    /// </param>
    public HookRehookReason Evaluate(uint now, uint lastHookTick, uint lastOsInputTick, bool oneHookSilent = false)
    {
        lock (_gate)
        {
            // 対の通知が届かなかったと見て、上限を過ぎたものは検知に戻す。
            _locked &= unchecked(now - _lockedSince) < PauseLimitMs;
            _suspended &= unchecked(now - _suspendedSince) < PauseLimitMs;
            if (_locked || _suspended)
            {
                return HookRehookReason.None;
            }

            // 張り直しのあとにコールバックが来たら、フックは生きている。疑いのバックオフを戻す。
            // 片方が黙ったままなら、もう片方のコールバックが来ても戻さない（張り直しで直らない場合に間引くため）。
            if (_hasRehooked && !oneHookSilent && Newer(lastHookTick, _lastRehookTick))
            {
                _backoffMs = 0;
            }

            var sinceRehook = _hasRehooked ? unchecked(now - _lastRehookTick) : uint.MaxValue;
            if (_pending)
            {
                if (sinceRehook < MinIntervalMs)
                {
                    return HookRehookReason.None;
                }

                _pending = false;
                var approved = Approve(now, HookRehookReason.Environment);
                _followUp = true;
                return approved;
            }

            if (_followUp && sinceRehook >= EnvironmentFollowUpMs)
            {
                return Approve(now, HookRehookReason.Environment);
            }

            var suspected = HookTicks.Diff(lastOsInputTick, lastHookTick) > InputGraceMs || oneHookSilent;
            if (suspected &&
                sinceRehook >= Math.Max(MinIntervalMs, _backoffMs))
            {
                _backoffMs = _backoffMs == 0 ? InitialBackoffMs : Math.Min(_backoffMs * 2, MaxBackoffMs);
                return Approve(now, HookRehookReason.InputMismatch);
            }

            return HookRehookReason.None;
        }
    }

    private HookRehookReason Approve(uint now, HookRehookReason reason)
    {
        _hasRehooked = true;
        _followUp = false;
        _lastWasEnvironment = reason == HookRehookReason.Environment;
        _lastRehookTick = now;
        return reason;
    }

    private static bool Newer(uint value, uint baseline) => HookTicks.Diff(value, baseline) > 0;
}
