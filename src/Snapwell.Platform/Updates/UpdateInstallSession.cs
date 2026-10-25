using System.IO.Compression;
using System.Security.Cryptography;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>更新の適用通知と新版の起動確認を進行役とやり取りする。</summary>
public interface IUpdateSignals
{
    /// <summary>新版の配置が完了し、起動できることを通知する。</summary>
    void SignalApplied();

    /// <summary>新版の起動成功を待ち、時間切れなら false を返す。</summary>
    bool WaitReady(TimeSpan timeout);
}

/// <summary>空の作業フォルダー、取得済みファイル、および更新の検証条件。</summary>
public sealed record UpdateInstallRequest(
    string InstallDir,
    string WorkDir,
    string ManifestPath,
    string SignaturePath,
    string ZipPath,
    string PublicKeySpkiBase64,
    ReleaseVersion CurrentVersion,
    UpdateOsArchitecture Architecture,
    TimeSpan ReadyTimeout);

/// <summary>更新ヘルパーの終了コードとして使う固定の処理結果。</summary>
public enum UpdateInstallResult
{
    Success = 0,
    SignatureInvalid = 10,
    ManifestInvalid = 11,
    NotNewer = 12,
    ArchitectureUnsupported = 13,
    PackageMismatch = 14,
    ZipRejected = 15,
    ApplyFailedRolledBack = 20,
    ApplyFailedRollbackIncomplete = 21,
    ReadyTimeoutRolledBack = 22,
    ReadyTimeoutRollbackIncomplete = 23,
    IoError = 30,
}

/// <summary>作業用コピーを検証・展開・適用し、新版の起動確認まで旧版を保持する。</summary>
public sealed class UpdateInstallSession(IUpdateFileSystem fs, IUpdateSignals signals)
{
    private static readonly uint[] _crc32Table = CreateCrc32Table();

    /// <summary>更新を実行し、例外を処理結果と英語の診断に変換する。</summary>
    public UpdateInstallResult Run(UpdateInstallRequest request, out string diagnostic)
    {
        var ownsWork = false;
        var preserveStaging = false;
        var needsRollback = false;
        var copies = new List<string>();
        string? staging = null;
        string? journal = null;
        var applier = new UpdateApplier(fs);
        diagnostic = "";
        try
        {
            var work = Path.GetFullPath(request.WorkDir);
            var install = Path.GetFullPath(request.InstallDir);
            if (Within(work, install) || Within(install, work)
                || string.Equals(work, install, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Install and work directories must not overlap.");
            }
            if (Directory.EnumerateFileSystemEntries(work).Any())
            {
                throw new IOException("The work directory must be empty.");
            }
            ownsWork = true;
            staging = Path.Combine(work, "staging");
            journal = Path.Combine(work, "journal.jsonl");
            var manifestPath = Path.Combine(work, "manifest.json");
            var signaturePath = Path.Combine(work, "manifest.sig");
            var packagePath = Path.Combine(work, "package.zip");

            // 長さの検査とコピーは同じハンドルで行い、コピー中の書き換えも許さない。
            var sources = new[] { request.ManifestPath, request.SignaturePath, request.ZipPath };
            var destinations = new[] { manifestPath, signaturePath, packagePath };
            long[] limits = [UpdateManifestParser.MaximumBytes, 1024, UpdateDownloadPolicy.MaximumPackageBytes];
            for (var index = 0; index < sources.Length; index++)
            {
                using var source = new FileStream(sources[index], FileMode.Open, FileAccess.Read, FileShare.Read);
                if (source.Length > limits[index])
                {
                    diagnostic = "An input file exceeds its size limit.";
                    return index == 2 ? UpdateInstallResult.PackageMismatch : UpdateInstallResult.ManifestInvalid;
                }
                using var destination = new FileStream(destinations[index], FileMode.CreateNew, FileAccess.Write);
                copies.Add(destinations[index]);
                source.CopyTo(destination);
            }

            // 以後は呼び出し元のファイルを開かず、保護されたコピーだけを使う。
            var manifestBytes = File.ReadAllBytes(manifestPath);
            if (!ManifestSignature.Verify(manifestBytes, File.ReadAllBytes(signaturePath), request.PublicKeySpkiBase64))
            {
                diagnostic = "Manifest signature verification failed.";
                return UpdateInstallResult.SignatureInvalid;
            }
            var parsed = UpdateManifestParser.Parse(manifestBytes);
            if (parsed.Manifest is not { } manifest)
            {
                diagnostic = "Invalid manifest: " + parsed.Error;
                return UpdateInstallResult.ManifestInvalid;
            }
            if (manifest.Version.CompareTo(request.CurrentVersion) <= 0)
            {
                diagnostic = "The release is not newer than the installed version.";
                return UpdateInstallResult.NotNewer;
            }
            var asset = UpdateAssetSelector.Select(request.Architecture, manifest);
            if (asset is null)
            {
                diagnostic = "The release has no package for this OS architecture.";
                return UpdateInstallResult.ArchitectureUnsupported;
            }
            using (var package = File.OpenRead(packagePath))
            {
                if (package.Length != asset.Size
                    || Convert.ToHexStringLower(SHA256.HashData(package)) != asset.Sha256)
                {
                    diagnostic = "Package size or SHA256 does not match the signed manifest.";
                    return UpdateInstallResult.PackageMismatch;
                }
            }
            IReadOnlyList<string> files;
            try
            {
                files = Extract(packagePath, staging);
            }
            catch (InvalidDataException exception)
            {
                diagnostic = "Package extraction rejected: " + exception.Message;
                return UpdateInstallResult.ZipRejected;
            }
            needsRollback = true;
            var result = applier.Apply(install, staging, files, journal);
            if (result.Status != ApplyStatus.Success)
            {
                needsRollback = false;
                preserveStaging = result.Rollback?.Status != RollbackStatus.RolledBack;
                diagnostic = result.Diagnostic + RollbackDiagnostic(result.Rollback);
                return preserveStaging ? UpdateInstallResult.ApplyFailedRollbackIncomplete
                    : UpdateInstallResult.ApplyFailedRolledBack;
            }
            signals.SignalApplied();
            if (signals.WaitReady(request.ReadyTimeout))
            {
                // 起動確認後は旧版を一部削除済みの可能性があるため、巻き戻しに移らない。
                needsRollback = false;
                BestEffort(() => applier.CommitCleanup(install, journal));
                diagnostic = "Update installed and startup confirmed.";
                return UpdateInstallResult.Success;
            }
            var rollback = Rollback(applier, install, journal);
            needsRollback = false;
            preserveStaging = rollback.Status != RollbackStatus.RolledBack;
            diagnostic = "Startup confirmation timed out." + RollbackDiagnostic(rollback);
            return preserveStaging ? UpdateInstallResult.ReadyTimeoutRollbackIncomplete
                : UpdateInstallResult.ReadyTimeoutRolledBack;
        }
        catch (Exception exception)
        {
            diagnostic = "Update installation failed: " + exception.Message;
            if (needsRollback)
            {
                var rollback = Rollback(applier, request.InstallDir, journal!);
                preserveStaging = rollback.Status != RollbackStatus.RolledBack;
                diagnostic += RollbackDiagnostic(rollback);
            }
            return UpdateInstallResult.IoError;
        }
        finally
        {
            if (ownsWork)
            {
                if (!preserveStaging && staging is not null) { RemoveStaging(staging); }
                foreach (var path in copies) { BestEffort(() => fs.DeleteFile(path)); }
            }
        }
    }

    private static IReadOnlyList<string> Extract(string packagePath, string staging)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(entry => new UpdateZipEntry(entry.FullName, entry.Length,
            entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))).ToArray();
        var validation = UpdateZipPolicy.Validate(entries);
        if (!validation.IsValid) { throw new InvalidDataException(validation.Error); }

