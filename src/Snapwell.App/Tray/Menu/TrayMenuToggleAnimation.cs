using Snapwell.Core.Physics;

namespace Snapwell.App.Tray.Menu;

/// <summary>トレイ STA 上で使い、静止・非表示のときはフレームの要求を止める。</summary>
internal sealed class TrayMenuToggleAnimation(Func<double> clock, Func<bool> reduceMotion, Action<bool> schedule)
{
    private SpringSwitch? _body;
    private bool _scheduled;
    private double _last;

    internal double Position => _body?.Position ?? 0;
    internal double Velocity => _body?.Velocity ?? 0;
    internal double Fill => _body?.Fill ?? 0;
    internal double Stretch => _body?.Stretch ?? 1;
    // 反転直後に静止の許容範囲へ入っていても、Advance が位置と速度を確定する一回分は残す。
    internal bool NeedsFrame => _body is { } body &&
        (body.Position != (body.IsOn ? body.Travel : 0) || body.Velocity != 0);

    internal void Show(bool enabled)
    {
        if (_body is null)
        {
            // 開いた瞬間は現在の状態へ直接置き、以前の表示の速度を持ち越さない。
            _body = new(20, FeelPreset.StiffHeavy, enabled);
        }
        else
        {
            _body.Set(enabled);
        }

        if (reduceMotion()) { _body.SettleNow(); }
        Schedule();
    }

    internal bool Frame()
    {
        if (!NeedsFrame) { return false; }
        var now = clock();
        var seconds = Math.Clamp(now - _last, 0, 1.0 / 30);
        _last = now;
        if (reduceMotion()) { _body!.SettleNow(); }
        else { _body!.Advance(seconds); }
        Schedule();
        return true;
    }

    internal void Close()
    {
        _body = null;
        Schedule();
    }

    private void Schedule()
    {
        var needed = NeedsFrame;
        if (_scheduled == needed) { return; }
        if (needed) { _last = clock(); }
        // 開始に失敗しても Close が停止を試みられるよう、先に状態を更新する。
        _scheduled = needed;
        schedule(needed);
    }
}
