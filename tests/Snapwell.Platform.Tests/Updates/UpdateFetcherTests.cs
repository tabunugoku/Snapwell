using System.Net;
using System.Text;
using Snapwell.Core.Settings;
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

    /// <summary>HTTP の状態を保ち、429 と 5xx だけを一時的な失敗として返す。</summary>
    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(410, false)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(599, true)]
    public async Task HttpFailurePreservesRetryClassification(int status, bool transient)
    {
        using var fetcher = new UpdateFetcher("1.2.3", new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var result = await fetcher.FetchAsync(default);
        Assert.Equal(UpdateFetchFailure.HttpStatus, result.Failure);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Equal(transient, result.IsTransient);
    }

    /// <summary>公開の切り替わりから回復するときも、上限四回の匿名 GET だけで検証する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationRaceRefetchesBothFilesWithOnlyUserAgent(bool recovered)
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var oldManifest = Encoding.UTF8.GetBytes("""
            {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://example.com/notes","files":{"win-arm64":{"url":"https://github.com/app.zip",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
            """);
        var newManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(oldManifest).Replace("1.2.0", "1.3.0"));
        var oldSignature = ManifestSignature.Sign(oldManifest, keys.PrivateKeyPkcs8Base64);
        var newSignature = ManifestSignature.Sign(newManifest, keys.PrivateKeyPkcs8Base64);
        var requests = new List<Uri>();
        using var fetcher = new UpdateFetcher("1.0.0", new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var header = Assert.Single(request.Headers);
            Assert.Equal("User-Agent", header.Key);
            Assert.Equal("Snapwell/1.0.0", Assert.Single(header.Value));
            Assert.Null(request.Content);
            requests.Add(request.RequestUri!);
            var bytes = requests.Count switch
            {
                1 => oldManifest,
                2 => newSignature,
                3 => newManifest,
                4 => recovered ? newSignature : oldSignature,
                _ => throw new InvalidOperationException("Unexpected extra GET."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }));
        var store = new Store();
        var result = await new UpdateChecker(fetcher, store, "1.0.0", keys.PublicKeySpkiBase64,
            UpdateOsArchitecture.Arm64).CheckAsync(default);
        Assert.Equal(new[] { UpdateEndpoints.Manifest, UpdateEndpoints.Signature,
            UpdateEndpoints.Manifest, UpdateEndpoints.Signature }, requests);
        Assert.Equal(recovered ? UpdateCheckOutcome.Available : UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Equal(recovered ? "1.3.0" : null, result.Offer?.Manifest.Version.ToString());
        if (!recovered) { Assert.Equal(new DeviceState(), store.Value); }
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

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState>? TryReload() => Load();
        public void Save(DeviceState value) => Value = value;
    }
}
