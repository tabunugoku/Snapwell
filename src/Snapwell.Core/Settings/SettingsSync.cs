using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Snapwell.Core.Settings;

/// <summary>同期による変更と、変更しなかった理由。</summary>
public enum SettingsSyncKind
{
    NoChange, Pushed, Pulled, ConflictLocalWon, ConflictRemoteWon, Skipped, Failed, Rejected, Busy,
}

/// <summary>取り込み後の設定と、編集中の値の扱い。</summary>
public enum SettingsSyncReloadKind
{
    Unchanged, ReadOnly, KeepEdits, Replace,
}

/// <summary>同期の結果。一致した場合だけハッシュと成功時刻を返す。</summary>
public sealed record SettingsSyncResult(SettingsSyncKind Kind, string? Message = null,
    string? LastSyncedHash = null, DateTimeOffset? LastSyncUtc = null)
{
    public bool Pulled => Kind is SettingsSyncKind.Pulled or SettingsSyncKind.ConflictRemoteWon;
    public bool Retryable => Kind is SettingsSyncKind.Failed or SettingsSyncKind.Busy;

    /// <summary>フォルダーのパスを含めず、画面とログに使う文言を返す。</summary>
    public string ToDisplayText() => Kind switch
    {
        SettingsSyncKind.NoChange => "変更なし",
        SettingsSyncKind.Pushed => "書き出し済み",
        SettingsSyncKind.Pulled => "取り込み済み",
        SettingsSyncKind.ConflictLocalWon => "競合: この PC の設定を採用",
        SettingsSyncKind.ConflictRemoteWon => "競合: 同期先の設定を採用",
        SettingsSyncKind.Busy => "同期中: ほかの処理の完了を待っています",
        SettingsSyncKind.Skipped => "見送り: " + Message,
        _ => "失敗: " + Message,
    };
}

/// <summary>利用者の同期フォルダーとポータブル設定を、バックアップを残して突き合わせる。</summary>
public sealed class SettingsSync
{
    public const string FileName = "snapwell-settings.json";
    // オンラインのみのファイルも含むため、呼び出し側は必ずバックグラウンドで実行する。
    // 異常に大きな同期ファイルによって常駐プロセスのメモリを使い切らないための上限。
    private const int MaximumBytes = 4 * 1024 * 1024;
    private readonly string _local;
    private readonly string _backups;
    private readonly string _mutexName;
    private readonly TimeProvider _time;
    private readonly Action? _beforeWrite;
    private readonly Action? _beforeReplace;
    private readonly Action? _beforeRecord;

    /// <summary>保存先と共有する Mutex 名を指定する。同期先は実行時の最新の端末状態から選ぶ。</summary>
    public SettingsSync(string localPath, string backupFolder, TimeProvider timeProvider,
        string mutexName) : this(localPath, backupFolder, timeProvider, mutexName, null) { }

    internal SettingsSync(string localPath, string backupFolder, TimeProvider timeProvider,
        string mutexName, Action? beforeWrite, Action? beforeReplace = null, Action? beforeRecord = null)
    {
        _local = Path.GetFullPath(localPath);
        _backups = Path.GetFullPath(backupFolder);
        _time = timeProvider;
        _mutexName = mutexName;
        _beforeWrite = beforeWrite;
        _beforeReplace = beforeReplace;
        _beforeRecord = beforeRecord;
    }

