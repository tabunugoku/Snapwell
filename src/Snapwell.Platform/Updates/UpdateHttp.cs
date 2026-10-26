using System.Net;

namespace Snapwell.Platform.Updates;

internal static class UpdateHttp
{
    internal static async Task GetAsync(HttpClient client, string userAgent, Uri uri, long limit,
        Func<Uri, bool> allowedHost, Stream destination, CancellationToken cancellationToken,
        bool requireExactLength = false, TimeSpan? idleTimeout = null, IProgress<long>? progress = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = idleTimeout ?? Timeout.InfiniteTimeSpan;
        for (var redirects = 0; redirects <= 10; redirects++)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0
                || uri.UserInfo.Length != 0 || !allowedHost(uri))
            {
                throw new UpdateHttpException(UpdateFetchFailure.InsecureRedirect,
                    "Request requires an allowed anonymous HTTPS URL.");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(userAgent);
            timeout.CancelAfter(deadline);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } location)
                {
                    throw new UpdateHttpException(UpdateFetchFailure.HttpStatus, "Redirect has no location.");
                }
                uri = location.IsAbsoluteUri ? location : new(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateHttpException(UpdateFetchFailure.HttpStatus,
                    $"HTTP status {(int)response.StatusCode}.", response.StatusCode);
            }
            if (response.Content.Headers.ContentLength is { } length
                && (length > limit || (requireExactLength && length != limit)))
            {
                throw new UpdateHttpException(UpdateFetchFailure.TooLarge, requireExactLength
                    ? "Content-Length differs from asset size." : "Response exceeds its size limit.");
            }
            timeout.CancelAfter(deadline);
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            var buffer = new byte[(int)Math.Clamp(limit, 1, 65536)];
            long written = 0;
            long reported = 0;
            while (true)
            {
                // 上限に達した後は一バイトだけ調べ、超過分をファイルに書かない。
                var capacity = (int)Math.Min(Math.Min(buffer.Length, limit - written), 65536 - (written - reported));
                timeout.CancelAfter(deadline);
                int count;
                try
                {
                    count = await stream.ReadAsync(buffer.AsMemory(0, capacity == 0 ? 1 : capacity),
                        timeout.Token).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    throw new HttpRequestException("Response body could not be read.", exception);
                }
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (count == 0)
                {
                    if (requireExactLength && written != limit)
                    {
                        throw new UpdateHttpException(UpdateFetchFailure.TooLarge,
                            "Response length differs from asset size.");
                    }
                    if (reported != written || written == 0) { progress?.Report(written); }
                    return;
                }
                if (capacity == 0)
                {
                    throw new UpdateHttpException(UpdateFetchFailure.TooLarge, "Response exceeds its size limit.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                written += count;
                if (written - reported >= 65536)
                {
                    progress?.Report(written);
                    reported = written;
                }
            }
        }
        throw new UpdateHttpException(UpdateFetchFailure.NetworkError, "Too many HTTPS redirects.");
    }
}

internal sealed class UpdateHttpException(UpdateFetchFailure failure, string message,
    HttpStatusCode? statusCode = null) : Exception(message)
{
    internal UpdateFetchFailure Failure { get; } = failure;
    internal HttpStatusCode? StatusCode { get; } = statusCode;
}
