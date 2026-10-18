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

    /// <summary>磁石の強さの既定値。DESIGN.md §5.1 の feel の値。</summary>
    public const double DefaultMagnetStrength = 0.6;

    /// <summary>磁石の強さの下限と上限。</summary>
    public const double MinimumMagnetStrength = 0;

    /// <summary>磁石の強さの上限。</summary>
    public const double MaximumMagnetStrength = 1;

    /// <summary>
    /// 磁石の強さで吸い寄せの固有振動数を変えた操作感を返す。
    /// 吸着の振動数 3 + 4×強さ（§5.1）の、既定値に対する比を掛ける。質量と減衰比は保つ。
    /// </summary>
    public FeelPreset WithMagnetStrength(double strength)
    {
        strength = double.IsFinite(strength)
            ? Math.Clamp(strength, MinimumMagnetStrength, MaximumMagnetStrength)
            : DefaultMagnetStrength;
        if (strength == DefaultMagnetStrength)
        {
            return this;
        }

        var ratio = (3 + 4 * strength) / (3 + 4 * DefaultMagnetStrength);
        return new(Mass, Frequency * ratio, DampingRatio, Tau);
    }

    /// <summary>airy, slightly magnetic に対応する操作感。</summary>
    public static FeelPreset Airy { get; } = new(1, 1.6, 0.78, 0.22);

    /// <summary>snappy, precise に対応する操作感。</summary>
    public static FeelPreset Snappy { get; } = new(1, 2.6, 0.9, 0.16);

    /// <summary>heavy, settled に対応する操作感。</summary>
    public static FeelPreset Heavy { get; } = new(3, 1.35, 0.74, 0.30);

    /// <summary>light, playful に対応する操作感。</summary>
    public static FeelPreset Bouncy { get; } = new(1, 1.9, 0.5, 0.2);
}
