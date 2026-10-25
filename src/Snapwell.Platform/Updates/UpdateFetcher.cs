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
        // User-Agent には版だけを入れる。ビルドのメタデータ (+ 以降のコミットの SHA) は含めない。
        _userAgent = "Snapwell/" + version.Split('+')[0];
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
        catch (UpdateHttpException exception) { return new(Failure: exception.Failure, Diagnostic: exception.Message); }
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
        using var body = new MemoryStream();
        await UpdateHttp.GetAsync(_client, _userAgent, uri, limit, static _ => true, body,
            cancellationToken).ConfigureAwait(false);
        return body.ToArray();
    }

    /// <summary>通信資源を解放し、進行中の取得を取り消す。</summary>
    public void Dispose() => _client.Dispose();
}
