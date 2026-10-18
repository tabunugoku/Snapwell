namespace Snapwell.Core.Physics;

/// <summary>feel の語彙から導いた質量・固有振動数・減衰比と、磁石の強さ。</summary>
public sealed record FeelPreset
{
    /// <summary>語に磁石がないときの強さ。feel の corner-card の <c>w.magnet || 0.6</c>。</summary>
    public const double DefaultMagnetStrength = 0.6;

    /// <summary>設定で選べる磁石の強さの上限。feel の語は "super magnetic" で 1.8 になる。</summary>
    public const double MaximumMagnetStrength = 2;

    /// <summary>有限の物理パラメーターから操作感を定義する。</summary>
    public FeelPreset(double mass, double frequency, double dampingRatio,
        double magnetStrength = DefaultMagnetStrength)
    {
        if (!double.IsFinite(mass) || mass <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mass));
        }

        if (!double.IsFinite(frequency) || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (!double.IsFinite(dampingRatio) || dampingRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dampingRatio));
        }

        if (!double.IsFinite(magnetStrength) || magnetStrength < 0 || magnetStrength > MaximumMagnetStrength)
        {
            throw new ArgumentOutOfRangeException(nameof(magnetStrength));
        }

        Mass = mass;
        Frequency = frequency;
        DampingRatio = dampingRatio;
        MagnetStrength = magnetStrength;
    }

    /// <summary>質量。</summary>
    public double Mass { get; }

    /// <summary>固有振動数。単位は Hz。減衰係数と、目標へのばねの剛性を決める。</summary>
    public double Frequency { get; }

    /// <summary>減衰比。</summary>
    public double DampingRatio { get; }

    /// <summary>磁石の強さ。</summary>
    public double MagnetStrength { get; }

    /// <summary>質量と固有振動数から導く剛性 k = m(2πf)²。</summary>
    public double Stiffness => Mass * Math.Pow(2 * Math.PI * Frequency, 2);

    /// <summary>減衰比と剛性から導く減衰係数 c = 2ζ√(km)。</summary>
    public double Damping => 2 * DampingRatio * Math.Sqrt(Stiffness * Mass);

    /// <summary>手を離したあとの惰性で止まるまでの距離を速度に掛ける係数 m/c（feel の projected）。</summary>
    public double CoastTime => Mass / Damping;

    /// <summary>磁石場の遠くまで届くばねの振動数 0.6 + 0.5×強さ。</summary>
    public double ReachFrequency => 0.6 + 0.5 * MagnetStrength;

    /// <summary>磁石場の吸着半径の内側で効くばねの振動数 3 + 4×強さ。</summary>
    public double ClickFrequency => 3 + 4 * MagnetStrength;

    /// <summary>磁石の強さだけを変えた操作感を返す。範囲外は 0〜上限に収め、非有限は既定値にする。</summary>
    public FeelPreset WithMagnetStrength(double strength) => new(Mass, Frequency, DampingRatio,
        double.IsFinite(strength) ? Math.Clamp(strength, 0, MaximumMagnetStrength) : DefaultMagnetStrength);

    /// <summary>feel の語彙から操作感を作る。磁石の語がなければ既定の強さにする。</summary>
    public static FeelPreset FromWords(string text)
    {
        var words = FeelWords.Parse(text);
        return new(words.Mass, words.Frequency, words.DampingRatio,
            words.Magnet > 0 ? Math.Min(words.Magnet, MaximumMagnetStrength) : DefaultMagnetStrength);
    }

    /// <summary>airy, slightly magnetic に対応する操作感。</summary>
    public static FeelPreset Airy { get; } = FromWords("airy, slightly magnetic");

    /// <summary>snappy, precise に対応する操作感。</summary>
    public static FeelPreset Snappy { get; } = FromWords("snappy, precise");

    /// <summary>heavy, settled に対応する操作感。</summary>
    public static FeelPreset Heavy { get; } = FromWords("heavy, settled");

    /// <summary>light, playful に対応する操作感。</summary>
    public static FeelPreset Bouncy { get; } = FromWords("light, playful");
}
