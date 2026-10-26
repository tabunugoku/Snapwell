using System.Text;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>不正な結果や巨大なファイルを画面に出さないことを検証する。</summary>
public sealed class UpdateResultReaderTests
{
    /// <summary>進行役の四種類の結果と版の省略を読む。</summary>
    [Theory]
    [InlineData("installed")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("rolled-back")]
    public void ReadsKnownResults(string result)
    {
        var bytes = Encoding.UTF8.GetBytes($$"""
            {"result":"{{result}}","code":23,"utc":"2026-10-26T00:00:00Z","version":null}
            """);
        var read = UpdateResultReader.Parse(bytes);
        Assert.Equal(result, read!.Result);
        Assert.Equal(23, read.Code);
        Assert.Null(read.Version);
        Assert.Equal(TimeSpan.Zero, read.Utc.Offset);
    }

    /// <summary>破損、未知の値、型違い、必須値の不足と重複を無視する。</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"result\":\"unknown\",\"code\":0,\"utc\":\"2026-10-26T00:00:00Z\",\"version\":null}")]
    [InlineData("{\"result\":\"failed\",\"code\":\"0\",\"utc\":\"2026-10-26T00:00:00Z\",\"version\":null}")]
    [InlineData("{\"result\":\"failed\",\"code\":0,\"utc\":\"bad\",\"version\":null}")]
    [InlineData("{\"result\":\"failed\",\"code\":0,\"utc\":\"2026-10-26T00:00:00Z\",\"version\":42}")]
    [InlineData("{\"result\":\"failed\",\"code\":0,\"code\":1,\"utc\":\"2026-10-26T00:00:00Z\",\"version\":null}")]
    public void IgnoresInvalidResults(string json) => Assert.Null(UpdateResultReader.Parse(Encoding.UTF8.GetBytes(json)));

    /// <summary>ファイルの有無、上限、読み取り後の削除を確かめる。</summary>
    [Fact]
    public void FileIsBoundedAndCanBeConsumed()
    {
        var path = Path.Combine(Path.GetTempPath(), "snapwell-result-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(UpdateResultReader.Read(path));
            var json = """{"result":"installed","code":0,"utc":"2026-10-26T00:00:00Z","version":"1.2.0"}""";
            File.WriteAllText(path, json.PadRight(16 * 1024));
            Assert.Equal("1.2.0", UpdateResultReader.Read(path)!.Version);
            File.AppendAllText(path, " ");
            Assert.Null(UpdateResultReader.Read(path));
            UpdateResultReader.TryDelete(path);
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
