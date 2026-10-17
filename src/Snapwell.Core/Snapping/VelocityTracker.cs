using Snapwell.Core.Geometry;

namespace Snapwell.Core.Snapping;

/// <summary>直近のポインタ軌跡から離した瞬間の速度を推定する。</summary>
public sealed class VelocityTracker
{
    private const double TimeTolerance = 1e-12;
    private readonly Queue<(Vec2 Position, double Time)> _samples = new();
    private double? _lastTime;

    /// <summary>時刻が非減少の記録を追加し、百ミリ秒より古い軌跡を捨てる。</summary>
    public void AddSample(Vec2 position, double seconds)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!double.IsFinite(seconds) || (_lastTime.HasValue && seconds < _lastTime.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        _samples.Enqueue((position, seconds));
        _lastTime = seconds;
        while (_samples.TryPeek(out var sample) && seconds - sample.Time > 0.1 + TimeTolerance)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>最後の八十ミリ秒の速度を返し、六十ミリ秒以上停止していればゼロを返す。</summary>
    public Vec2 Release(double now)
    {
        if (!double.IsFinite(now) || (_lastTime.HasValue && now < _lastTime.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (_samples.Count < 2)
        {
            return default;
        }

        var last = _samples.Last();
        if (now - last.Time + TimeTolerance >= 0.06)
        {
            return default;
        }

        foreach (var first in _samples)
        {
            if (last.Time - first.Time > 0.08 + TimeTolerance)
            {
                continue;
            }

            var elapsed = last.Time - first.Time;
            return elapsed + TimeTolerance < 0.008 ? default : (last.Position - first.Position) * (1 / elapsed);
        }

        return default;
    }
}
