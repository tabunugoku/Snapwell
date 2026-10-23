using System.Diagnostics;
using System.Net.Http;
using System.Security.Authentication;

try
{
    var uri = ProbeReport.ParseUrl(args);
    ProbeReport.Before();
    var clock = Stopwatch.StartNew();
    var (status, bytes) = await Get(uri);
    clock.Stop();
    ProbeReport.After("HttpClientProbe", status, bytes, clock.Elapsed.TotalMilliseconds);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error={exception.GetType().Name} message={exception.Message}");
    return 1;
}

static async Task<(uint Status, int Bytes)> Get(Uri uri)
{
    using var handler = new HttpClientHandler
    {
        AllowAutoRedirect = true,
        SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    };
    using var client = new HttpClient(handler);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Snapwell/0.0.0");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
    var buffer = new byte[ProbeReport.MaxBytes];
    var bytes = 0;
    // 大きい応答でも本文を保持する量と転送量を 64 KiB で打ち切る。
    while (bytes < buffer.Length)
    {
        var read = await body.ReadAsync(buffer.AsMemory(bytes), timeout.Token);
        if (read == 0)
        {
            break;
        }
        bytes += read;
    }
    return ((uint)response.StatusCode, bytes);
}
