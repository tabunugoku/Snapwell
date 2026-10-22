using System.ComponentModel;
using System.Text;
using System.Threading.Channels;

namespace Snapwell.App.Overlay;

/// <summary>失敗と回復だけを有限キューへ渡し、ファイルの入出力を描画から分離する。</summary>
internal static class LauncherLog
{
    internal const int MaxBytes = 64 * 1024;
    private static readonly LauncherLogWriter _writer = new();

    /// <summary>通常の成功では呼ばず、呼び出し側ではファイルも例外の本文も扱わない。</summary>
    internal static void Write(long generation, string state, Exception? exception) =>
        _writer.Write(generation, state, exception);

    /// <summary>終了時だけキューを完了し、指定した上限まで保存の完了を待つ。</summary>
    internal static bool Flush(TimeSpan timeout) => _writer.Flush(timeout);

    /// <summary>既知の固定メッセージだけを記録し、パスやタイトルを含み得る本文は伏せる。</summary>
    internal static string Format(DateTimeOffset time, long generation, string state, Exception? exception)
    {
        exception = exception?.GetBaseException();
        var message = exception?.Message switch
        {
            null => "-",
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "矢印カーソルを読み込めない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイのクラスを登録できない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイを作れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイのウィンドウが失われた。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイの DIB を作れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "文字用の DC を作れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "文字用の DIB を作れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "文字のフォントを作れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイを更新できない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイの起床通知を送れない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
            "オーバーレイのメッセージを取得できない。" or
            // i18n-keep: ログで伏せずに記録してよい例外の照合文字列。
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
}

/// <summary>終了時に排出でき、保存先と書き込み処理を差し替えられるログのキュー。</summary>
internal sealed class LauncherLogWriter
{
    private readonly Channel<Entry> _entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
    private readonly Task _writer;

    internal LauncherLogWriter(string? path = null, Action<string, string>? append = null)
    {
        _writer = Task.Run(async () =>
        {
            await foreach (var entry in _entries.Reader.ReadAllAsync())
            {
                try
                {
                    // 保存先の取得も呼び出し側の描画スレッドには持ち込まない。
                    path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Snapwell", "launcher.log");
                    (append ?? LauncherLog.Append)(path,
                        LauncherLog.Format(entry.Time, entry.Generation, entry.State, entry.Exception));
                }
                catch (Exception)
                {
                    // ログ保存に失敗しても表示の回復や OS 設定の復元は止めない。
                }
            }
        });
    }

    /// <summary>終了の完了後の通知は捨て、呼び出し側では保存を待たない。</summary>
    internal void Write(long generation, string state, Exception? exception) =>
        _entries.Writer.TryWrite(new(DateTimeOffset.Now, generation, state, exception));

    /// <summary>受付を終了し、保存処理が止まっていても指定した上限で戻る。</summary>
    internal bool Flush(TimeSpan timeout)
    {
        _entries.Writer.TryComplete();
        return _writer.Wait(timeout);
    }

    private sealed record Entry(DateTimeOffset Time, long Generation, string State, Exception? Exception);
}