        Directory.CreateDirectory(staging);
        // Files と同じ順に元のエントリを使い、区切りの正規化後も対応を保つ。
        var fileEntries = archive.Entries.Where((_, index) => !entries[index].IsDirectory).ToArray();
        for (var index = 0; index < validation.Files.Count; index++)
        {
            var destination = Path.GetFullPath(Path.Combine(staging, validation.Files[index]));
            if (!Within(staging, destination))
            {
                throw new InvalidDataException("An extracted path escapes the staging directory.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = fileEntries[index].Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            // ZipArchiveEntry.Open は宣言サイズまでに読み取りを制限する場合がある。
            // サイズだけでなく CRC も照合し、切り詰められた本文を適用しない。
            if (CopyBounded(source, target, fileEntries[index].Length) != fileEntries[index].Crc32)
            {
                throw new InvalidDataException("The extracted file does not match its declared CRC32.");
            }
        }
        return validation.Files;
    }

    private static uint CopyBounded(Stream source, Stream target, long expected)
    {
        var buffer = new byte[65536];
        var remaining = expected;
        var crc = uint.MaxValue;
        while (remaining > 0)
        {
            var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) { throw new InvalidDataException("The stream is shorter than its declared size."); }
            target.Write(buffer, 0, count);
            for (var index = 0; index < count; index++)
            {
                crc = _crc32Table[(crc ^ buffer[index]) & 0xff] ^ (crc >> 8);
            }
            remaining -= count;
        }
        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException("The stream exceeds its declared size.");
        }
        return ~crc;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0xedb88320u);
            }
            table[index] = value;
        }
        return table;
    }

    private void RemoveStaging(string staging)
    {
        BestEffort(() =>
        {
            if (!fs.Exists(staging)) { return; }
            foreach (var relative in fs.EnumerateFiles(staging).ToArray())
            {
                BestEffort(() => fs.DeleteFile(Path.Combine(staging, relative)));
            }
            foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length).ToArray())
            {
                BestEffort(() => fs.DeleteDirectoryIfEmpty(directory));
            }
            fs.DeleteDirectoryIfEmpty(staging);
        });
    }

    private static void BestEffort(Action operation)
    {
        try { operation(); }
        catch (Exception) { /* 後始末の失敗で処理結果を変えない。 */ }
    }

    private static RollbackResult Rollback(UpdateApplier applier, string install, string journal)
    {
        try { return applier.Rollback(install, journal); }
        catch (Exception exception)
        {
            return new(RollbackStatus.RollbackIncomplete,
                [new("journal", journal, "Rollback failed: " + exception.Message)]);
        }
    }

    private static bool Within(string root, string path) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string RollbackDiagnostic(RollbackResult? result) => result is null ? ""
        : " Rollback: " + result.Status + ". " + string.Join("; ", result.Failures.Select(failure => failure.Diagnostic));
}
