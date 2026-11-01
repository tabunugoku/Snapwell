using System.Globalization;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tray;

/// <summary>16 論理 px の仕様の Path を、丸い端のアンチエイリアス線で描く。</summary>
public static class TrayMenuIcons
{
    private static readonly IReadOnlyDictionary<TrayMenuCommand, (Vec2 Start, Vec2 End)[]> _segments =
        new[] { TrayMenuCommand.Arrange, TrayMenuCommand.Launcher, TrayMenuCommand.Settings,
            TrayMenuCommand.Exit, TrayMenuCommand.Update }.ToDictionary(command => command, command => Parse(Path(command)));

    /// <summary>仕様の名前に対応する SVG Path を返す。</summary>
    public static string Path(TrayMenuCommand command) => command switch
    {
        TrayMenuCommand.Arrange =>
            "M2 2.5 H7 V7 H2 Z M9 2.5 H14 V7 H9 Z M2 9 H7 V13.5 H2 Z M9 9 H14 V13.5 H9 Z",
        TrayMenuCommand.Launcher => "M2 3 H14 V13 H2 Z M2 6 H14 M6 6 V13",
        TrayMenuCommand.Settings =>
            "M8 5.5 A2.5 2.5 0 1 1 8 10.5 A2.5 2.5 0 1 1 8 5.5 Z M8 1.5 v2 M8 12.5 v2 M1.5 8 h2 M12.5 8 h2 M3.4 3.4 l1.4 1.4 M11.2 11.2 l1.4 1.4 M3.4 12.6 l1.4 -1.4 M11.2 4.8 l1.4 -1.4",
        TrayMenuCommand.Exit =>
            "M6 2.5 H3 A0.5 0.5 0 0 0 2.5 3 V13 A0.5 0.5 0 0 0 3 13.5 H6 M10 5 L13 8 L10 11 M13 8 H6",
        TrayMenuCommand.Update => "M8 2 V10 M4.5 6.5 L8 10 L11.5 6.5 M2.5 13.5 H13.5",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    /// <summary>描画先の寸法で透明なアイコンを作る。メニューの静的な行に合成して保持する。</summary>
    public static OverlayFrame Create(TrayMenuCommand command, int size, OverlayColor color)
    {
        var frame = OverlayPainter.Create(size, size);
        var segment = OverlayPainter.Create(size, size);
        var scale = size / 16.0;
        foreach (var (start, end) in _segments[command])
        {
            Array.Clear(segment.Pixels);
            OverlayPainter.StrokeLine(segment, start * scale, end * scale, Math.Max(1, 1.5 * scale),
                new(255, 255, 255, 255));
            // 円弧の短い線分の端が重なっても、線の縁を重ね塗りで太くしない。
            for (var index = 3; index < frame.Pixels.Length; index += 4)
            {
                frame.Pixels[index] = Math.Max(frame.Pixels[index], segment.Pixels[index]);
            }
        }
        for (var index = 0; index < frame.Pixels.Length; index += 4)
        {
            var alpha = (byte)((frame.Pixels[index + 3] * color.Alpha + 127) / 255);
            frame.Pixels[index] = (byte)((color.Blue * alpha + 127) / 255);
            frame.Pixels[index + 1] = (byte)((color.Green * alpha + 127) / 255);
            frame.Pixels[index + 2] = (byte)((color.Red * alpha + 127) / 255);
            frame.Pixels[index + 3] = alpha;
        }
        return frame;
    }

    // 入力は上の固定 Path だけ。円弧は円・回転 0 のため、楕円一般のパーサーは持たない。
    private static (Vec2 Start, Vec2 End)[] Parse(string path)
    {
        var segments = new List<(Vec2, Vec2)>();
        var index = 0;
        var position = new Vec2(0, 0);
        var first = position;
        double Number()
        {
            while (index < path.Length && path[index] == ' ') { index++; }
            var start = index;
            while (index < path.Length && (char.IsDigit(path[index]) || path[index] is '.' or '-')) { index++; }
            return double.Parse(path.AsSpan(start, index - start), CultureInfo.InvariantCulture);
        }
        Vec2 Point() => new(Number(), Number());
        void Line(Vec2 end)
        {
            if (position != end) { segments.Add((position, end)); }
            position = end;
        }
        while (index < path.Length)
        {
            var command = path[index++];
            switch (command)
            {
                case ' ': break;
                case 'M': position = Point(); first = position; break;
                case 'L': Line(Point()); break;
                case 'l': Line(position + Point()); break;
                case 'H': Line(new(Number(), position.Y)); break;
                case 'h': Line(position + new Vec2(Number(), 0)); break;
                case 'V': Line(new(position.X, Number())); break;
                case 'v': Line(position + new Vec2(0, Number())); break;
                case 'Z': Line(first); break;
                case 'A':
                    var radius = Number();
                    _ = Number(); // ry は rx と同じ。
                    _ = Number(); // 回転は 0。
                    var large = Number() != 0;
                    var sweep = Number() != 0;
                    var end = Point();
                    var delta = end - position;
                    var distance = delta.Length;
                    var height = Math.Sqrt(Math.Max(0, radius * radius - distance * distance / 4));
                    var center = (position + end) * .5
                        + new Vec2(-delta.Y, delta.X) * ((large == sweep ? -1 : 1) * height / distance);
                    var angle = Math.Atan2(position.Y - center.Y, position.X - center.X);
                    var turn = Math.Atan2(end.Y - center.Y, end.X - center.X) - angle;
                    if (sweep && turn < 0) { turn += Math.Tau; }
                    if (!sweep && turn > 0) { turn -= Math.Tau; }
                    // 最大の半径 2.5 でも、200% の弦の誤差を 0.01 物理 px 未満にする。
                    var steps = (int)Math.Ceiling(Math.Abs(turn) * 16);
                    for (var step = 1; step <= steps; step++)
                    {
                        var current = angle + turn * step / steps;
                        Line(step == steps ? end : center + new Vec2(Math.Cos(current), Math.Sin(current)) * radius);
                    }
                    break;
                default: throw new InvalidOperationException("仕様にないアイコンの Path コマンド。");
            }
        }
        return segments.ToArray();
    }
}
