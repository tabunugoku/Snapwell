namespace Snapwell.Core.Physics;

/// <summary>描画間隔を上限つきの物理刻みに分割する。</summary>
public static class Simulator
{
    private const double FixedStep = 1.0 / 240;
    private const double MaximumFrame = 0.05;

    /// <summary>最大 0.05 秒を進め、静止したら目標を確定して true を返す。</summary>
    public static bool Advance(WindowBody body, double frameSeconds)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (frameSeconds <= 0)
        {
            return body.IsSettled;
        }

        if (!double.IsFinite(frameSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        }

        var remaining = Math.Min(frameSeconds, MaximumFrame);
        while (remaining > 0)
        {
            var dt = Math.Min(remaining, FixedStep);
            body.Step(dt);
            remaining -= dt;
        }

        if (!body.IsSettled)
        {
            return false;
        }

        body.Settle();
        return true;
    }
}
