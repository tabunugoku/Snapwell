using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Snapwell.Core.Updates;

/// <summary>ビルド情報を優先順位に含めない SemVer 2.0 の版。</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(long major, long minor, long patch, string? prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    /// <summary>メジャー番号。</summary>
    public long Major { get; }

    /// <summary>マイナー番号。</summary>
    public long Minor { get; }

    /// <summary>パッチ番号。</summary>
    public long Patch { get; }

    /// <summary>プレリリース識別子。</summary>
    public string? Prerelease { get; }

    /// <summary>優先順位に影響しないビルド識別子。</summary>
    public string? Build { get; }

    /// <summary>版を指定していない 0.0.0 系の開発用ビルドか。</summary>
    public bool IsDevelopmentBuild => Major == 0 && Minor == 0 && Patch == 0;

    /// <summary>先頭の小文字 v を許して解析し、不正な版や数値の桁あふれは false を返す。</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text[0] == 'v')
        {
            text = text[1..];
        }

        string? build = null;
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            build = text[(plus + 1)..];
            text = text[..plus];
            if (!ValidIdentifiers(build, prerelease: false))
            {
                return false;
            }
        }

        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (!ValidIdentifiers(prerelease, prerelease: true))
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length != 3 || !TryNumber(parts[0], out var major)
            || !TryNumber(parts[1], out var minor) || !TryNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new(major, minor, patch, prerelease, build);
        return true;
    }

    /// <summary>SemVer の優先順位で比較する。</summary>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) { return 1; }
        var result = Major.CompareTo(other.Major);
        if (result != 0) { return result; }
        result = Minor.CompareTo(other.Minor);
        if (result != 0) { return result; }
        result = Patch.CompareTo(other.Patch);
        if (result != 0) { return result; }
        if (Prerelease is null) { return other.Prerelease is null ? 0 : 1; }
        if (other.Prerelease is null) { return -1; }

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftNumeric = long.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ln);
            var rightNumeric = long.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rn);
            result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => ln.CompareTo(rn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };
            if (result != 0) { return result; }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>ビルド情報を除いた優先順位が等しいかを返す。</summary>
    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    /// <summary>ビルド情報を除いた優先順位が等しいかを返す。</summary>
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    /// <summary>優先順位に使う部分からハッシュ値を作る。</summary>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <summary>先頭の v を除いた SemVer 表記を返す。</summary>
    public override string ToString() => FormattableString.Invariant($"{Major}.{Minor}.{Patch}")
        + (Prerelease is null ? "" : "-" + Prerelease) + (Build is null ? "" : "+" + Build);

    private static bool TryNumber(string text, out long value)
    {
        value = 0;
        return text.Length > 0 && (text.Length == 1 || text[0] != '0')
            && text.All(c => c is >= '0' and <= '9')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool ValidIdentifiers(string text, bool prerelease)
    {
        foreach (var identifier in text.Split('.'))
        {
            if (identifier.Length == 0) { return false; }
            var numeric = true;
            foreach (var c in identifier)
            {
                if (c is >= '0' and <= '9') { continue; }
                numeric = false;
                if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-')) { return false; }
            }

            // ビルド識別子には数値の制限も先頭ゼロの制限もない。
            if (prerelease && numeric && !TryNumber(identifier, out _)) { return false; }
        }

        return true;
    }
}
