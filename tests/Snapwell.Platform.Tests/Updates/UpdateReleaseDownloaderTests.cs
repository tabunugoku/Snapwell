using System.Net;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

public sealed class UpdateReleaseDownloaderTests
{
    [Theory]
    [InlineData("https://example.com/package.zip", UpdateDownloadFailure.InsecureRedirect, 1)]
    [InlineData("https://objects.githubusercontent.com/package.zip", UpdateDownloadFailure.HashMismatch, 2)]
    public async Task FactoryUsesReleaseRedirectHostPolicy(string redirect, UpdateDownloadFailure expected, int calls)
    {
        using var handler = new RedirectHandler(new Uri(redirect));
        using var downloader = UpdateDownloader.CreateForRelease("1.0.0", handler);
        var path = Path.Combine(Path.GetTempPath(), "snapwell-redirect-" + Guid.NewGuid().ToString("N"));
        var result = await downloader.DownloadAsync(new(new Uri("https://github.com/package.zip"),
            new string('0', 64), 1), path, null, CancellationToken.None);
        Assert.Equal(expected, result.Failure);
        Assert.Equal(calls, handler.Calls);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("https://example.com/package.zip", 1, UpdateDownloadFailure.HttpStatus)]
    [InlineData("https://release-assets.githubusercontent.com/package.zip", 1, UpdateDownloadFailure.HttpStatus)]
    [InlineData("http://github.com/package.zip", 1, UpdateDownloadFailure.HttpStatus)]
    [InlineData("https://github.com/package.zip", 0, UpdateDownloadFailure.SizeMismatch)]
    [InlineData("https://github.com/package.zip", 314572801, UpdateDownloadFailure.SizeMismatch)]
    public async Task ReleasePolicyRejectsBeforeCommunication(string url, long size, UpdateDownloadFailure failure)
    {
        using var handler = new NoCommunication();
        using var downloader = UpdateDownloader.CreateForRelease("1.0.0", handler);
        var destination = Path.Combine(Path.GetTempPath(), "snapwell-policy-" + Guid.NewGuid().ToString("N"));
        var result = await downloader.DownloadAsync(new UpdateAsset(new Uri(url), new string('0', 64), size),
            destination, null, CancellationToken.None);
        Assert.Equal(failure, result.Failure);
        Assert.False(File.Exists(destination));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class NoCommunication : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Unexpected communication.");
        }
    }

    private sealed class RedirectHandler(Uri redirect) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(Calls == 1
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = redirect } }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        }
    }
}
