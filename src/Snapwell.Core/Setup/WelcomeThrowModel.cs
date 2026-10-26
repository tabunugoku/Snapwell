using Snapwell.Core.Physics;

namespace Snapwell.Core.Setup;

/// <summary>左上座標で表すようこその区画。</summary>
public readonly record struct WelcomeCell(double X, double Y, double Width, double Height);

/// <summary>モックアップと同じ中心への磁石・臨界減衰の寸法と壁で投げた窓を収める。</summary>
public sealed class WelcomeThrowModel
{
    private readonly WelcomeCell[] _cells = new WelcomeCell[3];
    private double _demoAt = 1.6;
    private double _elapsed;
    private double _velocityWidth;
    private double _velocityHeight;
    private double _positionX;
    private double _positionY;

    /// <summary>最初は右上の区画に置く。</summary>
    public WelcomeThrowModel(double width, double height) => Resize(width, height);

    public double StageWidth { get; private set; }
    public double StageHeight { get; private set; }
    // 壁の外の変位は物理に残し、描画する矩形だけを舞台内へ収める。
    public double X => Math.Clamp(_positionX, 0, Math.Max(0, StageWidth - Width));
    public double Y => Math.Clamp(_positionY, 0, Math.Max(0, StageHeight - Height));
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double VelocityX { get; private set; }
    public double VelocityY { get; private set; }
    public int Target { get; private set; } = 1;
    public bool Held { get; private set; }
    public bool Touched { get; private set; }
    public bool Settled { get; private set; } = true;
    public IReadOnlyList<WelcomeCell> Cells => _cells;

    /// <summary>舞台が狭くても寸法を負にせず、現在の行き先へ置き直す。</summary>
    public void Resize(double width, double height)
    {
        StageWidth = Math.Max(0, width); StageHeight = Math.Max(0, height);
        var gap = Math.Min(8, Math.Min(StageWidth, StageHeight) / 3);
        var cw = Math.Max(0, (StageWidth - gap * 3) / 2);
        var ch = Math.Max(0, (StageHeight - gap * 3) / 2);
        _cells[0] = new(gap, gap, cw, Math.Max(0, StageHeight - gap * 2));
        _cells[1] = new(gap * 2 + cw, gap, cw, ch);
        _cells[2] = new(gap * 2 + cw, gap * 2 + ch, cw, ch);
        Snap();
    }

    /// <summary>触ったあとは自動巡回を止め、現在の位置でつかむ。</summary>
    public void Grab()
    {
        _positionX = X; _positionY = Y;
        Held = true; Touched = true; VelocityX = 0; VelocityY = 0;
    }

    /// <summary>つかんでいる窓を舞台内で動かす。</summary>
    public void Move(double x, double y)
    {
        if (!Held) { return; }
        _positionX = Math.Clamp(x, 0, Math.Max(0, StageWidth - Width));
        _positionY = Math.Clamp(y, 0, Math.Max(0, StageHeight - Height));
        Target = Nearest(X + Width / 2, Y + Height / 2);
    }

    /// <summary>慣性の予測点に最も近い区画を選ぶ。効果オフなら直接置く。</summary>
    public void Release(double velocityX, double velocityY, bool animationsEnabled = true)
    {
        if (!Held) { return; }
        Held = false;
        var tau = FeelPreset.Standard.CoastTime;
        Target = Nearest(X + Width / 2 + velocityX * tau, Y + Height / 2 + velocityY * tau);
        VelocityX = velocityX; VelocityY = velocityY; Settled = false;
        if (!animationsEnabled) { Snap(); }
    }

    /// <summary>表示中だけ進める。効果オフでは巡回せず、物理の途中でも直接収める。</summary>
    public void Advance(double dt, bool active = true, bool animationsEnabled = true)
    {
        if (!active) { return; }
        if (!animationsEnabled)
        {
            if (!Held) { Snap(); }
            return;
        }
        if (!double.IsFinite(dt) || dt <= 0) { return; }
        var remaining = Math.Min(.05, dt);
        _elapsed += remaining;
        // フレームの加算誤差だけを吸収し、予定した刻みで巡回を始める。
        if (!Touched && _elapsed + 1e-12 >= _demoAt)
        {
            _demoAt += 2.6;
            Target = (Target + 1) % 3;
            var z = _cells[Target];
            VelocityX = (z.X + z.Width / 2 - X - Width / 2) * 1.2;
            VelocityY = (z.Y + z.Height / 2 - Y - Height / 2) * 1.2;
            Settled = false;
        }
        if (Held || Settled) { return; }
        while (remaining > 0)
        {
            var h = Math.Min(remaining, 1d / 240);
            remaining -= h;
            Step(h);
        }
        var cell = _cells[Target];
        var distance = Math.Sqrt(Math.Pow(_positionX - cell.X, 2) + Math.Pow(_positionY - cell.Y, 2)
            + Math.Pow(Width - cell.Width, 2) + Math.Pow(Height - cell.Height, 2));
        if (distance < .4 && double.Hypot(VelocityX, VelocityY) < 3) { Snap(); }
    }

    private int Nearest(double x, double y) => Enumerable.Range(0, 3)
        .MinBy(i => double.Hypot(x - _cells[i].X - _cells[i].Width / 2,
            y - _cells[i].Y - _cells[i].Height / 2));

    private void Snap()
    {
        var z = _cells[Target];
        _positionX = z.X; _positionY = z.Y; Width = z.Width; Height = z.Height;
        VelocityX = 0; VelocityY = 0; _velocityWidth = 0; _velocityHeight = 0; Settled = true;
    }

    private void Step(double h)
    {
        var z = _cells[Target];
        var f = FeelPreset.Standard;
        var k = f.Stiffness;
        var c = f.Damping;
        var m = f.Mass;
        VelocityX += (-k * (_positionX + Width / 2 - z.X - z.Width / 2) - c * VelocityX) / m * h;
        VelocityY += (-k * (_positionY + Height / 2 - z.Y - z.Height / 2) - c * VelocityY) / m * h;
        var critical = 2 * Math.Sqrt(k * m);
        _velocityWidth += (-k * (Width - z.Width) - critical * _velocityWidth) / m * h;
        _velocityHeight += (-k * (Height - z.Height) - critical * _velocityHeight) / m * h;
        var wallK = m * Math.Pow(2 * Math.PI * 8, 2);
        var wallC = 2 * Math.Sqrt(wallK * m);
        double Wall(double position, double velocity, double high) => position < 0
            ? (-wallK * position - wallC * velocity) / m
            : position > high ? (-wallK * (position - high) - wallC * velocity) / m : 0;
        VelocityX += Wall(_positionX, VelocityX, StageWidth - Width) * h;
        VelocityY += Wall(_positionY, VelocityY, StageHeight - Height) * h;
        Width = Math.Clamp(Width + _velocityWidth * h, 0, StageWidth);
        Height = Math.Clamp(Height + _velocityHeight * h, 0, StageHeight);
        _positionX += VelocityX * h; _positionY += VelocityY * h;
    }
}
