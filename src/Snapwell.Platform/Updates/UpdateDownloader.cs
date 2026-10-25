using System.Security.Cryptography;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>配布 zip の取得を完了できなかった原因。</summary>
public enum UpdateDownloadFailure
{
    NetworkError, Timeout, HttpStatus, SizeMismatch, HashMismatch, InsecureRedirect, Cancelled, IoError,
}

/// <summary>取得の成否と開発者向けの失敗診断。</summary>
public sealed record UpdateDownloadResult(UpdateDownloadFailure? Failure = null, string? Diagnostic = null)
{
    /// <summary>検証済みのファイルを保存できたか。</summary>
    public bool IsSuccess => Failure is null;
}

/// <summary>匿名 HTTPS で配布 zip を取得し、サイズと SHA256 を検証して保存する。</summary>
public sealed class UpdateDownloader : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _userAgent;
    private readonly Func<Uri, bool> _allowedHost;

    /// <summary>版とホストの許可条件を指定し、任意のハンドラーの所有権を受け取る。</summary>
    public UpdateDownloader(string version, Func<Uri, bool> allowedHost, HttpMessageHandler? handler = null)
    {
        _userAgent = "Snapwell/" + version.Split('+')[0];
        _allowedHost = allowedHost;
        _client = new(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
            DefaultProxyCredentials = null,
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>一時ファイルに取得し、検証後に上書きせず確定する。取り消しと例外も結果として返す。</summary>
    public async Task<UpdateDownloadResult> DownloadAsync(UpdateAsset asset, string destinationPath,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var partPath = destinationPath + ".part";
        var ownsPart = false;
        UpdateDownloadResult result = new();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
            {
                return new(UpdateDownloadFailure.IoError, "Destination file already exists.");
            }
            if (asset.Size < 0)
            {
                return new(UpdateDownloadFailure.SizeMismatch, "Asset size must not be negative.");
            }
            await using (var file = new FileStream(partPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                // CreateNew が失敗したときは、別の取得が所有する一時ファイルを消さない。
                ownsPart = true;
                await UpdateHttp.GetAsync(_client, _userAgent, asset.Url, asset.Size, _allowedHost, file,
                    cancellationToken, requireExactLength: true, idleTimeout: TimeSpan.FromSeconds(30),
                    progress: progress).ConfigureAwait(false);
                file.Position = 0;
                var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexStringLower(hash), asset.Sha256, StringComparison.Ordinal))
                {
                    result = new(UpdateDownloadFailure.HashMismatch, "Downloaded SHA256 differs from asset SHA256.");
                }
            }
            if (result.IsSuccess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partPath, destinationPath, overwrite: false);
                ownsPart = false;
            }
        }
        catch (UpdateHttpException exception)
        {
            var failure = exception.Failure switch
            {
                UpdateFetchFailure.HttpStatus => UpdateDownloadFailure.HttpStatus,
                UpdateFetchFailure.TooLarge => UpdateDownloadFailure.SizeMismatch,
                UpdateFetchFailure.InsecureRedirect => UpdateDownloadFailure.InsecureRedirect,
                _ => UpdateDownloadFailure.NetworkError,
            };
            result = new(failure, exception.Message);
        }
        catch (OperationCanceledException)
        {
            result = cancellationToken.IsCancellationRequested
                ? new(UpdateDownloadFailure.Cancelled, "Update download was cancelled.")
                : new(UpdateDownloadFailure.Timeout, "Update download was idle for 30 seconds.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            result = new(UpdateDownloadFailure.IoError, "Update file could not be saved: " + exception.Message);
        }
        catch (Exception exception)
        {
            result = new(UpdateDownloadFailure.NetworkError, "Update download failed: " + exception.Message);
        }
        finally
        {
            if (ownsPart)
            {
                try { File.Delete(partPath); }
                catch (Exception exception)
                {
                    result = new(UpdateDownloadFailure.IoError,
                        "Partial file could not be deleted: " + exception.Message);
                }
            }
        }
        return result;
    }

    /// <summary>通信資源を解放し、進行中の取得を取り消す。</summary>
    public void Dispose() => _client.Dispose();
}
