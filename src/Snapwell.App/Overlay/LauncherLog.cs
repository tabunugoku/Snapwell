using System.ComponentModel;
using System.Text;
using System.Threading.Channels;

namespace Snapwell.App.Overlay;

/// <summary>失敗と回復だけを有限キューへ渡し、ファイルの入出力を描画から分離する。</summary>
internal static class LauncherLog
{
    internal const int MaxBytes = 64 * 1024;
    private static readonly Channel<Entry> _entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
    private static readonly Task _writer = Task.Run(WriteEntries);

    /// <summary>通常の成功では呼ばず、呼び出し側ではファイルも例外の本文も扱わない。</summary>
    internal static void Write(long generation, string state, Exception? exception) =>
        _entries.Writer.TryWrite(new(DateTimeOffset.Now, generation, state, exception));

    private static async Task WriteEntries()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Snapwell", "launcher.log");
        await foreach (var entry in _entries.Reader.ReadAllAsync())
        {
            try
            {
                Append(path, Format(entry.Time, entry.Generation, entry.State, entry.Exception));
            }
            catch (Exception)
            {
                // ログ保存に失敗しても表示の回復や OS 設定の復元は止めない。
            }
        }
    }

    /// <summary>既知の固定メッセージだけを記録し、パスやタイトルを含み得る本文は伏せる。</summary>
    internal static string Format(DateTimeOffset time, long generation, string state, Exception? exception)
    {
        exception = exception?.GetBaseException();
        var message = exception?.Message switch
        {
            null => "-",
            "矢印カーソルを読み込めない。" or
            "オーバーレイのクラスを登録できない。" or
            "オーバーレイを作れない。" or
            "オーバーレイのウィンドウが失われた。" or
            "オーバーレイの DIB を作れない。" or
            "文字用の DC を作れない。" or
            "文字用の DIB を作れない。" or
            "文字のフォントを作れない。" or
            "オーバーレイを更新できない。" or
            "オーバーレイの起床通知を送れない。" or
            "オーバーレイのメッセージを取得できない。" or
            "オーバーレイの入力処理に失敗した。" => exception.Message,
            _ => "[redacted]",
        };
        var code = exception is Win32Exception native ? native.NativeErrorCode : exception?.HResult ?? 0;
        return $"{time:O} generation={generation} state={state} " +
            $"exception={exception?.GetType().Name ?? "-"} code={code} message={message}\n";
    }

    /// <summary>末尾の完全な行だけを残し、UTF-8 のバイト数を 64 KB 以下に保つ。</summary>
    internal static void Append(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length > MaxBytes)
        {
            return;
        }

        if (file.Length + bytes.Length > MaxBytes)
        {
            var retained = new byte[(int)Math.Min(file.Length, MaxBytes - bytes.Length)];
            var start = file.Length - retained.Length;
            file.Position = start;
            file.ReadExactly(retained);
            var first = start == 0 ? 0 : Array.IndexOf(retained, (byte)'\n') + 1;
            if (start > 0 && first == 0)
            {
                first = retained.Length;
            }

            file.Position = 0;
            file.Write(retained.AsSpan(first));
            file.SetLength(file.Position);
        }

        file.Position = file.Length;
        file.Write(bytes);
    }

    private sealed record Entry(DateTimeOffset Time, long Generation, string State, Exception? Exception);
}
