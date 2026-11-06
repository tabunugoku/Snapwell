namespace Snapwell.Platform.Hooks;

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
internal sealed class HookRehookPolicy
{
    /// <summary>OS の最後の入力が、フックの最後のコールバックよりこれ以上新しければ疑う。</summary>
    internal const uint InputGraceMs = 1500;

    /// <summary>どの理由でも、張り直しの間隔の下限。連打を避ける。</summary>
    internal const uint MinIntervalMs = 3000;

    /// <summary>疑いによる張り直しの、最初のバックオフ。</summary>
    internal const uint InitialBackoffMs = 10_000;

    /// <summary>疑いによる張り直しのバックオフの上限。</summary>
    internal const uint MaxBackoffMs = 600_000;

    private readonly object _gate = new();
    private bool _locked;
    private bool _suspended;
    private bool _pending;
    private bool _hasRehooked;
    private uint _lastRehookTick;
    private uint _backoffMs;

    /// <summary>環境の変化を受ける。復帰は張り直しの予約になり、間隔が空くまで次の判断が持ち越す。</summary>
    internal void Notify(HookEnvironmentChange change)
    {
        lock (_gate)
        {
            // ロックとサスペンドは別々に持つ。復帰がロック画面の途中で届いても、解除まで検知を止める。
            switch (change)
            {
                case HookEnvironmentChange.Locked: _locked = true; break;
                case HookEnvironmentChange.Suspended: _suspended = true; break;
                case HookEnvironmentChange.Unlocked: _locked = false; break;
                case HookEnvironmentChange.Resumed: _suspended = false; break;
            }

            _pending = change is HookEnvironmentChange.Unlocked or HookEnvironmentChange.Resumed;
        }
    }

    /// <summary>今張り直すべきかを判断し、張り直すならその時刻を記録する。</summary>
    internal HookRehookReason Evaluate(uint now, uint lastHookTick, uint lastOsInputTick)
    {
        lock (_gate)
        {
            if (_locked || _suspended)
            {
                return HookRehookReason.None;
            }

            // 張り直しのあとにコールバックが来たら、フックは生きている。疑いのバックオフを戻す。
            if (_hasRehooked && Newer(lastHookTick, _lastRehookTick))
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
                return Approve(now, HookRehookReason.Environment);
            }

            if (unchecked((int)(lastOsInputTick - lastHookTick)) > InputGraceMs &&
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
        _lastRehookTick = now;
        return reason;
    }

    private static bool Newer(uint value, uint baseline) => unchecked((int)(value - baseline)) > 0;
}
