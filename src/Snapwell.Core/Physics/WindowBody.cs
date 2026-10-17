using Snapwell.Core.Geometry;

namespace Snapwell.Core.Physics;

/// <summary>一枚のウィンドウの位置とサイズをばねと壁で運ぶ物体。</summary>
public sealed class WindowBody
{
    /// <summary>現在の矩形・目標・壁と初速度から物体を作る。</summary>
    public WindowBody(RectD current, RectD target, RectD walls, FeelPreset feel,
        SizeMode sizeMode = SizeMode.OnLanding, Vec2 velocity = default, Vec2 sizeVelocity = default)
    {
        ArgumentNullException.ThrowIfNull(feel);
        if (!Enum.IsDefined(sizeMode))
        {
            throw new ArgumentOutOfRangeException(nameof(sizeMode));
        }

        State = current;
        Target = target;
        Walls = walls;
        Feel = feel;
        SizeMode = sizeMode;
        Velocity = velocity;
        SizeVelocity = sizeMode == SizeMode.Smooth ? sizeVelocity : default;
    }

    /// <summary>現在の位置とサイズ。</summary>
    public RectD State { get; private set; }

    /// <summary>位置の速度。単位は px/s。</summary>
    public Vec2 Velocity { get; private set; }

    /// <summary>幅と高さの速度。単位は px/s。</summary>
    public Vec2 SizeVelocity { get; private set; }

    /// <summary>吸着先の位置とサイズ。</summary>
    public RectD Target { get; private set; }

    /// <summary>移動を制限する作業領域。</summary>
    public RectD Walls { get; }

    /// <summary>ばねの物理パラメーター。</summary>
    public FeelPreset Feel { get; }

    /// <summary>サイズ変更の方式。</summary>
    public SizeMode SizeMode { get; }

    /// <summary>静止と見なせる位置誤差・速さ・サイズ誤差になったか。</summary>
    public bool IsSettled => Math.Abs(State.X - Target.X) < 0.5 && Math.Abs(State.Y - Target.Y) < 0.5 &&
        Velocity.Length < 6 && (SizeMode == SizeMode.OnLanding ||
        (Math.Abs(State.W - Target.W) < 0.5 && Math.Abs(State.H - Target.H) < 0.5));

    /// <summary>速さに応じた代理描画の浮き上がり量。</summary>
    public double Lift => Math.Min(1, Velocity.Length / 3000);

    /// <summary>秒単位の刻みを半陰的オイラー法で進め、壁の外への描画を抑える。</summary>
    public void Step(double dt)
    {
        if (dt <= 0)
        {
            return;
        }

        if (!double.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt));
        }

        var velocity = SpringVelocity(new(State.X, State.Y), new(Target.X, Target.Y), Velocity, dt);
        var position = new Vec2(State.X, State.Y) + velocity * dt;
        var size = new Vec2(State.W, State.H);
        if (SizeMode == SizeMode.Smooth)
        {
            SizeVelocity = SpringVelocity(size, new(Target.W, Target.H), SizeVelocity, dt);
            size += SizeVelocity * dt;
            // 小さい目標への行き過ぎでも寸法は負にしない。
            size = new(Math.Max(0, size.X), Math.Max(0, size.Y));
        }

        var x = ClampToWall(position.X, velocity.X, Math.Min(Walls.X, Target.X),
            Math.Max(Walls.Right - size.X, Target.X));
        var y = ClampToWall(position.Y, velocity.Y, Math.Min(Walls.Y, Target.Y),
            Math.Max(Walls.Bottom - size.Y, Target.Y));
        Velocity = new(x.Velocity, y.Velocity);
        State = new(x.Position, y.Position, size.X, size.Y);
    }

    /// <summary>目標に正確に合わせ、位置とサイズの速度をゼロにする。</summary>
    public void Settle()
    {
        State = Target;
        Velocity = default;
        SizeVelocity = default;
    }

    /// <summary>モーション削減のため直ちに目標を確定する。</summary>
    public void JumpToTarget() => Settle();

    /// <summary>現在の状態と速度を保ったまま吸着先を変える。</summary>
    public void Retarget(RectD target) => Target = target;

    private Vec2 SpringVelocity(Vec2 current, Vec2 target, Vec2 velocity, double dt)
    {
        var force = (target - current) * Feel.Stiffness - velocity * Feel.Damping;
        return velocity + force * (dt / Feel.Mass);
    }

    private static (double Position, double Velocity) ClampToWall(double position, double velocity,
        double minimum, double maximum)
    {
        if (position < minimum)
        {
            return (minimum, Math.Max(0, velocity));
        }

        if (position > maximum)
        {
            return (maximum, Math.Min(0, velocity));
        }

        return (position, velocity);
    }
}
