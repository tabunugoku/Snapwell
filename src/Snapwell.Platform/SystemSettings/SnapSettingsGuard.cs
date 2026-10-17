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
            var hasState = File.Exists(_statePath);
            if (hasState)
            {
                // 正常な退避値は上書きせず、破損時だけ既定値へ戻して保存し直す。
                if (!TryReadState(out _))
                {
                    if (!RestoreDefaultAndQuarantine())
                    {
                        return false;
                    }

                    hasState = false;
                }
            }

            if (!hasState)
            {
                if (!_settings.TryGetEnabled(out var enabled))
                {
                    return false;
                }

                SaveState(new SnapSettingsState { Version = 1, DragSnapEnabled = enabled });
            }

            // 変更に失敗しても復元用ファイルを残し、再試行できるようにする。
            return TrySetAndVerify(false);
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

    /// <summary>前回の退避値を復元し、状態ファイルがなければ何もせず true を返す。復元失敗は false を返す。</summary>
    public bool RecoverOnStartup()
    {
        lock (_gate)
        {
            return RestoreCore();
        }
    }

    private bool RestoreCore()
    {
        if (!File.Exists(_statePath))
        {
            return true;
        }

        if (!TryReadState(out var state))
        {
            return RestoreDefaultAndQuarantine();
        }

        if (!TrySetAndVerify(state!.DragSnapEnabled))
        {
            return false;
        }

        File.Delete(_statePath);
        return true;
    }

    private bool RestoreDefaultAndQuarantine()
    {
        // 元の値は不明なので無効のまま固定されることを避け、OS の既定値である有効へ戻す。
        // 元々無効だった利用者も有効になるが、復元不能な無効化を残さないことを優先する。
        // ダウングレード時の未来の版も読めない状態として扱い、同じ既定値へ戻す。
        if (!TrySetAndVerify(true))
        {
            return false;
        }

        // 復元成功後だけ隔離する。移動に失敗しても元のファイルを残し、再試行できるようにする。
        var brokenPath = _statePath + ".broken";
        if (File.Exists(brokenPath))
        {
            brokenPath += "." + Guid.NewGuid().ToString("N");
        }

        File.Move(_statePath, brokenPath);
        return true;
    }

    private bool TrySetAndVerify(bool enabled)
    {
        // 呼び出し元の _gate 内で書き込みと読み直しを続けて行う。
        // 単一インスタンスのミューテックスにより、別インスタンスの書き込みはない前提。
        return _settings.TrySetEnabled(enabled)
            && _settings.TryGetEnabled(out var actual)
            && actual == enabled;
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
