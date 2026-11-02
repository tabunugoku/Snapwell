namespace Snapwell.Core.Updates;

/// <summary>画面に表示する版をビルド情報から切り離す。</summary>
public static class DisplayVersion
{
    /// <summary>最初の + 以降と前後の空白を除き、版がなければ null を返す。</summary>
    public static string? Format(string? informationalVersion)
    {
        if (informationalVersion is null) { return null; }
        var separator = informationalVersion.IndexOf('+');
        var version = (separator < 0 ? informationalVersion : informationalVersion[..separator]).Trim();
        return version.Length == 0 ? null : version;
    }
}