    /// <summary>本体と設定画面が同じローカル設定の同期を直列化するための名前。</summary>
    public static string MutexName(string localPath) => @"Local\Snapwell.SettingsSync." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(localPath).ToUpperInvariant())));

    /// <summary>未保存の編集を保ち、取り込み後に表示値を差し替えるかを判断する。</summary>
    public static SettingsSyncReloadKind DecideReload(PortableSettings saved,
        SettingsLoadResult<PortableSettings> loaded, bool hasChanges, bool isReadOnly)
    {
        if (loaded.IsReadOnly) { return SettingsSyncReloadKind.ReadOnly; }
        if (!SettingsTransfer.HasChanges(saved, loaded.Value) && !isReadOnly)
        {
            return SettingsSyncReloadKind.Unchanged;
        }
        return hasChanges ? SettingsSyncReloadKind.KeepEdits : SettingsSyncReloadKind.Replace;
    }

    /// <summary>設定の保存先と重なるフォルダーを拒否する。ファイルアクセスは行わない。</summary>
    public static string? ValidateFolder(string localPath, string folder)
    {
        try
        {
            var localFolder = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(localPath))!);
            var remoteFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (Contains(localFolder, remoteFolder) || Contains(remoteFolder, localFolder))
            {
                return "設定ファイルと同じフォルダー、その中、または親フォルダーは選べません。";
            }
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "同期フォルダーのパスが正しくありません。";
        }
    }

    /// <summary>通常の保存も同期と同じ排他で実行し、指定時間で取得できなければ何もしない。</summary>
    public static bool WithLock(string localPath, Action action, TimeSpan timeout) =>
        WithNamedLock(MutexName(localPath), action, timeout);

    private static bool WithNamedLock(string name, Action action, TimeSpan timeout)
    {
        using var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) { action(); }
            return acquired;
        }
        finally
        {
            if (acquired) { mutex.ReleaseMutex(); }
        }
    }

    /// <summary>排他を待たずに取得し、最新の端末状態による判断から成功の記録までを直列化する。</summary>
    public SettingsSyncResult Run(ISettingsStore<DeviceState> store, bool requireEnabled = false)
    {
        try
        {
            SettingsSyncResult result = new(SettingsSyncKind.Busy);
            WithNamedLock(_mutexName, () =>
            {
                var loaded = store.TryReload() ?? store.Load();
                var state = loaded.Value;
                // 手動の「今すぐ同期」はオフでも使える。自動同期だけは最新のオン・オフを確かめる。
                if (loaded.IsReadOnly || requireEnabled && !state.SyncEnabled ||
                    string.IsNullOrWhiteSpace(state.SyncFolder))
                {
                    result = new(SettingsSyncKind.Skipped, "同期がオフか、同期先が未設定か、端末状態が読み取り専用です。");
                    return;
                }
                var rejection = ValidateFolder(_local, state.SyncFolder);
                if (rejection is not null)
                {
                    result = new(SettingsSyncKind.Rejected, rejection);
                    return;
                }
                result = RunLocked(state, Path.Combine(Path.GetFullPath(state.SyncFolder), FileName));
                if (result.LastSyncedHash is null) { return; }
                _beforeRecord?.Invoke();
                DeviceStateChanges.Update(store, current =>
                    current.SyncEnabled == state.SyncEnabled &&
                    string.Equals(current.SyncFolder, state.SyncFolder, StringComparison.OrdinalIgnoreCase)
                        ? current with { LastSyncedHash = result.LastSyncedHash, LastSyncUtc = result.LastSyncUtc }
                        : current);
            }, TimeSpan.Zero);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or FutureSettingsVersionException)
        {
            return new(SettingsSyncKind.Failed, "ファイルを読み書きできません。接続とアクセス権を確認してください。");
        }
    }

    private SettingsSyncResult RunLocked(DeviceState state, string remotePath)
    {
        if (!Directory.Exists(Path.GetDirectoryName(remotePath)))
        {
            return new(SettingsSyncKind.Failed, "同期フォルダーが見つかりません。");
        }
        var local = Read(_local);
        var remote = Read(remotePath);
        if (local.Future || remote.Future)
        {
            return new(SettingsSyncKind.Skipped, "新しい版の設定は上書きできません。Snapwell を更新してください。");
        }
        if (local.Broken)
        {
            return new(SettingsSyncKind.Skipped, "この PC の設定が壊れているため、同期を見送りました。");
        }
        if (local.Bytes is null && remote.Bytes is null)
        {
            return new(SettingsSyncKind.NoChange);
        }
        if (remote.Broken && local.Value is null)
        {
            return new(SettingsSyncKind.Skipped, "同期先の設定が壊れています。");
        }
        if (local.Hash is not null && local.Hash == remote.Hash)
        {
            return Success(SettingsSyncKind.NoChange, local.Hash);
        }

        bool push;
        SettingsSyncKind kind;
        if (local.Bytes is not null && (remote.Bytes is null || remote.Broken || state.LastSyncedHash is not null &&
            state.LastSyncedHash == remote.Hash))
        {
            push = true;
            kind = SettingsSyncKind.Pushed;
        }
        else if (local.Bytes is null || state.LastSyncedHash is not null && state.LastSyncedHash == local.Hash)
        {
            push = false;
            kind = SettingsSyncKind.Pulled;
        }
        else
        {
            push = local.Modified >= remote.Modified;
            kind = push ? SettingsSyncKind.ConflictLocalWon : SettingsSyncKind.ConflictRemoteWon;
        }
        var source = push ? local : remote;
        _beforeWrite?.Invoke();
        // 同期ソフトや設定画面が判断中に書いた内容は、古い判断で上書きしない。
        VerifyUnchanged(_local, local);
        VerifyUnchanged(remotePath, remote);
        WriteDestination(source.Value!, push ? remotePath : _local, push ? remote : local,
            push ? "remote" : "local", () =>
            {
                VerifyUnchanged(push ? _local : remotePath, source);
            });
        return Success(kind, source.Hash!);
    }

    private SettingsSyncResult Success(SettingsSyncKind kind, string hash) =>
        new(kind, LastSyncedHash: hash, LastSyncUtc: _time.GetUtcNow());

    private static Snapshot Read(string path)
    {
        byte[] bytes;
        DateTime modified;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes)
            {
                throw new IOException("設定ファイルが大きすぎる。");
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            modified = File.GetLastWriteTimeUtc(path);
        }
        catch (FileNotFoundException) { return new(null, default); }
        try
        {
            var json = bytes.AsMemory();
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { json = json[3..]; }
            using var document = JsonDocument.Parse(json);
            var version = SettingsSchema.ReadVersion(document.RootElement);
            if (version > PortableSettings.CurrentSchemaVersion) { return new(bytes, modified, Future: true); }
            var value = document.RootElement.Deserialize(SettingsJsonContext.Default.PortableSettings)
                ?? throw new JsonException("設定が null。");
            value = SettingsSchema.Migrate(value, version).Normalize();
            return new(bytes, modified, value, Hash(value));
        }
        catch (JsonException) { return new(bytes, modified, Broken: true); }
    }

    private static string Hash(PortableSettings value)
    {
        using var document = JsonSerializer.SerializeToDocument(value, SettingsJsonContext.Default.PortableSettings);
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory)) { WriteCanonical(writer, document.RootElement); }
        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) { WriteCanonical(writer, item); }
            writer.WriteEndArray();
        }
        else { element.WriteTo(writer); }
    }

    private static Snapshot VerifyUnchanged(string path, Snapshot snapshot)
    {
        var current = Read(path);
        if ((snapshot.Bytes is null) != (current.Bytes is null) || snapshot.Bytes is not null &&
            !snapshot.Bytes.AsSpan().SequenceEqual(current.Bytes))
        {
            throw new IOException("判断中に設定が変更された。");
        }
        return current;
    }

    private void Backup(byte[] bytes, string side)
    {
        Directory.CreateDirectory(_backups);
        var stamp = _time.GetUtcNow();
        string path;
        do
        {
            path = Path.Combine(_backups, "settings-" + stamp.ToString("yyyyMMdd'T'HHmmssfff",
                CultureInfo.InvariantCulture) + "-" + side + ".json");
            stamp = stamp.AddMilliseconds(1);
        } while (File.Exists(path));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        foreach (var old in Directory.GetFiles(_backups, "settings-????????T?????????-" + side + ".json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(10))
        {
            File.Delete(old);
        }
    }

    private void WriteDestination(PortableSettings value, string path, Snapshot destination, string side,
        Action verifySource)
    {
        if (side == "local") { Directory.CreateDirectory(Path.GetDirectoryName(_local)!); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, SettingsJsonContext.Default.PortableSettings);
                stream.Flush(flushToDisk: true);
            }
            _beforeReplace?.Invoke();
            verifySource();
            // 一時ファイルを準備した後の書き込み先を検証し、その内容だけを退避する。
            var current = VerifyUnchanged(path, destination);
            if (current.Bytes is not null) { Backup(current.Bytes, side); }
            verifySource();
            VerifyUnchanged(path, destination);
            if (current.Bytes is not null) { File.Replace(temporary, path, null); }
            else { File.Move(temporary, path, overwrite: false); }
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private static bool Contains(string parent, string child) =>
        string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private sealed record Snapshot(byte[]? Bytes, DateTime Modified, PortableSettings? Value = null,
        string? Hash = null, bool Future = false, bool Broken = false);
}
