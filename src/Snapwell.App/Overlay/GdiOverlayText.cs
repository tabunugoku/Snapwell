using System.Runtime.InteropServices;
using Snapwell.Core.Geometry;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>GDI の白黒の文字マスクを、乗算済み BGRA に合成する。</summary>
internal sealed unsafe partial class GdiOverlayText : IDisposable
{
    private readonly nint _dc;
    private readonly Dictionary<(int Height, bool Mono, bool Bold, string Face), nint> _fonts = [];
    private readonly double _scale;

    internal GdiOverlayText(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        _scale = scale;
        _dc = CreateCompatibleDC(0);
        if (_dc == 0)
        {
            throw new InvalidOperationException("文字用の DC を作れない。");
        }

        SetBkMode(_dc, 1);
        SetTextColor(_dc, 0xFFFFFF);
    }

    /// <summary>縮小後も維持する元サイズの文字幅を論理 px で測る。</summary>
    internal double Measure(string text, double size, bool mono, bool bold = false)
    {
        var catalog = UiText.Current;
        var face = CjkFontFamily(catalog.Language);
        return SplitRuns(text, mono).Sum(run => MeasureRunPixels(run, size, mono, bold, face)) / _scale;
    }

    /// <summary>非 ASCII の文字を描く書体を言語から選ぶ。</summary>
    internal static string CjkFontFamily(UiLanguage language) => language switch
    {
        UiLanguage.ChineseSimplified => "Microsoft YaHei UI",
        UiLanguage.ChineseTraditional => "Microsoft JhengHei UI",
        _ => "Yu Gothic UI",
    };

    private static bool UsesCjk((string Value, bool Cjk) run, bool mono) =>
        run.Cjk || !mono && run.Value.Any(value => value > 0x7F && value is not ('×' or '·'));

