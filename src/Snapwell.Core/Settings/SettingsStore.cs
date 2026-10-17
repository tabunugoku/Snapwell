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
}

/// <summary>ソース生成した JSON を同じディレクトリ内で原子的に置き換える。</summary>
public sealed class SettingsStore<T>(string path, JsonTypeInfo<T> typeInfo, Func<T> defaults,
    Func<T, T> normalize) : ISettingsStore<T> where T : class
{
    private readonly object _gate = new();
    private readonly string _path = Path.GetFullPath(path);
    private bool _readOnly;

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
                using var stream = File.OpenRead(_path);
                using var document = JsonDocument.Parse(stream);
                if (IsFuture(document.RootElement))
                {
                    _readOnly = true;
                    return new(defaults(), IsReadOnly: true);
                }

                var value = document.RootElement.Deserialize(typeInfo) ?? throw new JsonException("設定が null。");
                return new(normalize(value));
            }
            catch (JsonException)
            {
                // 時刻に加えて乱数を付け、同時刻の隔離でも元のファイルを失わない。
                var suffix = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
                File.Move(_path, _path + ".broken-" + suffix);
                return new(defaults(), ResetToDefaults: true);
            }
        }
    }

    /// <summary>書き込み失敗時は元の設定を残す。</summary>
    public void Save(T value)
    {
        lock (_gate)
        {
            // 別プロセスや同期によって読み込み後に新しい版になった場合も保護する。
            if (!_readOnly && File.Exists(_path))
            {
                using var stream = File.OpenRead(_path);
                using var document = JsonDocument.Parse(stream);
                _readOnly = IsFuture(document.RootElement);
            }

            if (_readOnly)
            {
                throw new InvalidOperationException("新しい版の設定は上書きできない。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, normalize(value), typeInfo);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static bool IsFuture(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("SchemaVersion", out var version) && version.ValueKind == JsonValueKind.Number &&
        version.TryGetInt32(out var number) && number > 1;
}

/// <summary>設定ごとの型情報と既定の保存場所。</summary>
public static class SettingsStores
{
    /// <summary>指定パスまたは AppData のポータブル設定ストアを作る。</summary>
    public static SettingsStore<PortableSettings> Portable(string? path = null) => new(
        path ?? PortablePath(), SettingsJsonContext.Default.PortableSettings,
        () => new(), value => value.Normalize());

    /// <summary>指定パスまたは LocalAppData の端末状態ストアを作る。</summary>
    public static SettingsStore<DeviceState> Device(string? path = null) => new(
        path ?? DevicePath(), SettingsJsonContext.Default.DeviceState,
        () => new(), value => value with { SchemaVersion = 1 });

    /// <summary>ポータブル設定の既定のパス。</summary>
    public static string PortablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapwell", "settings.json");

    /// <summary>端末状態の既定のパス。</summary>
    public static string DevicePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapwell", "device.json");
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PortableSettings))]
[JsonSerializable(typeof(DeviceState))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
