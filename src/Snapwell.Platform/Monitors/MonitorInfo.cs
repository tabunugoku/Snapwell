using System.Security.Cryptography;
using System.Text;
using Snapwell.Core.Geometry;
using Snapwell.Core.Snapping;

namespace Snapwell.Platform.Monitors;

/// <summary>物理ピクセルのモニター領域と実効 DPI。</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea,
    uint DpiX, uint DpiY, bool IsPrimary)
{
    /// <summary>Core が使う作業領域と DPI 倍率へ変換する。</summary>
    public MonitorArea ToMonitorArea() => new(DeviceName, WorkArea, DpiX / 96.0);
}

/// <summary>現在のモニター一覧の取得元。</summary>
public interface IMonitorSource
{
    /// <summary>取得に失敗した場合は例外で通知し、部分的な一覧を返さない。</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();
}

/// <summary>列挙順や現在のカルチャに依存しないモニター構成の識別子。</summary>
public static class MonitorLayoutKey
{
    /// <summary>デバイス名・全体矩形・実効 DPI から SHA-256 の先頭 16 桁を返す。</summary>
    public static string Create(IEnumerable<MonitorInfo> monitors)
    {
        // 長さ付きの名前で区切り文字を含むデバイス名も一意に符号化する。
        var entries = monitors.Select(Encode)
            .Order(StringComparer.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", entries));
        return Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 8));
    }

    private static string Encode(MonitorInfo monitor)
    {
        var bounds = monitor.Bounds;
        return FormattableString.Invariant(
            $"{monitor.DeviceName.Length}:{monitor.DeviceName}:{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}") +
            FormattableString.Invariant($":{monitor.DpiX},{monitor.DpiY}");
    }
}
