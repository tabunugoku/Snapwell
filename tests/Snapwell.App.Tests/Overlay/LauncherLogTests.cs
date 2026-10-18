using System.ComponentModel;
using System.Text;
using Snapwell.App.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>失敗ログの容量と、個人情報を残さない例外の扱いを検証する。</summary>
public sealed class LauncherLogTests
{
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
