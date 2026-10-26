using Snapwell.Core.Physics;

namespace Snapwell.Core.Setup;

/// <summary>中心座標と寸法で表す、導入の目標矩形。</summary>
public readonly record struct IntroRect(double X, double Y, double Width, double Height, double Rotation = 0);

/// <summary>描画から読み取る窓の物理状態。</summary>
public sealed class IntroWindow
{
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Width { get; internal set; }
    public double Height { get; internal set; }
    public double Rotation { get; internal set; }
    public double VelocityX { get; internal set; }
    public double VelocityY { get; internal set; }
    public double VelocityWidth { get; internal set; }
    public double VelocityHeight { get; internal set; }
    public double VelocityRotation { get; internal set; }
    public double Flash { get; internal set; }
    public bool Settled { get; internal set; } = true;
    public bool Visible { get; internal set; }
    public IntroRect Target { get; internal set; }
    internal FeelPreset Feel { get; set; } = FeelPreset.Standard;
    internal List<(double X, double Y)> TrailPoints { get; } = [];
    public IReadOnlyList<(double X, double Y)> Trail => TrailPoints;
}

/// <summary>点の格子の元の座標と、波で動く変位・速度。</summary>
public sealed class IntroDot(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double DisplacementX { get; internal set; }
    public double DisplacementY { get; internal set; }
    public double VelocityX { get; internal set; }
    public double VelocityY { get; internal set; }
}

/// <summary>同心円として伝わる波。</summary>
public readonly record struct IntroWave(double X, double Y, double StartedAt, double Amplitude,
    double Speed, double Width, double Life);

/// <summary>モックアップの台本とばねを、時計や描画 API に依存せず進める。</summary>
public sealed class IntroChoreography
{
    private readonly List<(double Time, Action Run)> _script = [];
    private readonly List<IntroWave> _waves = [];
    private readonly Func<double> _random;
    private int _nextCue;
    private double _logoSquareVelocity;
    private double _logoNameVelocity;

    private static readonly double[][][] _layouts =
    [
        [[0, 0, 1d / 3, .5], [1d / 3, 0, 1d / 3, .5], [2d / 3, 0, 1d / 3, .5],
            [0, .5, 1d / 3, .5], [1d / 3, .5, 1d / 3, .5], [2d / 3, .5, 1d / 3, .5]],
        [[0, 0, .4, .5], [0, .5, .4, .5], [.4, 0, .35, .5], [.75, 0, .25, .5],
            [.4, .5, .3, .5], [.7, .5, .3, .5]],
        [[0, 0, .5, 1d / 3], [.5, 0, .5, 1d / 3], [0, 1d / 3, .5, 1d / 3],
            [.5, 1d / 3, .5, 1d / 3], [0, 2d / 3, .5, 1d / 3], [.5, 2d / 3, .5, 1d / 3]],
        [[0, 0, .5, 1], [.5, 0, .5, .5], [.5, .5, 1d / 6, .5], [2d / 3, .5, 1d / 6, .5],
            [5d / 6, .5, 1d / 6, .25], [5d / 6, .75, 1d / 6, .25]],
    ];

