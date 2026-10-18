namespace Snapwell.Core.Physics;

/// <summary>feel の語彙（runtime/feel.js の WORDS・AMPLIFY・words()）から質量・振動数・減衰比・磁石の強さを導く。</summary>
public static class FeelWords
{
    private const double NeutralFrequency = 2.2;
    private const double NeutralDampingRatio = 0.8;

    // 質量・振動数・減衰比・磁石の強さ。使わない項目は 0（feel の undefined に当たる）。
    private static readonly Dictionary<string, (double Mass, double Frequency, double Zeta, double Magnet)> _words =
        new()
        {
            ["snappy"] = (0, 3.4, 0.82, 0),
            ["crisp"] = (0, 3.8, 0.92, 0),
            ["quick"] = (0, 3.6, 0.95, 0),
            ["instant"] = (0, 6, 1, 0),
            ["precise"] = (0, 4.5, 1, 0),
            ["stiff"] = (0, 4.6, 0.9, 0),
            ["firm"] = (0, 3.8, 0.95, 0),
            ["tight"] = (0, 4.2, 0.85, 0),
            ["smooth"] = (0, 1.8, 1, 0),
            ["soft"] = (0, 1.4, 1, 0),
            ["gentle"] = (0, 1.2, 1, 0),
            ["calm"] = (0, 1.1, 1.1, 0),
            ["lazy"] = (0, 0.9, 1.2, 0),
            ["bouncy"] = (0, 2.4, 0.38, 0),
            ["playful"] = (0, 2.6, 0.45, 0),
            ["springy"] = (0, 2.8, 0.32, 0),
            ["wobbly"] = (0, 1.8, 0.22, 0),
            ["loose"] = (0, 1.6, 0.3, 0),
            ["elastic"] = (0, 2.2, 0.28, 0),
            ["heavy"] = (3, 1.35, 0.74, 0),
            ["weighty"] = (2.6, 1.5, 0.78, 0),
            ["solid"] = (2, 2.2, 0.88, 0),
            ["massive"] = (4.5, 1.1, 0.8, 0),
            ["light"] = (0.45, 3, 0.62, 0),
            ["airy"] = (0.35, 2.2, 0.55, 0),
            ["feathery"] = (0.25, 1.8, 0.6, 0),
            ["magnetic"] = (0, 0, 0, 1),
            ["clicky"] = (0, 3.4, 0, 0.6),
            ["tactile"] = (0, 0, 0, 0.5),
            // 摩擦の語は Snapwell では使わないが、減衰比の平均に加わる resistant だけ残す。
            ["resistant"] = (0, 0, 1, 0),
        };

    private static readonly Dictionary<string, double> _amplify = new()
    {
        ["very"] = 1.6,
        ["really"] = 1.5,
        ["super"] = 1.8,
        ["extra"] = 1.6,
        ["slightly"] = 0.5,
        ["a bit"] = 0.5,
        ["somewhat"] = 0.6,
        ["little"] = 0.5,
    };

    /// <summary>語を混ぜる。振動数は幾何平均、減衰比は平均、質量は積、磁石は最大。</summary>
    public static FeelWordsResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        double logFrequency = 0, zetaSum = 0, mass = 1, magnet = 0, gain = 1;
        int frequencyCount = 0, zetaCount = 0;
        foreach (var raw in Tokenize(text))
        {
            var token = raw == "a-bit" ? "a bit" : raw;
            if (_amplify.TryGetValue(token, out var amplify))
            {
                gain = amplify;
                continue;
            }

            if (!TryFind(token, out var word))
            {
                gain = 1;
                continue;
            }

            if (word.Frequency != 0)
            {
                logFrequency += Math.Log(PullLog(word.Frequency, NeutralFrequency, gain));
                frequencyCount++;
            }

            if (word.Zeta != 0)
            {
                zetaSum += NeutralDampingRatio + (word.Zeta - NeutralDampingRatio) * gain;
                zetaCount++;
            }

            if (word.Mass != 0)
            {
                mass *= PullLog(word.Mass, 1, gain);
            }

            if (word.Magnet != 0)
            {
                magnet = Math.Max(magnet, word.Magnet * gain);
            }

            gain = 1;
        }

        return new(Math.Min(8, Math.Max(0.15, mass)),
            frequencyCount > 0 ? Math.Exp(logFrequency / frequencyCount) : NeutralFrequency,
            zetaCount > 0 ? Math.Max(0.1, zetaSum / zetaCount) : NeutralDampingRatio, magnet);
    }

    private static double PullLog(double value, double neutral, double gain) =>
        Math.Exp(Math.Log(neutral) + (Math.Log(value) - Math.Log(neutral)) * gain);

    // feel の WORDS[t] || WORDS[t.replace(/ly$/, "")] || WORDS[t.replace(/ness$/, "")]。
    private static bool TryFind(string token, out (double Mass, double Frequency, double Zeta, double Magnet) word)
    {
        if (_words.TryGetValue(token, out word))
        {
            return true;
        }

        if (token.EndsWith("ly", StringComparison.Ordinal) &&
            _words.TryGetValue(token[..^2], out word))
        {
            return true;
        }

        return token.EndsWith("ness", StringComparison.Ordinal) &&
            _words.TryGetValue(token[..^4], out word);
    }

    // 小文字にして "a bit" を一語にし、[^a-z-] で区切る。
    private static IEnumerable<string> Tokenize(string text)
    {
        var lower = text.ToLowerInvariant().Replace("a bit", "a-bit", StringComparison.Ordinal);
        var start = -1;
        for (var i = 0; i <= lower.Length; i++)
        {
            var isWordChar = i < lower.Length && (lower[i] is >= 'a' and <= 'z' || lower[i] == '-');
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                yield return lower[start..i];
                start = -1;
            }
        }
    }
}

/// <summary>語彙から導いた質量・振動数・減衰比と、磁石の語がなければ 0 の強さ。</summary>
public readonly record struct FeelWordsResult(double Mass, double Frequency, double DampingRatio, double Magnet);
