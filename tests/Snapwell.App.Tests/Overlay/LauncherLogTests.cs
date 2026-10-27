using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Snapwell.App.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>失敗ログの容量と、個人情報を残さない例外の扱いを検証する。</summary>
public sealed class LauncherLogTests
{
    /// <summary>排出が完了した時点で全行を保存し、排出後の書き込みは受け付けない。</summary>
    [Fact]
    public void FlushPersistsQueuedLinesAndCompletesWriter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapwell-launcher-{Guid.NewGuid():N}.log");
        var writer = new LauncherLogWriter(path);
        try
        {
            for (var index = 0; index < 20; index++)
            {
                writer.Write(index, "stop-timeout", null);
            }

            Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
            var lines = File.ReadAllLines(path);
            Assert.Equal(20, lines.Length);
            for (var index = 0; index < lines.Length; index++)
            {
                Assert.Contains($"generation={index} state=stop-timeout", lines[index]);
            }

            writer.Write(20, "after-flush", null);
            Assert.True(writer.Flush(TimeSpan.Zero));
            Assert.Equal(lines, File.ReadAllLines(path));
        }
        finally
        {
            writer.Flush(TimeSpan.FromSeconds(2));
            File.Delete(path);
        }
    }

    /// <summary>保存が止まっても排出の待機を打ち切り、保存の再開後は残りの行を排出する。</summary>
    [Fact]
    public void FlushReturnsWithinLimitWhenWriterIsBlocked()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapwell-launcher-{Guid.NewGuid():N}.log");
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var writer = new LauncherLogWriter(path, (destination, line) =>
        {
            entered.Set();
            release.Wait();
            LauncherLog.Append(destination, line);
        });
        try
        {
            writer.Write(1, "stop-wake-failed", null);
            Assert.True(entered.Wait(2000));
            writer.Write(2, "stop-timeout", null);
            var elapsed = Stopwatch.StartNew();
            Assert.False(writer.Flush(TimeSpan.FromMilliseconds(50)));
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
            release.Set();
            Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
            Assert.Equal(2, File.ReadAllLines(path).Length);
        }
        finally
        {
            release.Set();
            writer.Flush(TimeSpan.FromSeconds(2));
            File.Delete(path);
        }
    }

    /// <summary>保存先の障害でもワーカーが終了し、排出から例外を漏らさない。</summary>
    [Fact]
    public void FlushDoesNotThrowWhenAppendFails()
    {
        var writer = new LauncherLogWriter("unused", (_, _) => throw new IOException());
        writer.Write(1, "stop-timeout", null);
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
    }

    /// <summary>古い行を捨てても最新の行と UTF-8 の境界を保つ。</summary>
    [Fact]
    public void RotationPreservesLatestCompleteLinesWithinByteLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapwell-launcher-{Guid.NewGuid():N}.log");
        try
        {
            for (var index = 0; index < 400; index++)
            {
                LauncherLog.Append(path, $"{index:D4}:{new string('あ', 100)}\n");
            }

            var bytes = File.ReadAllBytes(path);
            Assert.InRange(bytes.Length, 1, LauncherLog.MaxBytes);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.DoesNotContain("0000:", text);
            Assert.StartsWith("0399:", lines[^1]);
            Assert.All(lines, line => Assert.Equal(105, line.Length));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>種類とエラー番号を残し、任意の例外本文や改行は記録しない。</summary>
    [Fact]
    public void UnknownExceptionMessagesCannotExposeTitlesOrPaths()
    {
        var line = LauncherLog.Format(DateTimeOffset.UnixEpoch, 12, "paint-failed",
            new InvalidOperationException("private window title\nC:\\private\\file.txt"));
        Assert.Contains("generation=12 state=paint-failed exception=InvalidOperationException", line);
        Assert.Contains("message=[redacted]", line);
        Assert.DoesNotContain("private", line);
        Assert.Equal(1, line.Count(character => character == '\n'));
    }

    /// <summary>ネイティブの失敗では、固定メッセージと Win32 エラー番号を保持する。</summary>
    [Fact]
    public void NativeFailureIncludesSafeMessageAndErrorCode()
    {
        var line = LauncherLog.Format(DateTimeOffset.UnixEpoch, 3, "wake-failed",
            new Win32Exception(1816, "オーバーレイの起床通知を送れない。"));
        Assert.Contains("exception=Win32Exception code=1816", line);
        Assert.Contains("message=オーバーレイの起床通知を送れない。", line);
    }
}