    /// <summary>舞台の寸法と乱数源を受け取る。窓数は台本の最大 6 枚まで。</summary>
    public IntroChoreography(double width, double height, Func<double>? random = null, int windowCount = 6)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _random = random ?? Random.Shared.NextDouble;
        var cellWidth = Math.Max(0, (Width - 56) / 3 - 8);
        var cellHeight = Math.Max(0, (Height - 56) / 2 - 8);
        Windows = Enumerable.Range(0, Math.Clamp(windowCount, 0, 6)).Select(_ => new IntroWindow
        {
            X = -cellWidth,
            Y = Height * .55,
            Width = cellWidth * 1.15,
            Height = cellHeight * 1.15,
            Target = new(Width / 2, Height / 2, cellWidth, cellHeight),
        }).ToArray();
        var dots = new List<IntroDot>();
        for (double y = 11; y < Height; y += 22)
        {
            for (double x = 11; x < Width; x += 22) { dots.Add(new(x, y)); }
        }
        Dots = dots.AsReadOnly();
        At(.5, () =>
        {
            if (Windows.Count == 0) { return; }
            Aim(Windows[0], new(Width / 2, Height / 2, cellWidth * 1.5, cellHeight * 1.5),
                FeelPreset.Standard, 1500, -200);
        });
        At(1.9, () =>
        {
            for (var i = 0; i < Windows.Count; i++)
            {
                var w = Windows[i];
                if (i > 0)
                {
                    w.X = Windows[0].X; w.Y = Windows[0].Y;
                    w.Width = Windows[0].Width; w.Height = Windows[0].Height;
                }
                var angle = (double)i / Windows.Count * Math.PI * 2 + .4;
                var radius = Math.Max(0, Math.Min(Width - 56, Height - 56)) * .36;
                Aim(w, new(Width / 2 + Math.Cos(angle) * radius * 1.5,
                    Height / 2 + Math.Sin(angle) * radius * .9, cellWidth * .62, cellHeight * .62,
                    RandomBetween(-.28, .28)), FeelPreset.Bouncy, Math.Cos(angle) * 900, Math.Sin(angle) * 700);
                w.VelocityRotation = RandomBetween(-6, 6);
            }
            _waves.Add(new(Width / 2, Height / 2, 1.9, 18, 420, 60, 2));
        });
        At(3, () =>
        {
            for (var i = 0; i < Windows.Count; i++)
            {
                Aim(Windows[i], RectOf(0, i), FeelPreset.Standard,
                    RandomBetween(-250, 250), RandomBetween(-250, 250));
            }
        });
        foreach (var (time, layout) in new[] { (3.9, 1), (4.8, 2), (5.7, 3) })
        {
            for (var i = 0; i < Windows.Count; i++)
            {
                var index = i;
                At(time + i * .07, () => Aim(Windows[index], RectOf(layout, index), FeelPreset.Standard));
            }
        }
        for (var i = 0; i < Windows.Count; i++)
        {
            var index = i;
            var time = 6.5 + i * .1;
            At(time, () =>
            {
                var w = Windows[index];
                w.VelocityY = -520; w.Flash = 1; w.Settled = false;
                _waves.Add(new(w.Target.X, w.Target.Y, time, 8, 260, 40, 1.4));
            });
        }
        _script.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public double Width { get; }
    public double Height { get; }
    public double Elapsed { get; private set; }
    public IReadOnlyList<IntroWindow> Windows { get; }
    public IReadOnlyList<IntroDot> Dots { get; }
    public IReadOnlyList<IntroWave> Waves => _waves;
    public bool LogoVisible => Elapsed >= 7.4;
    public double LogoSquare { get; private set; }
    public double LogoName { get; private set; }
    public bool IsComplete => Elapsed >= 10.5;
    public double Opacity => 1 - Math.Clamp((Elapsed - 9.6) / .8, 0, 1);

    /// <summary>4 種の並びを余白 28、隙間 8 の中心座標に変換する。</summary>
    public IntroRect RectOf(int layout, int index)
    {
        var r = _layouts[layout][index];
        var sw = Math.Max(0, Width - 56);
        var sh = Math.Max(0, Height - 56);
        return new(Width / 2 + (r[0] + r[2] / 2 - .5) * sw,
            Height / 2 + (r[1] + r[3] / 2 - .5) * sh,
            Math.Max(0, r[2] * sw - 8), Math.Max(0, r[3] * sh - 8));
    }