    /// <summary>等幅の数字と記号を保ち、非 ASCII の連続を CJK 用のフォントへ分ける。</summary>
    internal static (string Value, bool Cjk)[] SplitRuns(string text, bool mono, bool spaced = false)
    {
        if (text.Length == 0)
        {
            return [];
        }

        // サイズ表示の × と · は Cascadia Mono に字形があるため、数字と同じ run に残す。
        static bool Cjk(int value) => value > 0x7F && value is not ('×' or '·');

        if (spaced)
        {
            return text.EnumerateRunes().Select(rune =>
                (rune.ToString(), mono && Cjk(rune.Value))).ToArray();
        }

        if (!mono)
        {
            return [(text, false)];
        }

        var runs = new List<(string Value, bool Cjk)>();
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index == text.Length || Cjk(text[index]) != Cjk(text[start]))
            {
                runs.Add((text[start..index], Cjk(text[start])));
                start = index;
            }
        }

        return runs.ToArray();
    }

    private int MeasureRunPixels((string Value, bool Cjk) run, double size, bool mono, bool bold, string face,
        int samples = 1)
    {
        var previous = SelectObject(_dc, Font(size, mono, bold, samples, UsesCjk(run, mono), face));
        try
        {
            var rect = new NativeRect();
            fixed (char* value = run.Value)
            {
                DrawTextW(_dc, value, run.Value.Length, &rect, 0x20 | 0x400 | 0x800);
            }

            return rect.Right;
        }
        finally
        {
            SelectObject(_dc, previous);
        }
    }

    /// <summary>上から下の 32 bit DIB へ DrawTextW で描き、被覆率を alpha に変換する。</summary>
    internal void Draw(OverlayFrame frame, PixelRect area, LauncherText text, OverlayColor color,
        bool centerFontBody = false)
    {
        var catalog = UiText.Current;
        var bounds = text.Bounds;
        if (text.Value.Length == 0 || bounds.Width == 0 || bounds.Height == 0)
        {
            return;
        }

        // 文字はすべて 4 倍解像度で描いて縮小する (区画のサイズ表示も同じ。短い文字列のため差分描画でも軽い)。
        var mask = SupersampledMask(text, color, centerFontBody, CjkFontFamily(catalog.Language));
        OverlayPainter.Composite(frame, mask, bounds.X - area.X, bounds.Y - area.Y);
    }

    private OverlayFrame SupersampledMask(LauncherText text, OverlayColor color, bool centerFontBody, string face)
    {
        const int Samples = 4;
        var bounds = text.Bounds;
        var result = OverlayPainter.Create(bounds.Width, bounds.Height);
        var runs = SplitRuns(text.Value, text.Mono, text.LetterSpacing > 0);
        var widths = runs.Select(run => MeasureRunPixels(run, text.Size, text.Mono, text.Bold, face)).ToArray();
        var spacing = text.LetterSpacing * _scale;
        var measuredWidth = widths.Sum() + Math.Max(0, runs.Length - 1) * spacing;
        var x = text.Center ? CenteredTextLeft(bounds.Width, measuredWidth) : 0;
        for (var index = 0; index < runs.Length; index++)
        {
            var width = widths[index];
            var sourceWidth = MeasureRunPixels(runs[index], text.Size, text.Mono, text.Bold, face, Samples);
            if (width > 0 && sourceWidth > 0)
            {
                // 計測は元のフォントのままにし、高解像度で変わる送り幅だけを縮小時に合わせる。
                var run = text with
                {
                    Value = runs[index].Value,
                    Bounds = new(0, 0, sourceWidth, checked(bounds.Height * Samples)),
                    Center = false,
                    LetterSpacing = 0,
                };
                var font = Font(text.Size, text.Mono, text.Bold, Samples, UsesCjk(runs[index], text.Mono), face);
                var mask = Rasterize(run, color, font, Samples, width, centerFontBody);
                var left = text.LetterSpacing > 0 ? (int)Math.Round(x) : (int)x;
                OverlayPainter.Composite(result, mask, left, 0);
            }

            x += width + spacing;
        }

        return result;
    }

    /// <summary>描画と同じフォントで測った送り幅を、ラベルの全幅の中央へ置く。</summary>
    internal static double CenteredTextLeft(double boxWidth, double measuredWidth) => (boxWidth - measuredWidth) / 2;

    /// <summary>実測した字面の ascent と descent を中央に置き、行の描画原点へ戻す。</summary>
    internal static int CenteredFontTop(int boxHeight, int ascent, int inkAscent, int inkDescent)
    {
        var baseline = (boxHeight - inkAscent - inkDescent) / 2.0 + inkAscent;
        return (int)Math.Round(baseline - ascent);
    }

    private TextMetrics MeasureFont(nint font)
    {
        var previous = SelectObject(_dc, font);
        try
        {
            return GetTextMetricsW(_dc, out var metrics) != 0 ? metrics
                : throw new InvalidOperationException("文字の行の高さを測れない。");
        }
        finally
        {
            SelectObject(_dc, previous);
        }
    }

    private static byte[] CenterFontPixels(ReadOnlySpan<byte> pixels, int width, int lineHeight,
        int boxHeight, int ascent)
    {
        var top = lineHeight;
        var bottom = 0;
        var stride = checked(width * 4);
        for (var y = 0; y < lineHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * stride + x * 4;
                if (pixels[index] != 0 || pixels[index + 1] != 0 || pixels[index + 2] != 0)
                {
                    top = Math.Min(top, y);
                    bottom = y + 1;
                    break;
                }
            }
        }

        var result = new byte[checked(stride * boxHeight)];
        if (bottom == 0) { return result; }
        // 日本語・等幅・UI フォントの余白を推測せず、ベースラインから字面の上下端を測る。
        var offset = CenteredFontTop(boxHeight, ascent, ascent - top, bottom - ascent);
        for (var y = top; y < bottom; y++)
        {
            var destination = y + offset;
            if (destination >= 0 && destination < boxHeight)
            {
                pixels.Slice(y * stride, stride).CopyTo(result.AsSpan(destination * stride, stride));
            }
        }

        return result;
    }

    private OverlayFrame Rasterize(LauncherText text, OverlayColor color, nint font, int samples, int width,
        bool centerFontBody)
    {
        var bounds = text.Bounds;
        var metrics = centerFontBody ? MeasureFont(font) : default;
        var rasterHeight = centerFontBody ? metrics.Height : bounds.Height;
        var info = new BitmapHeader
        {
            Size = (uint)sizeof(BitmapHeader),
            Width = bounds.Width,
            Height = -rasterHeight,
            Planes = 1,
            BitCount = 32,
        };
        void* bits = null;
        var bitmap = CreateDIBSection(_dc, &info, 0, &bits, 0, 0);
        if (bitmap == 0 || bits == null)
        {
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            throw new InvalidOperationException("文字用の DIB を作れない。");
        }

        var previousBitmap = SelectObject(_dc, bitmap);
        var previousFont = SelectObject(_dc, font);
        try
        {
            var pixels = new Span<byte>(bits, checked(bounds.Width * rasterHeight * 4));
            pixels.Clear();
            var rect = new NativeRect { Right = bounds.Width, Bottom = rasterHeight };
            // 省略は配置側で対象名にだけ行う。案内に DT_END_ELLIPSIS を付けない。
            var flags = 0x20u | 0x4u | 0x800u | (text.Center ? 1u : 0u);
            if (centerFontBody)
            {
                flags &= ~0x4u; // 行全体を DT_TOP で描き、字面の実測後に中央へ移す。
            }
            fixed (char* value = text.Value)
            {
                DrawTextW(_dc, value, text.Value.Length, &rect, flags);
            }

            GdiFlush();
            ReadOnlySpan<byte> source = centerFontBody
                ? CenterFontPixels(pixels, bounds.Width, rasterHeight, bounds.Height, metrics.Ascent) : pixels;
            if (samples > 1)
            {
                return OverlayTextMask.Downsample(source, bounds.Width, width,
                    bounds.Height / samples, samples, color);
            }

            var mask = new OverlayFrame(bounds.Width, bounds.Height, source.ToArray());
            for (var index = 0; index < mask.Pixels.Length; index += 4)
            {
                // GDI は alpha を保存しない。灰色の被覆率から自分で復元する。
                var coverage = Math.Max(mask.Pixels[index], Math.Max(mask.Pixels[index + 1], mask.Pixels[index + 2]));
                var alpha = (byte)((coverage * color.Alpha + 127) / 255);
                mask.Pixels[index] = (byte)((color.Blue * alpha + 127) / 255);
                mask.Pixels[index + 1] = (byte)((color.Green * alpha + 127) / 255);
                mask.Pixels[index + 2] = (byte)((color.Red * alpha + 127) / 255);
                mask.Pixels[index + 3] = alpha;
            }

            return mask;
        }
        finally
        {
            SelectObject(_dc, previousFont);
            SelectObject(_dc, previousBitmap);
            DeleteObject(bitmap);
        }
    }

    /// <summary>DC に選択されていないフォントと DC を解放する。</summary>
    public void Dispose()
    {
        foreach (var font in _fonts.Values.Distinct())
        {
            DeleteObject(font);
        }

        _fonts.Clear();
        DeleteDC(_dc);
    }

    private nint Font(double size, bool mono, bool bold, int samples = 1, bool cjk = false,
        string cjkFace = "Yu Gothic UI")
    {
        var height = Math.Max(1, (int)Math.Round(size * _scale * samples));
        var requested = cjk ? cjkFace : mono ? "Cascadia Mono" : "Segoe UI Variable Text";
        var key = (height, mono, bold, requested);
        if (_fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = CreateFont(requested, height, bold);
        var previous = SelectObject(_dc, font);
        Span<char> face = stackalloc char[64];
        int count;
        fixed (char* name = face)
        {
            count = GetTextFaceW(_dc, face.Length, name);
        }

        SelectObject(_dc, previous);
        var resolved = new string(face[..Math.Clamp(count, 0, face.Length)]).TrimEnd('\0');
        if (!string.Equals(resolved, requested, StringComparison.OrdinalIgnoreCase))
        {
            DeleteObject(font);
            if (cjk)
            {
                // CJK 用の書体がなければ従来のフォントを共有する。Dispose で重複を除いて解放する。
                font = Font(size, mono, bold, samples);
                _fonts.Add(key, font);
                return font;
            }

            font = CreateFont(mono ? "Consolas" : "Segoe UI", height, bold);
        }

        _fonts.Add(key, font);
        return font;
    }

    private static nint CreateFont(string face, int height, bool bold)
    {
        fixed (char* name = face)
        {
            // ClearType の色の縁は半透明の面で破綻するため、灰色のアンチエイリアスを選ぶ。
            var font = CreateFontW(-height, 0, 0, 0, bold ? 600 : 400, 0, 0, 0, 1, 0, 0, 4, 0, name);
            return font != 0 ? font : throw new InvalidOperationException("文字のフォントを作れない。");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetrics
    {
        internal int Height, Ascent, Descent, InternalLeading, ExternalLeading;
        internal int AverageWidth, MaximumWidth, Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        internal ushort FirstChar, LastChar, DefaultChar, BreakChar;
        internal byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, SizeImage;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ClrUsed, ClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(nint value);
    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint dc, int mode);
    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport("gdi32.dll")]
    private static partial int GdiFlush();
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapHeader* info, uint usage, void** bits,
        nint section, uint offset);
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, char* face);
    [LibraryImport("gdi32.dll")]
    private static partial int GetTextFaceW(nint dc, int count, char* face);
    [LibraryImport("gdi32.dll")]
    private static partial int GetTextMetricsW(nint dc, out TextMetrics metrics);
    [LibraryImport("user32.dll")]
    private static partial int DrawTextW(nint dc, char* text, int length, NativeRect* rect, uint format);
}
