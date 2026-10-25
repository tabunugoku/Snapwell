using System.Diagnostics;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>実ファイルや通信を使わず、更新開始の順序と後始末を検証する。</summary>
public sealed class UpdateStartupTests
{
    /// <summary>署名した元バイト列と取得した zip を進行役へ渡し、成功時だけ保持する。</summary>
    [Fact]
    public async Task SuccessPreservesPendingAndStartsWithoutElevation()
    {
        var fixture = new Fixture();
        var result = await fixture.Run();
        Assert.Null(result.Failure);
        Assert.Equal(new[] { "reset", "manifest.json", "manifest.sig", "download", "start" }, fixture.Calls);
        Assert.Equal(fixture.Request.Manifest, fixture.Files["manifest.json"]);
        Assert.Equal(fixture.Request.Signature, fixture.Files["manifest.sig"]);
        Assert.False(fixture.Started!.UseShellExecute);
        Assert.Equal("--apply-update", fixture.Started.ArgumentList[0]);
        Assert.Contains(Path.Combine(fixture.Request.Pending, "package.zip"), fixture.Started.ArgumentList);
    }

    /// <summary>署名の不一致は進行役を起動せず、取得済みの zip も削除する。</summary>
    [Fact]
    public async Task BadSignatureDoesNotLaunch()
    {
        var fixture = new Fixture();
        fixture.Request = fixture.Request with { Signature = [0] };
        Assert.Equal(UpdateStartupFailure.SignatureInvalid, (await fixture.Run()).Failure);
        Assert.Null(fixture.Started);
        Assert.Empty(fixture.Files);
    }

    /// <summary>ダウンロードの失敗種別を落とさず返し、残ったファイルを削除する。</summary>
    [Theory]
    [InlineData(UpdateDownloadFailure.NetworkError)]
    [InlineData(UpdateDownloadFailure.Timeout)]
    [InlineData(UpdateDownloadFailure.HttpStatus)]
    [InlineData(UpdateDownloadFailure.SizeMismatch)]
    [InlineData(UpdateDownloadFailure.HashMismatch)]
    [InlineData(UpdateDownloadFailure.InsecureRedirect)]
    [InlineData(UpdateDownloadFailure.Cancelled)]
    [InlineData(UpdateDownloadFailure.IoError)]
    public async Task DownloadFailurePreservesKind(UpdateDownloadFailure failure)
    {
        var fixture = new Fixture { DownloadResult = new(failure) };
        var result = await fixture.Run();
        Assert.Equal(UpdateStartupFailure.Download, result.Failure);
        Assert.Equal(failure, result.DownloadFailure);
        Assert.Null(fixture.Started);
        Assert.Empty(fixture.Files);
    }

    /// <summary>取得中と取得直後の取り消しも zip を残さず進行役を起動しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCleansPending(bool throwDuringDownload)
    {
        using var stop = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.OnDownload = () =>
        {
            stop.Cancel();
            if (throwDuringDownload) { stop.Token.ThrowIfCancellationRequested(); }
        };
        Assert.Equal(UpdateStartupFailure.Cancelled, (await fixture.Run(stop.Token)).Failure);
        Assert.Empty(fixture.Files);
        Assert.Null(fixture.Started);
    }

    /// <summary>起動が失敗した場合も pending を残さない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStartCleansPending(bool throws)
    {
        var fixture = new Fixture { StartSucceeds = false, ThrowOnStart = throws };
        Assert.Equal(UpdateStartupFailure.ProcessStart, (await fixture.Run()).Failure);
        Assert.Empty(fixture.Files);
    }

    /// <summary>保存できなければダウンロードせず、作りかけのファイルも削除する。</summary>
    [Fact]
    public async Task WriteFailureCleansPending()
    {
        var fixture = new Fixture { ThrowOnWrite = true };
        Assert.Equal(UpdateStartupFailure.IoError, (await fixture.Run()).Failure);
        Assert.DoesNotContain("download", fixture.Calls);
        Assert.Empty(fixture.Files);
    }

    /// <summary>前のページの取得が終わるまで待ち、待機中の取り消しは他の pending を消さない。</summary>
    [Fact]
    public async Task WaitingCancellationDoesNotDeleteActivePending()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Fixture { DownloadWait = release.Task, OnDownload = () => entered.SetResult() };
        var running = first.Run();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var stop = new CancellationTokenSource();
            var second = new Fixture();
            var waiting = second.Run(stop.Token);
            Assert.False(waiting.IsCompleted);
            stop.Cancel();
            Assert.Equal(UpdateStartupFailure.Cancelled, (await waiting).Failure);
            Assert.Empty(second.Calls);
            Assert.Equal(new[] { "manifest.json", "manifest.sig", "package.zip" }, first.Files.Keys);
        }
        finally { release.TrySetResult(); }
        Assert.Null((await running).Failure);
    }

    private sealed class Fixture : IUpdateStartupFiles
    {
        internal readonly List<string> Calls = [];
        internal readonly Dictionary<string, byte[]> Files = [];
        internal UpdateStartupRequest Request;
        internal UpdateDownloadResult DownloadResult = new();
        internal ProcessStartInfo? Started;
        internal bool StartSucceeds = true;
        internal bool ThrowOnStart;
        internal bool ThrowOnWrite;
        internal Action? OnDownload;
        internal Task? DownloadWait;

        internal Fixture()
        {
            var keys = ManifestSignature.GenerateKeyPair();
            byte[] bytes = "signed original bytes"u8.ToArray();
            Request = new(@"C:\update folder\pending", @"C:\app folder\Snapwell.exe", "S-1-5-21-1", 123,
                new(new("https://github.com/package.zip"), new('a', 64), 123), bytes,
                ManifestSignature.Sign(bytes, keys.PrivateKeyPkcs8Base64), keys.PublicKeySpkiBase64);
        }

        internal Task<UpdateStartupResult> Run(CancellationToken token = default) =>
            new UpdateStartup(this, Download, Start).RunAsync(Request, null, token);

        private async Task<UpdateDownloadResult> Download(UpdateAsset asset, string path, IProgress<long>? progress,
            CancellationToken token)
        {
            Calls.Add("download");
            Files.Add(Path.GetFileName(path), [1]);
            OnDownload?.Invoke();
            if (DownloadWait is { } wait) { await wait.WaitAsync(token); }
            return DownloadResult;
        }

        private Task<bool> Start(ProcessStartInfo info, CancellationToken token)
        {
            Calls.Add("start");
            if (ThrowOnStart) { throw new System.ComponentModel.Win32Exception(); }
            Started = info;
            return Task.FromResult(StartSucceeds);
        }

        /// <summary>取得先の初期化を記録する。</summary>
        public void Reset(string pending) { Calls.Add("reset"); Files.Clear(); }

        /// <summary>上書きを許さない保存を模倣する。</summary>
        public void WriteNew(string path, byte[] bytes)
        {
            var name = Path.GetFileName(path);
            Calls.Add(name);
            Files.Add(name, bytes);
            if (ThrowOnWrite) { throw new IOException(); }
        }
        /// <summary>ファイルの後始末を記録する。</summary>
        public void Remove(string pending) { Calls.Add("remove"); Files.Clear(); }
    }
}
