namespace Snapwell.Core.Physics;

/// <summary>トグルスイッチのつまみを、ばねと壁とハンドばねで動かす物体（feel の heavy-toggle と同じモデル）。</summary>
public sealed class SpringSwitch
{
    private const double FixedStep = 1.0 / 240;
    private const double MaximumFrame = 1.0 / 30;
    private const double HandFrequency = 6;
    private const double RestVelocity = 4;
    private const double RestPosition = 0.25;
    private const double StaleHandSeconds = 0.06;
    private const double MaximumStretch = 0.22;
    private const double StretchSpeed = 2600;

    private double _handPosition;
    private double _handVelocity;
    private double _handTime;
    private double _sinceMove;
    private double _target;

    /// <summary>可動幅・操作感・初期の状態からつまみを作る。</summary>
    public SpringSwitch(double travel, FeelPreset feel, bool isOn)
    {
        ArgumentNullException.ThrowIfNull(feel);
        if (!double.IsFinite(travel) || travel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(travel));
        }

        Travel = travel;
        Feel = feel;
        IsOn = isOn;
        _target = isOn ? travel : 0;
        Position = _target;
    }

    /// <summary>つまみの可動幅。単位は px。</summary>
    public double Travel { get; }

    /// <summary>ばねの物理パラメーター。</summary>
    public FeelPreset Feel { get; }

    /// <summary>つまみの位置。0 がオフ側、<see cref="Travel"/> がオン側。壁で止めるので範囲の外へは出ない。</summary>
    public double Position { get; private set; }

    /// <summary>つまみの速度。単位は px/s。</summary>
    public double Velocity { get; private set; }

    /// <summary>ばねが向かっているほうの状態。</summary>
    public bool IsOn { get; private set; }

    /// <summary>ドラッグでつまみを握っているか。</summary>
    public bool IsHeld { get; private set; }

    /// <summary>トラックの塗りの度合い。位置から 0〜1 で決める。</summary>
    public double Fill => Math.Clamp(Position / Travel, 0, 1);

    /// <summary>速さに応じたつまみの進行方向の伸び。重いつまみはほとんど変形しない。</summary>
    public double Stretch => 1 + Math.Min(MaximumStretch, Math.Abs(Velocity) / (StretchSpeed * Math.Sqrt(Feel.Mass)));

    /// <summary>手を離したあとに自分の勢いだけで進む先（x + v·m/c）。</summary>
    public double Projected => Position + Velocity * Feel.Mass / Feel.Damping;

    /// <summary>握っておらず、目標で静止していると見なせるか。</summary>
    public bool IsSettled => !IsHeld && Math.Abs(Velocity) < RestVelocity && Math.Abs(Position - _target) < RestPosition;

    /// <summary>タップとして確定してよいか。押した点から離れて入力面の外で離したときは取り消す。</summary>
    public static bool IsTapRelease(double x, double y, double width, double height) =>
        x >= 0 && x <= width && y >= 0 && y <= height;

    /// <summary>状態を反対側に切り替える。位置と速度は保つ。</summary>
    public void Toggle() => Set(!IsOn);

    /// <summary>目標を変える。位置と速度は保ち、ばねが運ぶ。</summary>
    public void Set(bool isOn)
    {
        IsOn = isOn;
        _target = isOn ? Travel : 0;
    }

    /// <summary>つまみを握る。手の位置は呼び出し側が「握った時点のつまみ + ポインターの移動量」で渡す。</summary>
    public void Grab(double handPosition, double time)
    {
        IsHeld = true;
        _handPosition = ClampHand(handPosition);
        _handVelocity = 0;
        _handTime = time;
        _sinceMove = 0;
    }

    /// <summary>手を動かす。手の速度は直前の値と混ぜて均す（位置は直接動かさず、ハンドばねが引く）。</summary>
    public void Move(double handPosition, double time)
    {
        if (!IsHeld)
        {
            return;
        }

        var hand = ClampHand(handPosition);
        // 止まっていた手の古い速度は混ぜない。再開した入力は新しい速度だけで始める。
        if (_sinceMove > StaleHandSeconds)
        {
            _handVelocity = 0;
        }

        var dt = time - _handTime;
        if (dt > 0)
        {
            _handVelocity = _handVelocity * 0.35 + (hand - _handPosition) / dt * 0.65;
        }

        _handPosition = hand;
        _handTime = time;
        _sinceMove = 0;
    }

    /// <summary>手を離す。勢いのまま進む先が中央より先の側に決める（手の速度は離すと消えるので判定に使わない）。</summary>
    public void Release()
    {
        if (!IsHeld)
        {
            return;
        }

        IsHeld = false;
        Set(Projected > Travel / 2);
    }

    /// <summary>握りを取り消す。状態は変えず、つまみは今の側へ戻る（スクロールへの移行やキャプチャ喪失用）。</summary>
    public void Cancel()
    {
        if (!IsHeld)
        {
            return;
        }

        IsHeld = false;
        Set(IsOn);
    }

    /// <summary>描画間隔を固定刻みに分割して進め、静止したら目標に合わせて true を返す。</summary>
    public bool Advance(double frameSeconds)
    {
        if (!double.IsFinite(frameSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        }

        var remaining = Math.Min(frameSeconds, MaximumFrame);
        while (remaining > 0)
        {
            var h = Math.Min(remaining, FixedStep);
            Step(h);
            remaining -= h;
        }

        if (!IsSettled)
        {
            return false;
        }

        Settle();
        return true;
    }

    /// <summary>モーション削減のため、握っていなければ直ちに目標へ置く。</summary>
    public void SettleNow()
    {
        if (!IsHeld)
        {
            Settle();
        }
    }

    // 手はトラックの端より先へ置かない。feel は柔らかい壁だけで受けるが、手が遠いと壁がばねに負けてつまみが外へ出るため。
    private double ClampHand(double handPosition) => Math.Clamp(handPosition, 0, Travel);

    private void Settle()
    {
        Position = _target;
        Velocity = 0;
    }

    private void Step(double h)
    {
        _sinceMove += h;
        // 4 次のルンゲ・クッタ。feel の本体と同じ積分。
        var x = Position;
        var v = Velocity;
        var k1x = v;
        var k1v = Acceleration(x, v);
        var k2x = v + h / 2 * k1v;
        var k2v = Acceleration(x + h / 2 * k1x, v + h / 2 * k1v);
        var k3x = v + h / 2 * k2v;
        var k3v = Acceleration(x + h / 2 * k2x, v + h / 2 * k2v);
        var k4x = v + h * k3v;
        var k4v = Acceleration(x + h * k3x, v + h * k3v);
        Position = x + h / 6 * (k1x + 2 * k2x + 2 * k3x + k4x);
        Velocity = v + h / 6 * (k1v + 2 * k2v + 2 * k3v + k4v);

        // 壁はトラックの端で位置と外向きの速度を止める。feel の柔らかい壁は、速い手の速度が残ると
        // 押し越して反動で逆側へ決まるため、ここでは使わない（行き過ぎを受けるだけの用途は同じ）。
        if (Position > Travel)
        {
            Position = Travel;
            Velocity = Math.Min(Velocity, 0);
        }
        else if (Position < 0)
        {
            Position = 0;
            Velocity = Math.Max(Velocity, 0);
        }
    }

    private double Acceleration(double x, double v)
    {
        var force = -Feel.Damping * v;
        if (IsHeld)
        {
            // ハンドばねは質量 1 のつまみに合わせた強さ。重いつまみは指に遅れてついてくる。
            var kh = Math.Pow(2 * Math.PI * HandFrequency, 2);
            var ch = 2 * Math.Sqrt(kh * Feel.Mass);
            // 入力が止まった手は動いていない。最後に測った速度を力として残さない。
            var handVelocity = _sinceMove > StaleHandSeconds ? 0 : _handVelocity;
            force += kh * (_handPosition - x) + ch * (handVelocity - v);
        }
        else
        {
            force -= Feel.Stiffness * (x - _target);
        }

        return force / Feel.Mass;
    }
}
