using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>署名済みの実ファイルと偽の信号で、更新セッションの検証と復旧を確認する。</summary>
public sealed class UpdateInstallSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Snapwell session " + Guid.NewGuid());
    private readonly (string PrivateKeyPkcs8Base64, string PublicKeySpkiBase64) _key =
        ManifestSignature.GenerateKeyPair();
    private readonly Signals _signals = new();
    private readonly Stopper _stopper = new();
    private readonly UpdateFileSystem _fs = new();

    private string Install => Path.Combine(_root, "配置 先");
    private string Work => Path.Combine(_root, "作業 用");
    private string Manifest => Path.Combine(_root, "latest.json");
    private string Signature => Path.Combine(_root, "latest.json.sig");
    private string Package => Path.Combine(_root, "release.zip");
    private string Staging => Path.Combine(Work, "staging");
    private string Journal => Path.Combine(Work, "journal.jsonl");

    /// <summary>日本語と空白を含む専用フォルダーに旧版と署名済みの新版を用意する。</summary>
    public UpdateInstallSessionTests()
    {
        Directory.CreateDirectory(Install);
        Directory.CreateDirectory(Work);
        WriteInstalled("Snapwell.exe", "old executable");
        WriteInstalled("obsolete/data.txt", "old data");
        CreateZip(("Snapwell.exe", "new executable"), ("日本語/sub/data.txt", "new data"), ("empty/", ""));
        SignManifest();
    }

    /// <summary>確認中は旧版を保持し、成功後に不要な旧ファイルと作業ファイルを消す。</summary>
    [Theory]
    [InlineData(UpdateOsArchitecture.X64)]
    [InlineData(UpdateOsArchitecture.Arm64)]
    public void InstallsAndCleansAfterReady(UpdateOsArchitecture architecture)
    {
        SignManifest(rid: architecture == UpdateOsArchitecture.X64 ? "win-x64" : "win-arm64");
        _signals.BeforeWait = () =>
        {
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
            Assert.Equal("old executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe.old")));
            Assert.True(File.Exists(Journal));
            Assert.True(Directory.Exists(Staging));
        };
        AssertResult(UpdateInstallResult.Success, Request() with { Architecture = architecture });
        Assert.Equal(new[] { "日本語/sub/data.txt", "Snapwell.exe" }.Order(), Snapshot(Install).Keys.Order());
        Assert.Equal("new data", File.ReadAllText(Path.Combine(Install, "日本語/sub/data.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Work));
        Assert.Equal(new[] { "applied", "wait" }, _signals.Calls);
        Assert.Equal(TimeSpan.Zero, _signals.Timeout);
    }

    /// <summary>期限がゼロで確認に失敗しても、名前と内容を旧版に戻す。</summary>
    [Fact]
    public void ReadyTimeoutRestoresOriginalFiles()
    {
        var before = Snapshot(Install);
        _signals.Ready = false;
        _stopper.BeforeStop = () =>
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
        AssertResult(UpdateInstallResult.ReadyTimeoutRolledBack);
        Assert.Equal(Install, _stopper.InstallDir);
        Assert.Equal(TimeSpan.FromSeconds(15), _stopper.Timeout);
        AssertSnapshot(before, Install);
        Assert.False(Directory.Exists(Path.Combine(Install, "日本語")));
        Assert.Equal(new[] { "applied", "wait" }, _signals.Calls);
        AssertCleaned();
        Assert.True(File.Exists(Journal));
    }

    /// <summary>新版が止まらなければ配置を戻さず、復旧用の資料を保持する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadyTimeoutWithRunningResidentPreservesNewFiles(bool throws)
    {
        _signals.Ready = false;
        _stopper.Stopped = false;
        _stopper.BeforeStop = () =>
        {
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
            if (throws) { throw new IOException("Injected stop failure."); }
        };
        var diagnostic = AssertResult(UpdateInstallResult.ReadyTimeoutResidentRunning);
        Assert.Equal(24, (int)UpdateInstallResult.ReadyTimeoutResidentRunning);
        Assert.Contains("Rollback was skipped", diagnostic);
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe.old")));
        Assert.True(File.Exists(Journal));
        Assert.True(File.Exists(Path.Combine(Staging, "Snapwell.exe")));
    }

    /// <summary>前回成功時の退避だけを、今回の差し替え前に削除する。</summary>
    [Fact]
    public void RemovesPreviousSuccessfulBackupsBeforeApply()
    {
        WriteInstalled("Snapwell.exe.old", "previous executable");
        WriteInstalled("obsolete/data.txt.OLD", "previous data");
        _signals.OnApplied = () =>
            Assert.Equal("old executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe.old")));
        AssertResult(UpdateInstallResult.Success);
        Assert.DoesNotContain(Snapshot(Install).Keys, name => name.EndsWith(".old", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>現行ファイルが無い退避と未解決の前回更新には触れず、事前検査で拒否する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsBackupsNeededForPreviousRecovery(bool unresolved)
    {
        WriteInstalled("Snapwell.exe.old", "previous executable");
        if (!unresolved) { File.Delete(Path.Combine(Install, "Snapwell.exe")); }
        var before = Snapshot(Install);
        var diagnostic = AssertResult(UpdateInstallResult.IoError,
            Request() with { PreviousUpdateUnresolved = unresolved });
        Assert.Contains("Previous .old files", diagnostic);
        AssertSnapshot(before, Install);
        Assert.Empty(_signals.Calls);
        AssertCleaned();
    }

    /// <summary>古い退避の削除失敗も、事前検査の診断で返す。</summary>
    [Fact]
    public void PreviousBackupDeleteFailureReturnsIoError()
    {
        WriteInstalled("Snapwell.exe.old", "previous executable");
        var before = Snapshot(Install);
        var diagnostic = AssertResult(UpdateInstallResult.IoError, fs: new Faults(_fs) { FailOldDelete = true });
        Assert.Contains("Previous .old files", diagnostic);
        AssertSnapshot(before, Install);
    }

    /// <summary>成功後に消せない退避は残し、次回の更新開始時に再試行することを診断に記す。</summary>
    [Fact]
    public void LockedBackupAfterReadyStillSucceedsWithCleanupDiagnostic()
    {
        var diagnostic = AssertResult(UpdateInstallResult.Success, fs: new Faults(_fs) { FailOldDelete = true });
        Assert.Contains("at the start of the next update", diagnostic);
        Assert.True(File.Exists(Path.Combine(Install, "Snapwell.exe.old")));
        Assert.False(Directory.Exists(Staging));
    }

    /// <summary>署名の破損、別の鍵、本文の改ざんでは適用を始めない。</summary>
    [Theory]
    [InlineData("signature")]
    [InlineData("key")]
    [InlineData("manifest")]
    public void RejectsInvalidSignature(string kind)
    {
        var request = Request();
        switch (kind)
        {
            case "signature": File.WriteAllText(Signature, "invalid"); break;
            case "key":
                request = request with
                { PublicKeySpkiBase64 = ManifestSignature.GenerateKeyPair().PublicKeySpkiBase64 }; break;
            case "manifest": File.AppendAllText(Manifest, " "); break;
        }
        AssertRejected(UpdateInstallResult.SignatureInvalid, request);
    }

    /// <summary>署名が正しくても構文や必須項目が不正なら拒否する。</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    public void RejectsInvalidSignedManifest(string manifest)
    {
        WriteSignedManifest(manifest);
        AssertRejected(UpdateInstallResult.ManifestInvalid);
    }

    /// <summary>署名後にパッケージの内容または長さが変われば展開しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsPackageMismatch(bool changeSize)
    {
        var bytes = File.ReadAllBytes(Package);
        bytes[0] ^= 1;
        File.WriteAllBytes(Package, changeSize ? bytes[..^1] : bytes);
        AssertRejected(UpdateInstallResult.PackageMismatch);
    }

    /// <summary>SemVer の優先順位で同版と旧版を拒否する。</summary>
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+another")]
    [InlineData("0.9.9")]
    [InlineData("1.0.0-rc.1")]
    public void RejectsNonNewerVersion(string version)
    {
        SignManifest(version);
        AssertRejected(UpdateInstallResult.NotNewer);
    }

    /// <summary>OS に対応する RID が無ければ別の RID のパッケージを使わない。</summary>
    [Theory]
    [InlineData(UpdateOsArchitecture.Arm64)]
    [InlineData(UpdateOsArchitecture.Other)]
    public void RejectsUnsupportedArchitecture(UpdateOsArchitecture architecture) =>
        AssertRejected(UpdateInstallResult.ArchitectureUnsupported, Request() with { Architecture = architecture });

    /// <summary>パス逸脱、欠落、重複をポリシーで拒否し、配置先を変更しない。</summary>
    [Theory]
    [InlineData("../evil")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void RejectsUnsafeZip(string kind)
    {
        if (kind == "missing") { CreateZip(("data.txt", "data")); }
        else if (kind == "duplicate") { CreateZip(("Snapwell.exe", "one"), ("SNAPWELL.EXE", "two")); }
        else { CreateZip(("Snapwell.exe", "new"), (kind, "evil")); }
        SignManifest();
        AssertRejected(UpdateInstallResult.ZipRejected);
        Assert.False(File.Exists(Path.Combine(Work, "evil")));
    }

    /// <summary>中央ディレクトリの宣言より長い本文も短い本文も展開時に拒否する。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void RejectsFalseUncompressedSize(int declaredSize)
    {
        CreateZip(("Snapwell.exe", "payload larger than one byte"));
        var bytes = File.ReadAllBytes(Package);
        var central = FindCentralHeader(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24, 4), (uint)declaredSize);
        File.WriteAllBytes(Package, bytes);
        SignManifest();
        AssertRejected(UpdateInstallResult.ZipRejected);
    }

    /// <summary>中央ディレクトリの途中で切れた zip は検証済みハッシュでも拒否する。</summary>
    [Fact]
    public void RejectsTruncatedZip()
    {
        var bytes = File.ReadAllBytes(Package);
        File.WriteAllBytes(Package, bytes[..(bytes.Length / 2)]);
        SignManifest();
        AssertRejected(UpdateInstallResult.ZipRejected);
    }

    /// <summary>入力の上限を超えるファイルをコピーせずに拒否する。</summary>
    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("package")]
    public void RejectsOversizedInput(string kind)
    {
        var path = kind switch { "manifest" => Manifest, "signature" => Signature, _ => Package };
        var limit = kind switch
        {
            "manifest" => UpdateManifestParser.MaximumBytes,
            "signature" => 1024,
            _ => UpdateDownloadPolicy.MaximumPackageBytes,
        };
        using (var stream = File.OpenWrite(path)) { stream.SetLength(limit + 1); }
        AssertRejected(kind == "package" ? UpdateInstallResult.PackageMismatch : UpdateInstallResult.ManifestInvalid);
        Assert.False(File.Exists(Path.Combine(Work, kind switch
        {
            "manifest" => "manifest.json",
            "signature" => "manifest.sig",
            _ => "package.zip",
        })));
    }

    /// <summary>作業フォルダーに空の staging やファイルがあれば、一切変更しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsNonemptyWorkWithoutChanges(bool file)
    {
        if (file) { File.WriteAllText(Path.Combine(Work, "package.zip"), "keep"); }
        else { Directory.CreateDirectory(Staging); }
        var before = Snapshot(_root);
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        AssertResult(UpdateInstallResult.IoError);
        AssertSnapshot(before, _root);
        Assert.Equal(directories, Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).Order());
        Assert.Empty(_signals.Calls);
    }

    /// <summary>入力が無いときは例外を漏らさず、途中までコピーしたファイルも消す。</summary>
    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("package")]
    public void MissingInputReturnsIoError(string kind)
    {
        File.Delete(kind switch { "manifest" => Manifest, "signature" => Signature, _ => Package });
        AssertRejected(UpdateInstallResult.IoError);
    }

    /// <summary>作業フォルダーが未作成なら作成せずに拒否する。</summary>
    [Fact]
    public void RejectsMissingWork()
    {
        Directory.Delete(Work);
        AssertResult(UpdateInstallResult.IoError);
        Assert.False(Directory.Exists(Work));
        Assert.Empty(_signals.Calls);
    }

    /// <summary>コピー元を適用時に改ざんし、通知時に削除しても作業用コピーで完了する。</summary>
    [Fact]
    public void UsesOnlyPrivateCopiesAfterCopying()
    {
        var fs = new Faults(_fs)
        {
            BeforeEnumerate = root =>
            {
                if (root != Install) { return; }
                File.WriteAllText(Manifest, "tampered");
                File.WriteAllText(Signature, "tampered");
                File.WriteAllText(Package, "tampered");
            },
        };
        _signals.OnApplied = () =>
        {
            File.Delete(Manifest);
            File.Delete(Signature);
            File.Delete(Package);
        };
        AssertResult(UpdateInstallResult.Success, fs: fs);
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Work));
    }

    /// <summary>配置へのコピーが途中で失敗しても元の名前と内容に戻す。</summary>
    [Fact]
    public void ApplyFailureRollsBack()
    {
        var before = Snapshot(Install);
        var fs = new Faults(_fs) { FailCopy = true };
        var diagnostic = AssertResult(UpdateInstallResult.ApplyFailedRolledBack, fs: fs);
        Assert.Contains("Injected copy failure", diagnostic);
        AssertSnapshot(before, Install);
        Assert.Empty(_signals.Calls);
        AssertCleaned();
    }

    /// <summary>適用失敗と起動期限切れの両方で、不完全な巻き戻しの診断資料を残す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteRollbackPreservesJournalAndStaging(bool applyFailure)
    {
        var fs = new Faults(_fs) { FailCopy = applyFailure, FailRestore = true };
        _signals.Ready = false;
        var diagnostic = AssertResult(applyFailure ? UpdateInstallResult.ApplyFailedRollbackIncomplete
            : UpdateInstallResult.ReadyTimeoutRollbackIncomplete, fs: fs);
        Assert.Contains("Injected restore failure", diagnostic);
        Assert.True(File.Exists(Journal));
        Assert.True(File.Exists(Path.Combine(Staging, "Snapwell.exe")));
        Assert.False(File.Exists(Path.Combine(Work, "package.zip")));
    }

    /// <summary>後始末だけの失敗で成功の結果を変えない。</summary>
    [Fact]
    public void CleanupFailureDoesNotChangeResult()
    {
        AssertResult(UpdateInstallResult.Success, fs: new Faults(_fs) { FailPackageDelete = true });
        Assert.True(File.Exists(Path.Combine(Work, "package.zip")));
        Assert.False(Directory.Exists(Staging));
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(Install, "Snapwell.exe")));
    }

    /// <summary>通知や待機の例外も外に漏らさず、適用済みなら巻き戻す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalFailureReturnsIoErrorAndRestores(bool duringWait)
    {
        var before = Snapshot(Install);
        if (duringWait) { _signals.BeforeWait = () => throw new IOException("Signal failed."); }
        else { _signals.OnApplied = () => throw new InvalidOperationException("Signal failed."); }
        AssertResult(UpdateInstallResult.IoError);
        AssertSnapshot(before, Install);
        AssertCleaned();
    }

    /// <summary>巻き戻し自体の予期しない例外も漏らさず、復旧に必要な資料を残す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedRollbackFailurePreservesRecoveryFiles(bool timeout)
    {
        if (timeout) { _signals.Ready = false; }
        else { _signals.OnApplied = () => throw new IOException("Signal failed."); }
        var diagnostic = AssertResult(timeout ? UpdateInstallResult.ReadyTimeoutRollbackIncomplete
            : UpdateInstallResult.IoError,
            fs: new Faults(_fs) { FailJournalRead = true });
        Assert.Contains("Injected journal failure", diagnostic);
        Assert.True(File.Exists(Journal));
        Assert.True(File.Exists(Path.Combine(Staging, "Snapwell.exe")));
        Assert.False(File.Exists(Path.Combine(Work, "package.zip")));
    }

    /// <summary>信号の例外は壊れた zip と区別し、旧版を復元する。</summary>
    [Fact]
    public void SignalInvalidDataReturnsIoError()
    {
        var before = Snapshot(Install);
        _signals.BeforeWait = () => throw new InvalidDataException("Invalid signal data.");
        AssertResult(UpdateInstallResult.IoError);
        AssertSnapshot(before, Install);
        AssertCleaned();
    }

    /// <summary>適用途中の予期しない例外でもジャーナルから旧版を戻す。</summary>
    [Fact]
    public void UnexpectedApplyFailureRestoresOriginalFiles()
    {
        var before = Snapshot(Install);
        AssertResult(UpdateInstallResult.IoError, fs: new Faults(_fs) { UnexpectedCopyFailure = true });
        AssertSnapshot(before, Install);
        Assert.Empty(_signals.Calls);
        AssertCleaned();
    }

    private UpdateInstallRequest Request()
    {
        Assert.True(ReleaseVersion.TryParse("1.0.0", out var version));
        return new(Install, Work, Manifest, Signature, Package, _key.PublicKeySpkiBase64, version,
            UpdateOsArchitecture.X64, TimeSpan.Zero, false);
    }

    private string AssertResult(UpdateInstallResult expected, UpdateInstallRequest? request = null,
        IUpdateFileSystem? fs = null)
    {
        var result = new UpdateInstallSession(fs ?? _fs, _signals, _stopper)
            .Run(request ?? Request(), out var diagnostic);
        Assert.True(result == expected, $"Expected {expected}, got {result}: {diagnostic}");
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
        return diagnostic;
    }

    private void AssertRejected(UpdateInstallResult expected, UpdateInstallRequest? request = null)
    {
        var before = Snapshot(Install);
        AssertResult(expected, request);
        AssertSnapshot(before, Install);
        Assert.Empty(_signals.Calls);
        AssertCleaned();
        Assert.False(File.Exists(Journal));
    }

    private void AssertCleaned()
    {
        Assert.False(Directory.Exists(Staging));
        Assert.All(Directory.EnumerateFiles(Work), path => Assert.Equal(Journal, path));
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText);

    private static void AssertSnapshot(Dictionary<string, string> expected, string root) =>
        Assert.Equal(expected.OrderBy(pair => pair.Key), Snapshot(root).OrderBy(pair => pair.Key));

    private void WriteInstalled(string relative, string contents)
    {
        var path = Path.Combine(Install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private void CreateZip(params (string Name, string Contents)[] entries)
    {
        using var stream = new FileStream(Package, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, contents) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var target = entry.Open();
            target.Write(Encoding.UTF8.GetBytes(contents));
        }
    }

    private void SignManifest(string version = "2.0.0", string rid = "win-x64")
    {
        var bytes = File.ReadAllBytes(Package);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        WriteSignedManifest($$"""
            {"version":"{{version}}","publishedAt":"2026-10-01T00:00:00Z",
             "releaseNotesUrl":"https://github.com/tabunugoku/Snapwell/releases/tag/v2.0.0",
             "files":{"{{rid}}":{"url":"https://github.com/tabunugoku/Snapwell/releases/download/v2.0.0/app.zip",
             "sha256":"{{hash}}","size":{{bytes.Length}}
             }
             }
             }
            """);
    }

    private void WriteSignedManifest(string manifest)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);
        File.WriteAllBytes(Manifest, bytes);
        File.WriteAllBytes(Signature, ManifestSignature.Sign(bytes, _key.PrivateKeyPkcs8Base64));
    }

    private static int FindCentralHeader(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - 4; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) == 0x02014b50) { return index; }
        }
        throw new InvalidOperationException("Central directory not found.");
    }

    /// <summary>このテスト専用の一時フォルダーを削除する。</summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Stopper : IUpdateResidentStopper
    {
        internal bool Stopped { get; set; } = true;
        internal string? InstallDir { get; private set; }
        internal TimeSpan? Timeout { get; private set; }
        internal Action? BeforeStop { get; set; }

        /// <summary>停止要求を記録し、指定した結果を返す。</summary>
        public bool StopResidents(string installDir, TimeSpan timeout)
        {
            InstallDir = installDir;
            Timeout = timeout;
            BeforeStop?.Invoke();
            return Stopped;
        }
    }

    private sealed class Signals : IUpdateSignals
    {
        internal bool Ready { get; set; } = true;
        internal List<string> Calls { get; } = [];
        internal TimeSpan? Timeout { get; private set; }
        internal Action? OnApplied { get; set; }
        internal Action? BeforeWait { get; set; }

        /// <summary>通知順序を記録し、試験用の処理を行う。</summary>
        public void SignalApplied()
        {
            Calls.Add("applied");
            OnApplied?.Invoke();
        }

        /// <summary>待機条件を記録し、実際には待たずに確認結果を返す。</summary>
        public bool WaitReady(TimeSpan timeout)
        {
            Calls.Add("wait");
            Timeout = timeout;
            BeforeWait?.Invoke();
            return Ready;
        }
    }

    private sealed class Faults(IUpdateFileSystem inner) : IUpdateFileSystem
    {
        internal bool FailCopy { get; set; }
        internal bool UnexpectedCopyFailure { get; init; }
        internal bool FailRestore { get; init; }
        internal bool FailPackageDelete { get; init; }
        internal bool FailOldDelete { get; init; }
        internal bool FailJournalRead { get; init; }
        internal Action<string>? BeforeEnumerate { get; init; }

        /// <summary>列挙の直前に入力の改ざんを注入できる境界。</summary>
        public IEnumerable<string> EnumerateFiles(string root)
        {
            BeforeEnumerate?.Invoke(root);
            return inner.EnumerateFiles(root);
        }

        /// <summary>存在確認を実体へ委譲する。</summary>
        public bool Exists(string path) => inner.Exists(path);

        /// <summary>旧版の復元だけを失敗させる。</summary>
        public void RenameInPlace(string path, string newName)
        {
            if (FailRestore && path.EndsWith(".old", StringComparison.Ordinal))
            {
                throw new IOException("Injected restore failure.");
            }
            inner.RenameInPlace(path, newName);
        }

        /// <summary>配置先に書いた後で失敗を注入する。</summary>
        public void CopyFile(string source, string destination)
        {
            inner.CopyFile(source, destination);
            if (UnexpectedCopyFailure) { throw new InvalidOperationException("Unexpected copy failure."); }
            if (FailCopy)
            {
                FailCopy = false;
                throw new IOException("Injected copy failure.");
            }
        }

        /// <summary>パッケージの後始末だけを失敗させる。</summary>
        public void DeleteFile(string path)
        {
            if (FailOldDelete && path.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Injected backup cleanup failure.");
            }
            if (FailPackageDelete && Path.GetFileName(path) == "package.zip")
            {
                throw new UnauthorizedAccessException("Injected cleanup failure.");
            }
            inner.DeleteFile(path);
        }

        /// <summary>フォルダーの作成を実体へ委譲する。</summary>
        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        /// <summary>空のフォルダーの削除を実体へ委譲する。</summary>
        public void DeleteDirectoryIfEmpty(string path) => inner.DeleteDirectoryIfEmpty(path);

        /// <summary>ジャーナルの追記を実体へ委譲する。</summary>
        public void AppendLine(string path, string line) => inner.AppendLine(path, line);

        /// <summary>ジャーナルの読み取りで予期しない例外を注入する。</summary>
        public IEnumerable<string> ReadLines(string path)
        {
            if (FailJournalRead) { throw new InvalidOperationException("Injected journal failure."); }
            return inner.ReadLines(path);
        }
    }
}
