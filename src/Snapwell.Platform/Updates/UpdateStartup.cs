using System.Diagnostics;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>更新開始時のファイル操作をテストから切り離す。</summary>
public interface IUpdateStartupFiles
{
    /// <summary>指定された pending を空にして作り直す。</summary>
    void Reset(string pending);
    /// <summary>既存のファイルを上書きせず元のバイト列を書く。</summary>
    void WriteNew(string path, byte[] bytes);
    /// <summary>失敗した更新の pending とダウンロード済みのファイルを削除する。</summary>
    void Remove(string pending);
}

/// <summary>ダウンロードから進行役の起動までに必要な変更しない値。</summary>
public sealed record UpdateStartupRequest(string Pending, string Executable, string Sid, int WaitPid,
    UpdateAsset Asset, byte[] Manifest, byte[] Signature, string PublicKey);

/// <summary>進行役へ渡す前に起きた失敗。</summary>
public enum UpdateStartupFailure { Download, SignatureInvalid, ProcessStart, IoError, Cancelled, AnotherUpdateRunning }

/// <summary>起動の成否と、ダウンロードの詳しい失敗。</summary>
public sealed record UpdateStartupResult(UpdateStartupFailure? Failure = null,
    UpdateDownloadFailure? DownloadFailure = null);

/// <summary>通常権限で取得と署名の先確認を行い、進行役へ引き渡す。</summary>
public sealed class UpdateStartup(IUpdateStartupFiles files,
    Func<UpdateAsset, string, IProgress<long>?, CancellationToken, Task<UpdateDownloadResult>> download,
    Func<ProcessStartInfo, CancellationToken, Task<bool>> start, Func<bool> isUpdateRunning)
{
    // ページを開き直した場合も、古い取り消しの後始末と次の取得を競合させない。
    private static readonly SemaphoreSlim _pendingGate = new(1, 1);

    /// <summary>既存の匿名 HTTPS の配布ポリシーと実ファイル操作を接続する。</summary>
    public static UpdateStartup CreateForRelease(string version, Func<bool> isUpdateRunning,
        Func<ProcessStartInfo, CancellationToken, Task<bool>>? start = null) => new(new StartupFiles(),
        async (asset, path, progress, token) =>
        {
            using var downloader = UpdateDownloader.CreateForRelease(version);
            return await downloader.DownloadAsync(asset, path, progress, token).ConfigureAwait(false);
        }, start ?? ((info, token) =>
        {
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(info);
            return Task.FromResult(process is not null);
        }), isUpdateRunning);

    /// <summary>起動成功だけ pending を保持し、それ以外は取得の残骸を削除する。</summary>
    public async Task<UpdateStartupResult> RunAsync(UpdateStartupRequest request, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var locked = false;
        var ownsPending = false;
        var handedOff = false;
        UpdateStartupResult result = new();
        try
        {
            await _pendingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            locked = true;
            cancellationToken.ThrowIfCancellationRequested();
            // 進行役への引き渡し後も入力を保護し、拒否時は後始末の削除も行わない。
            if (isUpdateRunning()) { return new(UpdateStartupFailure.AnotherUpdateRunning); }
            ownsPending = true;
            files.Reset(request.Pending);
            var manifest = Path.Combine(request.Pending, "manifest.json");
            var signature = Path.Combine(request.Pending, "manifest.sig");
            var zip = Path.Combine(request.Pending, "package.zip");
            files.WriteNew(manifest, request.Manifest);
            files.WriteNew(signature, request.Signature);
            var downloaded = await download(request.Asset, zip, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!downloaded.IsSuccess) { result = new(UpdateStartupFailure.Download, downloaded.Failure); }
            else if (!ManifestSignature.Verify(request.Manifest, request.Signature, request.PublicKey))
            {
                result = new(UpdateStartupFailure.SignatureInvalid);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    handedOff = await start(UpdateApplyArguments.Create(request.Executable, request.Sid, manifest,
                        signature, zip, request.WaitPid), cancellationToken).ConfigureAwait(false);
                    result = handedOff ? new() : new(UpdateStartupFailure.ProcessStart);
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                    or InvalidOperationException or System.Security.SecurityException or IOException
                    or UnauthorizedAccessException)
                { result = new(UpdateStartupFailure.ProcessStart); }
            }
        }
        catch (OperationCanceledException) { result = new(UpdateStartupFailure.Cancelled); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        { result = new(UpdateStartupFailure.IoError); }
        finally
        {
            try
            {
                if (ownsPending && !handedOff) { files.Remove(request.Pending); }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
            { result = new(UpdateStartupFailure.IoError); }
            finally { if (locked) { _pendingGate.Release(); } }
        }
        return result;
    }

    private sealed class StartupFiles : IUpdateStartupFiles
    {
        /// <summary>専用の取得先を空にして作り直す。</summary>
        public void Reset(string pending)
        {
            Remove(pending);
            Directory.CreateDirectory(pending);
        }

        /// <summary>CreateNew で上書きを拒み、署名対象のバイト列をそのまま保存する。</summary>
        public void WriteNew(string path, byte[] bytes)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes);
        }

        /// <summary>LocalAppData の既知の pending だけを再帰削除する。</summary>
        public void Remove(string pending)
        {
            // 呼び出し元の専用ディレクトリ以外を再帰削除しない。
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Snapwell", "update", "pending");
            if (!string.Equals(Path.GetFullPath(pending), Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase)) { throw new IOException("Unexpected pending directory."); }
            if (Directory.Exists(pending)) { Directory.Delete(pending, recursive: true); }
        }
    }
}
