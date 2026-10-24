using System.Net;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>リリース情報と署名の取得の境界。</summary>
public interface IUpdateFetcher
{
    /// <summary>通信の失敗と取り消しを結果として返す。</summary>
    Task<UpdateFetchResult> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>取得を完了できなかった原因。</summary>
public enum UpdateFetchFailure { NetworkError, Timeout, HttpStatus, TooLarge, InsecureRedirect }

/// <summary>未検証のバイト列、または開発者向けの失敗診断。</summary>
public sealed record UpdateFetchResult(byte[]? Manifest = null, byte[]? Signature = null,
    UpdateFetchFailure? Failure = null, string? Diagnostic = null)
{
    /// <summary>二つのファイルを取得できたか。</summary>
    public bool IsSuccess => Failure is null && Manifest is not null && Signature is not null;
}

/// <summary>認証と Cookie を使わず HTTPS の GET だけで情報を取得する。</summary>
public sealed class UpdateFetcher : IUpdateFetcher, IDisposable
{
    private readonly HttpClient _client;
    private readonly string _userAgent;

    /// <summary>版と任意のテスト用ハンドラーを指定し、ハンドラーの所有権を受け取る。</summary>
    public UpdateFetcher(string version, HttpMessageHandler? handler = null)
    {
        _userAgent = "Snapwell/" + version;
        _client = new(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
            DefaultProxyCredentials = null,
        })
        { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>ヘッダーと本文を含む二つの取得全体を十五秒で打ち切る。</summary>
    public async Task<UpdateFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var manifest = await GetAsync(UpdateEndpoints.Manifest, UpdateManifestParser.MaximumBytes,
                timeout.Token).ConfigureAwait(false);
            var signature = await GetAsync(UpdateEndpoints.Signature, 1024, timeout.Token).ConfigureAwait(false);
            return new(manifest, signature);
        }
        catch (FetchException exception) { return new(Failure: exception.Failure, Diagnostic: exception.Message); }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? new(Failure: UpdateFetchFailure.NetworkError, Diagnostic: "Update fetch was cancelled.")
                : new(Failure: UpdateFetchFailure.Timeout, Diagnostic: "Update fetch timed out.");
        }
        catch (Exception exception)
        {
            return new(Failure: UpdateFetchFailure.NetworkError, Diagnostic: "Update fetch failed: " + exception.Message);
        }
    }

    private async Task<byte[]> GetAsync(Uri uri, int limit, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects <= 10; redirects++)
        {
            if (!IsSecure(uri))
            {
                throw new FetchException(UpdateFetchFailure.InsecureRedirect, "Redirect requires an anonymous HTTPS URL.");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } final && !IsSecure(final))
            {
                throw new FetchException(UpdateFetchFailure.InsecureRedirect, "Response URL is not anonymous HTTPS.");
            }
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } location)
                {
                    throw new FetchException(UpdateFetchFailure.HttpStatus, "Redirect has no location.");
                }
                uri = location.IsAbsoluteUri ? location : new(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new FetchException(UpdateFetchFailure.HttpStatus, $"HTTP status {(int)response.StatusCode}.");
            }
            if (response.Content.Headers.ContentLength > limit)
            {
                throw new FetchException(UpdateFetchFailure.TooLarge, "Response exceeds its size limit.");
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length,
                    limit + 1 - (int)body.Length)), cancellationToken).ConfigureAwait(false);
                if (count == 0) { return body.ToArray(); }
                if (body.Length + count > limit)
                {
                    throw new FetchException(UpdateFetchFailure.TooLarge, "Response exceeds its size limit.");
                }
                body.Write(buffer, 0, count);
            }
        }
        throw new FetchException(UpdateFetchFailure.NetworkError, "Too many HTTPS redirects.");
    }

    private static bool IsSecure(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Length > 0 && uri.UserInfo.Length == 0;

    /// <summary>通信資源を解放し、進行中の取得を取り消す。</summary>
    public void Dispose() => _client.Dispose();

    private sealed class FetchException(UpdateFetchFailure failure, string message) : Exception(message)
    {
        internal UpdateFetchFailure Failure { get; } = failure;
    }
}
