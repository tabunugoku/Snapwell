using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Snapwell.Core.Settings;

/// <summary>読み込んだ値と、破損からの復帰・新しい版の保護状態。</summary>
public sealed record SettingsLoadResult<T>(T Value, bool ResetToDefaults = false, bool IsReadOnly = false);

/// <summary>設定ファイルの境界。</summary>
public interface ISettingsStore<T>
{
    /// <summary>設定と読み込みの状態を返す。</summary>
    SettingsLoadResult<T> Load();

    /// <summary>完全なファイルで置き換える。新しい版は上書きしない。</summary>
    void Save(T value);

    /// <summary>元のファイルを動かさず、読めない場合は現在の値を維持させる。</summary>
    SettingsLoadResult<T>? TryReload() => null;
}

/// <summary>ソース生成した JSON を同じディレクトリ内で原子的に置き換える。</summary>
public sealed class SettingsStore<T>(string path, JsonTypeInfo<T> typeInfo, Func<T> defaults,
    Func<T, T> normalize) : ISettingsStore<T> where T : class
{
    private readonly object _gate = new();
    private readonly string _path = Path.GetFullPath(path);
    private readonly Action? _beforeReplace;
    private bool _readOnly;
    private readonly int _currentSchemaVersion = 1;
    private readonly Func<T, double, T>? _migrate;

    internal string FilePath => _path;

    /// <summary>ストアごとの現在の版と、元の JSON の版を受け取る移行を指定する。</summary>
    public SettingsStore(string path, JsonTypeInfo<T> typeInfo, Func<T> defaults, Func<T, T> normalize,
        int currentSchemaVersion, Func<T, double, T>? migrate = null) : this(path, typeInfo, defaults, normalize)
    {
        _currentSchemaVersion = currentSchemaVersion;
        _migrate = migrate;
    }

    internal SettingsStore(string path, JsonTypeInfo<T> typeInfo, Func<T> defaults, Func<T, T> normalize,
        Action beforeReplace, int currentSchemaVersion = 1) : this(path, typeInfo, defaults, normalize, currentSchemaVersion)
    {
        _beforeReplace = beforeReplace;
    }

    /// <summary>未来の版を先に検出し、壊れたファイルだけを隔離する。</summary>
    public SettingsLoadResult<T> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new(defaults());
            }

            try
            {
                return Read();
            }
            catch (JsonException)
            {
                // 時刻に加えて乱数を付け、同時刻の隔離でも元のファイルを失わない。
                var suffix = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
                File.Move(_path, _path + ".broken-" + suffix);
                _readOnly = false;
                return new(defaults(), ResetToDefaults: true);
            }
        }
    }

    /// <summary>書き込み失敗時は元の設定を残す。</summary>
    public void Save(T value)
    {
        lock (_gate)
        {
            // 読み込み後に新しい版へ変わった場合は、置き換え後のバックアップの確認で守る。
            if (_readOnly)
            {
                throw new FutureSettingsVersionException("新しい版の設定は上書きできない。");
            }

            if (value is PortableSettings portable) { portable.ValidateWindowRules(); }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backupPath = _path + "." + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, normalize(value), typeInfo);
                    stream.Flush(flushToDisk: true);
                }

                var exists = File.Exists(_path);
                _beforeReplace?.Invoke();
                if (exists)
                {
                    File.Replace(temporaryPath, _path, backupPath);
                    var future = false;
                    try
                    {
                        using var stream = File.OpenRead(backupPath);
                        using var document = JsonDocument.Parse(stream);
                        future = IsFuture(document.RootElement);
                    }
                    catch (JsonException)
                    {
                        // 壊れた JSON は未来の版と判定せず、今回保存した設定を残す。
                    }

                    if (future)
                    {
                        File.Replace(backupPath, _path, null);
                        _readOnly = true;
                        throw new FutureSettingsVersionException("新しい版の設定は上書きできない。");
                    }
                }
                else
                {
                    File.Move(temporaryPath, _path, overwrite: false);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
        }
    }

    /// <summary>同期中の書きかけや共有違反でも、ファイルと現在の設定を保つ。</summary>
    public SettingsLoadResult<T>? TryReload()
    {
        lock (_gate)
        {
            try
            {
                return Read();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private SettingsLoadResult<T> Read()
    {
        using var stream = File.OpenRead(_path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        // 新しい版のファイルが現在の版に置き換わった場合は、保護を解く。
        _readOnly = IsFuture(root);
        if (_readOnly)
        {
            return new(defaults(), IsReadOnly: true);
        }

        var value = root.Deserialize(typeInfo) ?? throw new JsonException("設定が null。");
        return new(normalize(_migrate is null ? value : _migrate(value, SettingsSchema.ReadVersion(root))));
    }

    private bool IsFuture(JsonElement root) => SettingsSchema.ReadVersion(root) > _currentSchemaVersion;
}

/// <summary>設定ごとの型情報と既定の保存場所。</summary>
public static class SettingsStores
{
    /// <summary>指定パスまたは AppData のポータブル設定ストアを作る。</summary>
    public static SettingsStore<PortableSettings> Portable(string? path = null) => new(
        path ?? PortablePath(), SettingsJsonContext.Default.PortableSettings,
        () => new(), value => value.Normalize(), PortableSettings.CurrentSchemaVersion, SettingsSchema.Migrate);

    /// <summary>指定パスまたは LocalAppData の端末状態ストアを作る。</summary>
    public static SettingsStore<DeviceState> Device(string? path = null, string? portablePath = null)
    {
        var devicePath = path ?? DevicePath();
        portablePath ??= path is null ? PortablePath()
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "settings.json");
        // 端末状態がまだない旧版や、破損を退避した既存の利用者にも案内を出さない。
        var existingUser = File.Exists(devicePath) || File.Exists(portablePath);
        return new(devicePath, SettingsJsonContext.Default.DeviceState,
            () => new() { SetupCompleted = existingUser },
            value => value with { SchemaVersion = DeviceState.CurrentSchemaVersion },
            DeviceState.CurrentSchemaVersion, DeviceState.Migrate);
    }

    /// <summary>ポータブル設定の既定のパス。</summary>
    public static string PortablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapwell", "settings.json");

    /// <summary>端末状態の既定のパス。</summary>
    public static string DevicePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapwell", "device.json");
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PortableSettings))]
[JsonSerializable(typeof(Snapwell.Core.Rules.WindowRule))]
[JsonSerializable(typeof(DeviceState))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
