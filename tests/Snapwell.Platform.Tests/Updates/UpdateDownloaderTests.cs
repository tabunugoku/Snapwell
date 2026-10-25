using System.Net;
using System.Security.Cryptography;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>通信せず、配布 zip の検証、保存、失敗時の後始末を確かめる。</summary>
public sealed class UpdateDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-download-" + Guid.NewGuid());
    private string Destination => Path.Combine(_directory, "update.zip");

    /// <summary>各テスト専用の保存先を用意する。</summary>
    public UpdateDownloaderTests() => Directory.CreateDirectory(_directory);

    /// <summary>匿名 GET と版だけの User-Agent を使い、64 KiB ごとと最後に進捗を報告する。</summary>
    [Fact]
    public async Task SavesVerifiedFileAndReportsProgress()
    {
        var bytes = new byte[131089];
        Random.Shared.NextBytes(bytes);
        var reports = new List<long>();
        using var downloader = Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Snapwell/1.2.3", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Content);
            return Task.FromResult(Ok(new ByteArrayContent(bytes)));
        });
        var result = await downloader.DownloadAsync(Asset(bytes), Destination, new Reports(reports.Add), default);
        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Destination));
        Assert.False(File.Exists(Destination + ".part"));
        Assert.Equal(new long[] { 65536, 131072, bytes.Length }, reports);
    }

    /// <summary>本文が細切れで届いても進捗の報告を 64 KiB の境界にそろえる。</summary>
    [Fact]
    public async Task ReportsProgressForFragmentedBody()
    {
        var bytes = new byte[131089];
        var reports = new List<long>();
        using var downloader = Create((_, _) => Task.FromResult(Ok(new StreamContent(
            new BodyStream(bytes, maximumChunk: 7000)))));
        var result = await downloader.DownloadAsync(Asset(bytes), Destination, new Reports(reports.Add), default);
        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal(new long[] { 65536, 131072, bytes.Length }, reports);
    }

    /// <summary>空のファイルもハッシュで検証し、完了時の進捗を報告する。</summary>
    [Fact]
    public async Task SavesEmptyAsset()
    {
        var reports = new List<long>();
        using var downloader = Create((_, _) => Task.FromResult(Ok(new ByteArrayContent([]))));
        var result = await downloader.DownloadAsync(Asset([]), Destination, new Reports(reports.Add), default);
        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Empty(await File.ReadAllBytesAsync(Destination));
        Assert.Equal(new long[] { 0 }, reports);
    }

    /// <summary>サイズ、Content-Length、SHA256 の不一致を区別し、一時ファイルを消す。</summary>
    [Theory]
    [InlineData("hash", UpdateDownloadFailure.HashMismatch)]
    [InlineData("short", UpdateDownloadFailure.SizeMismatch)]
    [InlineData("long", UpdateDownloadFailure.SizeMismatch)]
    [InlineData("header-short", UpdateDownloadFailure.SizeMismatch)]
    [InlineData("header-long", UpdateDownloadFailure.SizeMismatch)]
    public async Task RejectsInvalidContent(string kind, UpdateDownloadFailure expected)
    {
        byte[] bytes = [1, 2, 3];
        var body = kind == "short" ? new byte[] { 1, 2 } : kind == "long" ? [1, 2, 3, 4] : bytes;
        var stream = new BodyStream(body);
        using var downloader = Create((_, _) =>
        {
            var content = new StreamContent(stream);
            if (kind.StartsWith("header-", StringComparison.Ordinal))
            {
                content.Headers.ContentLength = kind == "header-short" ? 2 : 4;
            }
            return Task.FromResult(Ok(content));
        });
        var asset = Asset(bytes);
        if (kind == "hash") { asset = asset with { Sha256 = new string('0', 64) }; }
        AssertFailed(await downloader.DownloadAsync(asset, Destination, null, default), expected);
        if (kind.StartsWith("header-", StringComparison.Ordinal)) { Assert.Equal(0, stream.BytesRead); }
    }

    /// <summary>失敗した HTTP ステータスの本文を保存しない。</summary>
    [Fact]
    public async Task RejectsNotFound()
    {
        using var downloader = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        AssertFailed(await downloader.DownloadAsync(Asset([1]), Destination, null, default),
            UpdateDownloadFailure.HttpStatus);
    }

    /// <summary>HTTPS、匿名 URL、許可ホストをすべて満たすときだけ次の要求を送る。</summary>
    [Theory]
    [InlineData("http://github.com/update.zip")]
    [InlineData("https://untrusted.example/update.zip")]
    [InlineData("https://user:password@github.com/update.zip")]
    public async Task RejectsUnsafeRedirect(string location)
    {
        var calls = 0;
        using var downloader = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Redirect(location));
        });
        AssertFailed(await downloader.DownloadAsync(Asset([1]), Destination, null, default),
            UpdateDownloadFailure.InsecureRedirect);
        Assert.Equal(1, calls);
    }

    /// <summary>最初の URL も HTTPS、匿名、許可ホストに限定する。</summary>
    [Theory]
    [InlineData("http://github.com/update.zip")]
    [InlineData("https://untrusted.example/update.zip")]
    [InlineData("https://user@github.com/update.zip")]
    public async Task RejectsUnsafeInitialUrl(string url)
    {
        var calls = 0;
        using var downloader = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Ok(new ByteArrayContent([1])));
        });
        AssertFailed(await downloader.DownloadAsync(Asset([1]) with { Url = new(url) }, Destination, null, default),
            UpdateDownloadFailure.InsecureRedirect);
        Assert.Equal(0, calls);
    }

    /// <summary>許可した別ホストへの転送と相対 URL の転送をたどる。</summary>
    [Fact]
    public async Task FollowsAllowedAndRelativeRedirects()
    {
        var requests = new List<Uri>();
        using var downloader = Create((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(requests.Count switch
            {
                1 => Redirect("https://objects.githubusercontent.com/start"),
                2 => Redirect("/update.zip"),
                _ => Ok(new ByteArrayContent([1, 2, 3])),
            });
        });
        var result = await downloader.DownloadAsync(Asset([1, 2, 3]), Destination, null, default);
        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal(new Uri("https://objects.githubusercontent.com/update.zip"), requests[^1]);
        Assert.Equal(3, requests.Count);
    }

    /// <summary>リダイレクトは十回までに制限し、Location の欠落も失敗として返す。</summary>
    [Theory]
    [InlineData(false, 11, UpdateDownloadFailure.NetworkError)]
    [InlineData(true, 1, UpdateDownloadFailure.HttpStatus)]
    public async Task RejectsInvalidRedirectChain(bool missingLocation, int expectedCalls,
        UpdateDownloadFailure expected)
    {
        var calls = 0;
        using var downloader = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(missingLocation ? new(HttpStatusCode.Redirect) : Redirect("/loop"));
        });
        AssertFailed(await downloader.DownloadAsync(Asset([1]), Destination, null, default), expected);
        Assert.Equal(expectedCalls, calls);
    }

    /// <summary>十回目のリダイレクトの先で成功する応答を受け取れる。</summary>
    [Fact]
    public async Task AllowsTenRedirects()
    {
        var calls = 0;
        using var downloader = Create((_, _) => Task.FromResult(++calls <= 10
            ? Redirect("/next") : Ok(new ByteArrayContent([1]))));
        Assert.True((await downloader.DownloadAsync(Asset([1]), Destination, null, default)).IsSuccess);
        Assert.Equal(11, calls);
    }

    /// <summary>ヘッダーまたは本文の通信エラーをファイルの I/O エラーと区別する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlesNetworkErrors(bool bodyError)
    {
        using var downloader = Create((_, _) => bodyError
            ? Task.FromResult(Ok(new StreamContent(new BodyStream([1], beforeRead: _ =>
                throw new IOException("Connection closed.")))))
            : throw new HttpRequestException("Connection failed."));
        AssertFailed(await downloader.DownloadAsync(Asset([1]), Destination, null, default),
            UpdateDownloadFailure.NetworkError);
    }

    /// <summary>本文の待機中でも利用者の取り消しに応じ、一時ファイルを消す。</summary>
    [Fact]
    public async Task CancelsWhileReading()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        using var downloader = Create((_, _) => Task.FromResult(Ok(new StreamContent(new BodyStream([1],
            beforeRead: async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            })))));
        var pending = downloader.DownloadAsync(Asset([1]), Destination, null, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        AssertFailed(await pending, UpdateDownloadFailure.Cancelled);
    }

    /// <summary>ヘッダーまたは本文が三十秒途絶えたときはタイムアウトとして返す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimesOutWhenIdle(bool body)
    {
        using var downloader = Create(async (_, token) =>
        {
            if (!body) { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            return Ok(new StreamContent(new BodyStream([1], beforeRead: readToken =>
                Task.Delay(Timeout.InfiniteTimeSpan, readToken))));
        });
        var result = await downloader.DownloadAsync(Asset([1]), Destination, null, default)
            .WaitAsync(TimeSpan.FromSeconds(45));
        AssertFailed(result, UpdateDownloadFailure.Timeout);
    }

    /// <summary>通信が続けば全体が三十秒を超えても取得し、読み取りごとに期限を更新する。</summary>
    [Fact]
    public async Task ContinuesWhileDataArrives()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6];
        using var downloader = Create((_, _) => Task.FromResult(Ok(new StreamContent(new BodyStream(bytes,
            beforeRead: token => Task.Delay(TimeSpan.FromSeconds(5), token), maximumChunk: 1)))));
        var result = await downloader.DownloadAsync(Asset(bytes), Destination, null, default)
            .WaitAsync(TimeSpan.FromSeconds(50));
        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Destination));
        Assert.False(File.Exists(Destination + ".part"));
    }

    /// <summary>開始前の取り消しでは通信もファイルの作成もしない。</summary>
    [Fact]
    public async Task CancelsBeforeStarting()
    {
        var calls = 0;
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        using var downloader = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Ok(new ByteArrayContent([1])));
        });
        AssertFailed(await downloader.DownloadAsync(Asset([1]), Destination, null, stop.Token),
            UpdateDownloadFailure.Cancelled);
        Assert.Equal(0, calls);
    }

    /// <summary>保存先フォルダーがなければ I/O の失敗として返す。</summary>
    [Fact]
    public async Task HandlesMissingDirectory()
    {
        using var downloader = Create((_, _) => Task.FromResult(Ok(new ByteArrayContent([1]))));
        var destination = Path.Combine(_directory, "missing", "update.zip");
        var result = await downloader.DownloadAsync(Asset([1]), destination, null, default);
        Assert.Equal(UpdateDownloadFailure.IoError, result.Failure);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    /// <summary>既存の確定ファイルは要求を送る前に保護する。</summary>
    [Fact]
    public async Task DoesNotOverwriteDestination()
    {
        byte[] existing = [9, 8, 7];
        await File.WriteAllBytesAsync(Destination, existing);
        var calls = 0;
        using var downloader = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Ok(new ByteArrayContent([1])));
        });
        var result = await downloader.DownloadAsync(Asset([1]), Destination, null, default);
        Assert.Equal(UpdateDownloadFailure.IoError, result.Failure);
        Assert.Equal(existing, await File.ReadAllBytesAsync(Destination));
        Assert.False(File.Exists(Destination + ".part"));
        Assert.Equal(0, calls);
    }

    /// <summary>取得中に確定先が作られても上書きせず、自分の一時ファイルだけを消す。</summary>
    [Fact]
    public async Task DoesNotOverwriteDestinationCreatedDuringDownload()
    {
        using var downloader = Create((_, _) => Task.FromResult(Ok(new ByteArrayContent([1]))));
        var result = await downloader.DownloadAsync(Asset([1]), Destination,
            new Reports(_ => File.WriteAllBytes(Destination, [9])), default);
        Assert.Equal(UpdateDownloadFailure.IoError, result.Failure);
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(Destination));
        Assert.False(File.Exists(Destination + ".part"));
    }

    /// <summary>別の取得の一時ファイルを上書きも削除もしない。</summary>
    [Fact]
    public async Task PreservesExistingPartFile()
    {
        await File.WriteAllBytesAsync(Destination + ".part", [9]);
        using var downloader = Create((_, _) => Task.FromResult(Ok(new ByteArrayContent([1]))));
        Assert.Equal(UpdateDownloadFailure.IoError,
            (await downloader.DownloadAsync(Asset([1]), Destination, null, default)).Failure);
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(Destination + ".part"));
        Assert.False(File.Exists(Destination));
    }

    /// <summary>無限に続く本文は上限の次の一バイトで打ち切り、上限までしかディスクに書かない。</summary>
    [Fact]
    public async Task BoundsEndlessResponse()
    {
        const int size = 65536;
        var stream = new BodyStream([], endless: true);
        var lengths = new List<long>();
        using var downloader = Create((_, _) => Task.FromResult(Ok(new StreamContent(stream))));
        var result = await downloader.DownloadAsync(Asset(new byte[size]), Destination,
            new Reports(_ => lengths.Add(new FileInfo(Destination + ".part").Length)), default);
        AssertFailed(result, UpdateDownloadFailure.SizeMismatch);
        Assert.Equal(size + 1, stream.BytesRead);
        Assert.Equal(new long[] { size }, lengths);
        Assert.InRange(stream.MaximumReadSize, 1, size);
    }

    /// <summary>書き込み済みでも取り消し時に一時ファイルを消す。</summary>
    [Fact]
    public async Task CancelsAfterWriting()
    {
        var bytes = new byte[65537];
        using var stop = new CancellationTokenSource();
        using var downloader = Create((_, _) => Task.FromResult(Ok(new StreamContent(new BodyStream(bytes)))));
        AssertFailed(await downloader.DownloadAsync(Asset(bytes), Destination,
            new Reports(_ => stop.Cancel()), stop.Token),
            UpdateDownloadFailure.Cancelled);
    }

    private void AssertFailed(UpdateDownloadResult result, UpdateDownloadFailure expected)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ".part"));
    }

    private static UpdateDownloader Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new("1.2.3+commit", static uri => uri.Host is "github.com" or "objects.githubusercontent.com",
            new Handler(send));

    private static UpdateAsset Asset(byte[] bytes) => new(new Uri("https://github.com/update.zip"),
        Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);

    private static HttpResponseMessage Ok(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>このテストが作ったファイルと専用フォルダーを消す。</summary>
    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_directory)) { File.Delete(file); }
        Directory.Delete(_directory);
    }

    private sealed class Reports(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    private sealed class BodyStream(byte[] bytes, bool endless = false,
        Func<CancellationToken, Task>? beforeRead = null, int maximumChunk = int.MaxValue) : Stream
    {
        internal long BytesRead { get; private set; }
        internal int MaximumReadSize { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeRead is not null) { await beforeRead(cancellationToken); }
            MaximumReadSize = Math.Max(MaximumReadSize, buffer.Length);
            var capacity = Math.Min(buffer.Length, maximumChunk);
            var count = endless ? capacity : (int)Math.Min(capacity, bytes.LongLength - BytesRead);
            if (endless) { buffer.Span[..count].Clear(); }
            else { bytes.AsMemory((int)BytesRead, count).CopyTo(buffer); }
            BytesRead += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
