using Snapwell.Core.Geometry;

namespace Snapwell.Core.Physics;

/// <summary>
/// 一枚のウィンドウの位置とサイズを運ぶ物体。feel の Body2D の移植で、位置は RK4・刻み 1/240 秒で進める。
/// 停止点を渡すと feel の磁石場（目標のばねなし）、渡さないと feel の to(p) と同じ目標へのばねで動く。
/// </summary>
public sealed class WindowBody
{
    /// <summary>磁石場の吸着半径（物理ピクセル）の既定値。feel の corner-card の clickRadius。</summary>
    public const double DefaultClickRadius = 28;

    private const double RestSpeed = 4;
    private const double RestDistance = 0.25;
    private const double WallFrequency = 8;
    private const double WallDampingRatio = 0.9;
    private const double MaximumStep = 1.0 / 240;

    private Vec2[]? _stops;

    /// <summary>現在の矩形・目標・壁と初速度から物体を作る。</summary>
    /// <param name="current">現在の矩形。</param>
    /// <param name="target">吸着先の矩形。</param>
    /// <param name="walls">移動を制限する作業領域。</param>
    /// <param name="feel">物理パラメーター。</param>
    /// <param name="velocity">位置の初速度。</param>
    /// <param name="stops">磁石場の停止点（左上の座標）。null なら目標へのばねで動く。</param>
    /// <param name="clickRadius">磁石場の吸着半径。物理ピクセル。</param>
    public WindowBody(RectD current, RectD target, RectD walls, FeelPreset feel,
        Vec2 velocity = default, IReadOnlyList<Vec2>? stops = null, double clickRadius = DefaultClickRadius)
    {
        ArgumentNullException.ThrowIfNull(feel);
        if (!double.IsFinite(clickRadius) || clickRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clickRadius));
        }

        if (stops is { Count: 0 })
        {
            throw new ArgumentException("停止点は一つ以上必要。", nameof(stops));
        }

        State = current;
        Target = target;
        Walls = walls;
        Feel = feel;
        Velocity = velocity;
        ClickRadius = clickRadius;
        _stops = stops?.ToArray();
    }

    /// <summary>現在の位置とサイズ。</summary>
    public RectD State { get; private set; }

    /// <summary>位置の速度。単位は px/s。</summary>
    public Vec2 Velocity { get; private set; }

    /// <summary>吸着先の位置とサイズ。</summary>
    public RectD Target { get; private set; }

    /// <summary>移動を制限する作業領域。</summary>
    public RectD Walls { get; }

    /// <summary>物理パラメーター。</summary>
    public FeelPreset Feel { get; }

    /// <summary>磁石場の吸着半径。物理ピクセル。</summary>
    public double ClickRadius { get; }

    /// <summary>磁石場の停止点。null なら目標へのばねで動く。</summary>
    public IReadOnlyList<Vec2>? Stops => _stops;

    /// <summary>静止と見なせる速さと位置誤差になったか。</summary>
    public bool IsSettled
    {
        get
        {
            var rest = Rest(State.X, State.Y);
            return Velocity.Length < RestSpeed && (rest - new Vec2(State.X, State.Y)).Length < RestDistance;
        }
    }

    /// <summary>速さに応じた代理描画の浮き上がり量。</summary>
    public double Lift => Math.Min(1, Velocity.Length / 3000);

    /// <summary>秒単位の刻みを RK4 で進める。</summary>
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

        var count = Math.Max(1, (int)Math.Ceiling(dt / MaximumStep));
        var h = dt / count;
        for (var i = 0; i < count; i++)
        {
            StepOnce(h);
        }
    }

    /// <summary>最寄りの停止点（目標のばねなら目標）に正確に合わせて速度をゼロにし、サイズは目標に合わせる。</summary>
    public void Settle()
    {
        var rest = Rest(State.X, State.Y);
        State = new(rest.X, rest.Y, Target.W, Target.H);
        Target = State;
        Velocity = default;
    }

    /// <summary>モーション削減のため直ちに目標を確定する。</summary>
    public void JumpToTarget()
    {
        Retarget(Target);
        Settle();
    }

    /// <summary>現在の状態と速度を保ったまま吸着先を変える。</summary>
    public void Retarget(RectD target)
    {
        Target = target;
        if (_stops is not null)
        {
            _stops = [new(target.X, target.Y)];
        }
    }

    /// <summary>現在の速度のまま磁石場が止める停止点（左上）を返す。上限は既定で 6 秒。</summary>
    public Vec2 PredictRest(double horizonSeconds = 6)
    {
        var ghost = new WindowBody(State, Target, Walls, Feel, Velocity, _stops, ClickRadius);
        for (var t = 0.0; t < horizonSeconds; t += 1.0 / 60)
        {
            ghost.Step(1.0 / 60);
            if (ghost.IsSettled)
            {
                break;
            }
        }

        return ghost.Rest(ghost.State.X, ghost.State.Y);
    }

    private Vec2 Rest(double x, double y)
    {
        if (_stops is null)
        {
            return new(Target.X, Target.Y);
        }

        var best = _stops[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var stop in _stops)
        {
            var distance = (stop.X - x) * (stop.X - x) + (stop.Y - y) * (stop.Y - y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = stop;
            }
        }

        return best;
    }

    private void StepOnce(double h)
    {
        var size = new Vec2(State.W, State.H);
        var bounds = Bounds(size);
        var mass = Feel.Mass;
        Vec2 Acceleration(Vec2 position, Vec2 velocity) => Force(position, velocity, bounds) * (1 / mass);

        var p = new Vec2(State.X, State.Y);
        var v = Velocity;
        var a1 = Acceleration(p, v);
        var v2 = v + a1 * (h / 2);
        var a2 = Acceleration(p + v * (h / 2), v2);
        var v3 = v + a2 * (h / 2);
        var a3 = Acceleration(p + v2 * (h / 2), v3);
        var v4 = v + a3 * h;
        var a4 = Acceleration(p + v3 * h, v4);
        var position = p + (v + v2 * 2 + v3 * 2 + v4) * (h / 6);
        Velocity = v + (a1 + a2 * 2 + a3 * 2 + a4) * (h / 6);

        State = new(position.X, position.Y, size.X, size.Y);
    }

    private Vec2 Force(Vec2 position, Vec2 velocity, (double MinX, double MaxX, double MinY, double MaxY) bounds)
    {
        var force = velocity * -Feel.Damping;
        if (_stops is null)
        {
            force -= (position - new Vec2(Target.X, Target.Y)) * Feel.Stiffness;
        }
        else
        {
            var offset = position - Rest(position.X, position.Y);
            force -= offset * MagnetStiffness(offset.Length);
        }

        return force + new Vec2(Wall(position.X, velocity.X, bounds.MinX, bounds.MaxX),
            Wall(position.Y, velocity.Y, bounds.MinY, bounds.MaxY));
    }

    // 遠くまで届くばねに、吸着半径の内側で滑らかに効く強いばねを足す。
    private double MagnetStiffness(double distance)
    {
        var far = Feel.Mass * Math.Pow(2 * Math.PI * Feel.ReachFrequency, 2);
        var near = Feel.Mass * Math.Pow(2 * Math.PI * Feel.ClickFrequency, 2);
        return far + near * SmoothStep(1 - distance / ClickRadius);
    }

    private static double SmoothStep(double e) => e <= 0 ? 0 : e >= 1 ? 1 : e * e * (3 - 2 * e);

    // 壁の外へ出た分だけ押し返す、硬く減衰の効いたばね。押し込むときだけ減衰させ、引き留めない。
    private double Wall(double position, double velocity, double minimum, double maximum)
    {
        var stiffness = Feel.Mass * Math.Pow(2 * Math.PI * WallFrequency, 2);
        var damping = 2 * WallDampingRatio * Math.Sqrt(stiffness * Feel.Mass);
        if (position < minimum)
        {
            return -stiffness * (position - minimum) - (velocity < 0 ? damping * velocity : 0);
        }

        if (position > maximum)
        {
            return -stiffness * (position - maximum) - (velocity > 0 ? damping * velocity : 0);
        }

        return 0;
    }

    // 目標や停止点が壁の外にあっても、そこへ向かう動きを壁が止めない。
    private (double MinX, double MaxX, double MinY, double MaxY) Bounds(Vec2 size)
    {
        double minX = Walls.X, maxX = Walls.Right - size.X, minY = Walls.Y, maxY = Walls.Bottom - size.Y;
        if (_stops is null)
        {
            return (Math.Min(minX, Target.X), Math.Max(maxX, Target.X), Math.Min(minY, Target.Y),
                Math.Max(maxY, Target.Y));
        }

        foreach (var stop in _stops)
        {
            minX = Math.Min(minX, stop.X);
            maxX = Math.Max(maxX, stop.X);
            minY = Math.Min(minY, stop.Y);
            maxY = Math.Max(maxY, stop.Y);
        }

        return (minX, maxX, minY, maxY);
    }
}
