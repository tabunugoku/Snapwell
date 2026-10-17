using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapwell.Platform.SystemSettings;

/// <summary>設定を変更する前に元の値を保存し、無効化・終了・次回起動時に復元する。</summary>
public sealed class SnapSettingsGuard
{
    private readonly object _gate = new();
    private readonly ISnapSystemSettings _settings;
    private readonly string _statePath;

    /// <summary>状態ファイルを指定し、省略時は端末固有の LocalAppData を使う。</summary>
    public SnapSettingsGuard(ISnapSystemSettings settings, string? statePath = null)
    {
        _settings = settings;
        _statePath = Path.GetFullPath(statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapwell", "os-state.json"));
    }

    /// <summary>元の値の保存が完了してから標準スナップを無効にする。IO 失敗は呼び出し元へ返す。</summary>
    public bool Disable()
    {
        lock (_gate)
        {
            if (File.Exists(_statePath))
            {
                // 最初の値を上書きしない。破損していれば変更せず復旧の判断を呼び出し元へ返す。
                if (!TryReadState(out _))
                {
                    return false;
                }
            }
            else
            {
                if (!_settings.TryGetEnabled(out var enabled))
                {
                    return false;
                }

                SaveState(new SnapSettingsState { Version = 1, DragSnapEnabled = enabled });
            }

            // 変更に失敗しても復元用ファイルを残し、再試行できるようにする。
            return _settings.TrySetEnabled(false);
        }
    }

    /// <summary>保存した値に戻せたときだけ状態ファイルを削除する。</summary>
    public bool Restore()
    {
        lock (_gate)
        {
            return RestoreCore();
        }
    }

    /// <summary>前回の状態が残っていれば復元し、復元できたときに true を返す。</summary>
    public bool RecoverOnStartup()
    {
        lock (_gate)
        {
            return File.Exists(_statePath) && RestoreCore();
        }
    }

    private bool RestoreCore()
    {
        if (!File.Exists(_statePath))
        {
            return true;
        }

        if (!TryReadState(out var state) || !_settings.TrySetEnabled(state!.DragSnapEnabled))
        {
            return false;
        }

        File.Delete(_statePath);
        return true;
    }

    private void SaveState(SnapSettingsState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporaryPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, SnapSettingsJsonContext.Default.SnapSettingsState);
                stream.Flush(flushToDisk: true);
            }

            // 同じディレクトリ内の rename で完全な JSON だけを見せる。既存の退避値は上書きしない。
            File.Move(temporaryPath, _statePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private bool TryReadState(out SnapSettingsState? state)
    {
        state = null;
        try
        {
            using var stream = File.OpenRead(_statePath);
            state = JsonSerializer.Deserialize(stream, SnapSettingsJsonContext.Default.SnapSettingsState);
            if (state is null || state.Version != 1)
            {
                throw new JsonException("未対応または欠落した状態ファイルの版。");
            }

            return true;
        }
        catch (JsonException)
        {
            // 壊れた値から OS 設定を推測しない。既存の破損資料も残す。
            var brokenPath = _statePath + ".broken";
            if (File.Exists(brokenPath))
            {
                brokenPath += "." + Guid.NewGuid().ToString("N");
            }

            File.Move(_statePath, brokenPath);
            state = null;
            return false;
        }
    }
}

internal sealed record SnapSettingsState
{
    [JsonRequired]
    public int Version { get; init; }

    [JsonRequired]
    public bool DragSnapEnabled { get; init; }
}

[JsonSerializable(typeof(SnapSettingsState))]
internal sealed partial class SnapSettingsJsonContext : JsonSerializerContext;
