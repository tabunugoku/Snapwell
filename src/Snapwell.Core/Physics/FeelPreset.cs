namespace Snapwell.Core.Physics;

/// <summary>ばねの質量・固有振動数・減衰比と空走の時定数。</summary>
public sealed record FeelPreset
{
    /// <summary>有限の物理パラメーターから操作感を定義する。</summary>
    public FeelPreset(double mass, double frequency, double dampingRatio, double tau)
    {
        if (!double.IsFinite(mass) || mass <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mass));
        }

        if (!double.IsFinite(frequency) || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (!double.IsFinite(dampingRatio) || dampingRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dampingRatio));
        }

        if (!double.IsFinite(tau) || tau < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tau));
        }

        Mass = mass;
        Frequency = frequency;
        DampingRatio = dampingRatio;
        Tau = tau;
    }

    /// <summary>質量。</summary>
    public double Mass { get; }

    /// <summary>固有振動数。単位は Hz。</summary>
    public double Frequency { get; }

    /// <summary>減衰比。</summary>
    public double DampingRatio { get; }

    /// <summary>空走の時定数。単位は秒。</summary>
    public double Tau { get; }

    /// <summary>質量と固有振動数から導く剛性。</summary>
    public double Stiffness => Mass * Math.Pow(2 * Math.PI * Frequency, 2);

    /// <summary>減衰比と剛性から導く減衰係数。</summary>
    public double Damping => 2 * DampingRatio * Math.Sqrt(Stiffness * Mass);

    /// <summary>airy, slightly magnetic に対応する操作感。</summary>
    public static FeelPreset Airy { get; } = new(1, 1.6, 0.78, 0.22);

    /// <summary>snappy, precise に対応する操作感。</summary>
    public static FeelPreset Snappy { get; } = new(1, 2.6, 0.9, 0.16);

    /// <summary>heavy, settled に対応する操作感。</summary>
    public static FeelPreset Heavy { get; } = new(3, 1.35, 0.74, 0.30);

    /// <summary>light, playful に対応する操作感。</summary>
    public static FeelPreset Bouncy { get; } = new(1, 1.9, 0.5, 0.2);
}
