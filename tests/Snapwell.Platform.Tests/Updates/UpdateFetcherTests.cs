using System.Net;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>実際に通信せず、取得の上限と送信内容を検証する。</summary>
public sealed class UpdateFetcherTests
{
    /// <summary>ビルドのメタデータ (+ 以降の SHA) は User-Agent に含めない。</summary>
    [Fact]
    public async Task UserAgentOmitsBuildMetadata()
    {
        string? userAgent = null;
        using var fetcher = new UpdateFetcher("1.2.3+353f59a", new Handler((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));
        await fetcher.FetchAsync(CancellationToken.None);
        Assert.Equal("Snapwell/1.2.3", userAgent);
    }

    /// <summary>二つの GET と User-Agent だけを使い、認証と Cookie は送らない。</summary>
    [Fact]
    public async Task FetchesBothFilesWithAnonymousGet()
    {
        var requests = new List<Uri>();
        using var fetcher = new UpdateFetcher("1.2.3", new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Snapwell/1.2.3", request.Headers.UserAgent.ToString());
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) });
        }));
        var result = await fetcher.FetchAsync(default);
        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 1, 2 }, result.Manifest);
        Assert.Equal(new byte[] { 1, 2 }, result.Signature);
        Assert.Equal(new[] { UpdateEndpoints.Manifest, UpdateEndpoints.Signature }, requests);
    }

    /// <summary>HTTP の失敗と HTTPS からの降格を分類する。</summary>
    [Theory]
    [InlineData(false, UpdateFetchFailure.HttpStatus)]
    [InlineData(true, UpdateFetchFailure.InsecureRedirect)]
    public async Task RejectsStatusAndDowngrade(bool redirect, UpdateFetchFailure expected)
    {
        var calls = 0;
        using var fetcher = new UpdateFetcher("1.2.3", new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(redirect ? HttpStatusCode.Redirect : HttpStatusCode.NotFound);
            response.Headers.Location = new Uri("http://example.com/latest.json");
            return Task.FromResult(response);
        }));
        Assert.Equal(expected, (await fetcher.FetchAsync(default)).Failure);
        Assert.Equal(1, calls);
    }

    /// <summary>Content-Length がない本文にも上限を適用する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundsManifestAndSignatureStreams(bool signature)
    {
        var calls = 0;
        using var fetcher = new UpdateFetcher("1.2.3", new Handler((_, _) =>
        {
            var size = signature && calls++ == 0 ? 1 : signature ? 1025 : 65537;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new NonSeekableStream(new byte[size])) });
        }));
        Assert.Equal(UpdateFetchFailure.TooLarge, (await fetcher.FetchAsync(default)).Failure);
    }

    /// <summary>タイムアウトと利用者による取り消しを例外にしない。</summary>
    [Fact]
    public async Task HandlesTimeoutAndCancellation()
    {
        using var timeout = new UpdateFetcher("1.2.3", new Handler((_, _) => throw new TaskCanceledException()));
        Assert.Equal(UpdateFetchFailure.Timeout, (await timeout.FetchAsync(default)).Failure);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new UpdateFetcher("1.2.3", new Handler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        }));
        using var stop = new CancellationTokenSource();
        var pending = cancelled.FetchAsync(stop.Token);
        await entered.Task;
        stop.Cancel();
        Assert.False((await pending).IsSuccess);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
