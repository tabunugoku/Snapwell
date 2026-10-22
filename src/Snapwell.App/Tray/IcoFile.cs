using System.Buffers.Binary;

namespace Snapwell.App.Tray;

/// <summary>.ico の 1 フレーム。Data は CreateIconFromResourceEx に渡せる画像データ (PNG か DIB)。</summary>
internal readonly record struct IcoFrame(int Size, ReadOnlyMemory<byte> Data);

/// <summary>.ico のフレームの一覧を読む。トレイは DPI ごとのサイズを埋め込みの .ico から選ぶため。</summary>
internal static class IcoFile
{
    private const int HeaderLength = 6;
    private const int EntryLength = 16;

    /// <summary>壊れた .ico は例外にする (埋め込み素材なので、実行時に壊れることはない)。</summary>
    public static IReadOnlyList<IcoFrame> Parse(ReadOnlyMemory<byte> file)
    {
        var bytes = file.Span;
        if (bytes.Length < HeaderLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) != 1)
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            throw new InvalidDataException("アイコンのヘッダーが不正。");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        var frames = new List<IcoFrame>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = HeaderLength + (i * EntryLength);
            if (bytes.Length < entry + EntryLength)
            {
                // i18n-keep: 画面に出さない開発者向けの例外・ログ。
                throw new InvalidDataException("アイコンのディレクトリが途中で切れている。");
            }

            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 8)..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 12)..]);
            if (offset > bytes.Length || length > bytes.Length - offset)
            {
                // i18n-keep: 画面に出さない開発者向けの例外・ログ。
                throw new InvalidDataException("アイコンの画像が範囲外。");
            }

            frames.Add(new IcoFrame(width, file.Slice((int)offset, (int)length)));
        }

        return frames;
    }
}