    /// <summary>最大 0.05 秒を、台本の境界と最大 1/240 秒で分割して進める。</summary>
    public void Advance(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0 || IsComplete) { return; }
        var end = Math.Min(10.5, Elapsed + Math.Min(.05, dt));
        while (Elapsed < end)
        {
            while (_nextCue < _script.Count && _script[_nextCue].Time <= Elapsed)
            {
                _script[_nextCue++].Run();
            }
            var next = Math.Min(end, Elapsed + 1d / 240);
            if (_nextCue < _script.Count) { next = Math.Min(next, _script[_nextCue].Time); }
            var h = next - Elapsed;
            Elapsed = next;
            Step(h);
        }
        while (_nextCue < _script.Count && _script[_nextCue].Time <= Elapsed)
        {
            _script[_nextCue++].Run();
        }
        foreach (var w in Windows.Where(w => w.Visible))
        {
            w.TrailPoints.Add((w.X, w.Y));
            if (w.TrailPoints.Count > 40) { w.TrailPoints.RemoveAt(0); }
        }
    }

    private void At(double time, Action action) => _script.Add((time, action));
    private double RandomBetween(double low, double high) => low + _random() * (high - low);

    private static void Aim(IntroWindow w, IntroRect target, FeelPreset feel,
        double? velocityX = null, double? velocityY = null)
    {
        w.Target = target; w.Feel = feel; w.Visible = true; w.Settled = false;
        if (velocityX is { } vx) { w.VelocityX = vx; }
        if (velocityY is { } vy) { w.VelocityY = vy; }
    }

    private void Step(double h)
    {
        foreach (var w in Windows)
        {
            if (!w.Visible) { continue; }
            var k = w.Feel.Stiffness;
            var c = w.Feel.Damping;
            var m = w.Feel.Mass;
            var critical = 2 * Math.Sqrt(k * m);
            w.VelocityX += (-k * (w.X - w.Target.X) - c * w.VelocityX) / m * h;
            w.VelocityY += (-k * (w.Y - w.Target.Y) - c * w.VelocityY) / m * h;
            w.VelocityRotation += (-k * .5 * (w.Rotation - w.Target.Rotation)
                - c * .6 * w.VelocityRotation) / m * h;
            w.VelocityWidth += (-k * (w.Width - w.Target.Width) - critical * w.VelocityWidth) / m * h;
            w.VelocityHeight += (-k * (w.Height - w.Target.Height) - critical * w.VelocityHeight) / m * h;
            w.X += w.VelocityX * h; w.Y += w.VelocityY * h;
            w.Width += w.VelocityWidth * h; w.Height += w.VelocityHeight * h;
            w.Rotation += w.VelocityRotation * h;
            w.Flash = Math.Max(0, w.Flash - h * 1.3);
            var distance = double.Hypot(w.X - w.Target.X, w.Y - w.Target.Y)
                + Math.Abs(w.Width - w.Target.Width) + Math.Abs(w.Height - w.Target.Height);
            if (!w.Settled && distance < 1.6 && double.Hypot(w.VelocityX, w.VelocityY) < 10)
            {
                w.Settled = true; w.Flash = 1;
                _waves.Add(new(w.Target.X, w.Target.Y, Elapsed, 9, 250, 36, 1.4));
            }
        }
        _waves.RemoveAll(w => Elapsed - w.StartedAt > w.Life);
        foreach (var dot in Dots)
        {
            double tx = 0, ty = 0;
            foreach (var wave in _waves)
            {
                var x = dot.X - wave.X;
                var y = dot.Y - wave.Y;
                var radius = Math.Max(1, double.Hypot(x, y));
                var age = Elapsed - wave.StartedAt;
                var force = wave.Amplitude * Math.Exp(-age * 1.5)
                    * Math.Exp(-Math.Pow((radius - wave.Speed * age) / wave.Width, 2));
                tx += x / radius * force; ty += y / radius * force;
            }
            const double k = 140;
            var c = 2 * .55 * Math.Sqrt(k);
            dot.VelocityX += (k * (tx - dot.DisplacementX) - c * dot.VelocityX) * h;
            dot.VelocityY += (k * (ty - dot.DisplacementY) - c * dot.VelocityY) * h;
            dot.DisplacementX += dot.VelocityX * h; dot.DisplacementY += dot.VelocityY * h;
        }
        if (Elapsed >= 7.85)
        {
            var f = FeelPreset.Bouncy;
            _logoSquareVelocity += (f.Stiffness * (1 - LogoSquare) - f.Damping * _logoSquareVelocity) / f.Mass * h;
            LogoSquare += _logoSquareVelocity * h;
        }
        if (Elapsed >= 7.75)
        {
            var f = FeelPreset.Standard;
            _logoNameVelocity += (f.Stiffness * (1 - LogoName) - f.Damping * _logoNameVelocity) / f.Mass * h;
            LogoName += _logoNameVelocity * h;
        }
    }
}
