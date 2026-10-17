using System.Runtime.InteropServices;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>GDI の白黒の文字マスクを、乗算済み BGRA に合成する。</summary>
internal sealed unsafe partial class GdiOverlayText : IDisposable
{
    private readonly nint _dc;
    private readonly Dictionary<(int Height, bool Mono, bool Bold), nint> _fonts = [];
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

    /// <summary>描画と同じフォントで論理 px の文字幅を測る。</summary>
    internal double Measure(string text, double size, bool mono, bool bold = false)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var previous = SelectObject(_dc, Font(size, mono, bold));
        try
        {
            var rect = new NativeRect();
            fixed (char* value = text)
            {
                DrawTextW(_dc, value, text.Length, &rect, 0x20 | 0x400 | 0x800);
            }

            return rect.Right / _scale;
        }
        finally
        {
            SelectObject(_dc, previous);
        }
    }

    /// <summary>上から下の 32 bit DIB へ DrawTextW で描き、被覆率を alpha に変換する。</summary>
    internal void Draw(OverlayFrame frame, PixelRect area, LauncherText text, OverlayColor color)
    {
        var bounds = text.Bounds;
        if (text.Value.Length == 0 || bounds.Width == 0 || bounds.Height == 0)
        {
            return;
        }

        var font = Font(text.Size, text.Mono, text.Bold);
        var info = new BitmapHeader
        {
            Size = (uint)sizeof(BitmapHeader),
            Width = bounds.Width,
            Height = -bounds.Height,
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
            var pixels = new Span<byte>(bits, checked(bounds.Width * bounds.Height * 4));
            pixels.Clear();
            var rect = new NativeRect { Right = bounds.Width, Bottom = bounds.Height };
            var flags = 0x20u | 0x4u | 0x800u | 0x8000u | (text.Center ? 1u : 0u);
            if (text.LetterSpacing > 0)
            {
                DrawSpaced(text, rect, flags);
            }
            else
            {
                fixed (char* value = text.Value)
                {
                    DrawTextW(_dc, value, text.Value.Length, &rect, flags);
                }
            }

            GdiFlush();
            var mask = new OverlayFrame(bounds.Width, bounds.Height, pixels.ToArray());
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

            OverlayPainter.Composite(frame, mask, bounds.X - area.X, bounds.Y - area.Y);
        }
        finally
        {
            SelectObject(_dc, previousFont);
            SelectObject(_dc, previousBitmap);
            DeleteObject(bitmap);
        }
    }

    private void DrawSpaced(LauncherText text, NativeRect rect, uint flags)
    {
        var glyphs = text.Value.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        var widths = glyphs.Select(glyph => Measure(glyph, text.Size, text.Mono, text.Bold) * _scale).ToArray();
        var spacing = text.LetterSpacing * _scale;
        var x = text.Center ? (rect.Right - widths.Sum() - glyphs.Length * spacing) / 2 : 0;
        for (var index = 0; index < glyphs.Length; index++)
        {
            rect.Left = (int)Math.Round(x);
            fixed (char* value = glyphs[index])
            {
                DrawTextW(_dc, value, glyphs[index].Length, &rect, flags & ~1u);
            }

            x += widths[index] + spacing;
        }
    }

    /// <summary>DC に選択されていないフォントと DC を解放する。</summary>
    public void Dispose()
    {
        foreach (var font in _fonts.Values)
        {
            DeleteObject(font);
        }

        _fonts.Clear();
        DeleteDC(_dc);
    }

    private nint Font(double size, bool mono, bool bold)
    {
        var height = Math.Max(1, (int)Math.Round(size * _scale));
        var key = (height, mono, bold);
        if (_fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var requested = mono ? "Cascadia Mono" : "Segoe UI Variable Text";
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
    [LibraryImport("user32.dll")]
    private static partial int DrawTextW(nint dc, char* text, int length, NativeRect* rect, uint format);
}
