using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Snapwell.Platform.Dwm;

/// <summary>DWM_WINDOW_CORNER_PREFERENCE と同じ値を持つ角の指定。</summary>
public enum CornerStyle { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

/// <summary>ハンドル再利用時の誤復元を防ぐ、プロセスの識別と変更前の値。</summary>
public sealed record CornerState(long Handle, uint ProcessId, long ProcessStartTicks, CornerStyle Original);

/// <summary>角の設定に使う API の境界。</summary>
public interface ICornerApi
{
    /// <summary>変更前の値と、ハンドルを所有するプロセスを取得する。</summary>
    CornerState? Read(nint handle);
    /// <summary>ハンドルがまだ存在するかを返す。</summary>
    bool IsWindow(nint handle);
    /// <summary>HRESULT が成功の場合だけ true を返す。</summary>
    bool TrySet(nint handle, CornerStyle style);
}

/// <summary>公開 DWM API による他プロセスの角設定。</summary>
public sealed class Win32CornerApi : ICornerApi
{
    /// <summary>読み取れない対象は変更せず、復元できる対象だけを返す。</summary>
    public unsafe CornerState? Read(nint handle)
    {
        var window = new HWND(handle);
        DWM_WINDOW_CORNER_PREFERENCE preference;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Failed)
        {
            return null;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new((long)handle, processId, process.StartTime.ToUniversalTime().Ticks, (CornerStyle)preference);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>閉じたウィンドウと、値を読めない生存ウィンドウを区別する。</summary>
    public bool IsWindow(nint handle) => PInvoke.IsWindow(new HWND(handle));

    /// <summary>失効したハンドルや権限不足の失敗を false で返す。</summary>
    public unsafe bool TrySet(nint handle, CornerStyle style)
    {
        var preference = (DWM_WINDOW_CORNER_PREFERENCE)style;
        return PInvoke.DwmSetWindowAttribute(new HWND(handle), DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Succeeded;
    }
}

/// <summary>変更前の値を先に永続化し、終了・無効化・次の起動で復元する。</summary>
public sealed class CornerStyler
{
    private readonly object _gate = new();
    private readonly ICornerApi _api;
    private readonly string? _statePath;
    private readonly Action<string>? _log;
    private readonly Dictionary<long, CornerState> _modified = [];

    /// <summary>退避ファイルは設定とは分け、指定しない場合はメモリだけで管理する。</summary>
    public CornerStyler(ICornerApi api, string? statePath = null, Action<string>? log = null)
    {
        _api = api;
        _statePath = statePath;
        _log = log;
        if (statePath is not null && File.Exists(statePath))
        {
            foreach (var state in JsonSerializer.Deserialize(File.ReadAllText(statePath),
                CornerStateJsonContext.Default.ListCornerState) ?? [])
            {
                _modified[state.Handle] = state;
            }
        }
    }

    /// <summary>復元を待っているハンドルの数。</summary>
    public int TrackedCount
    {
        get
        {
            lock (_gate)
            {
                return _modified.Count;
            }
        }
    }

    /// <summary>変更前の値を保持したまま指定を変更し、既定の指定では元の値へ復元する。</summary>
    public bool Apply(nint handle, CornerStyle style)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)style, 3u);
        lock (_gate)
        {
            if (style == CornerStyle.Default)
            {
                return Restore(handle);
            }

            var current = _api.Read(handle);
            if (current is null)
            {
                _log?.Invoke($"角設定の取得に失敗: {handle}");
                return false;
            }

            if (!_modified.TryGetValue((long)handle, out var original) || !SameOwner(original, current))
            {
                _modified[(long)handle] = current;
                try
                {
                    Save();
                }
                catch
                {
                    // 退避できなかった記録で、次の適用を許可しない。
                    if (original is null)
                    {
                        _modified.Remove((long)handle);
                    }
                    else
                    {
                        _modified[(long)handle] = original;
                    }

                    throw;
                }
            }

            var success = _api.TrySet(handle, style);
            if (!success)
            {
                _log?.Invoke($"角設定の適用に失敗: {handle}");
            }

            return success;
        }
    }

    /// <summary>閉じたウィンドウの記録を取り除く。</summary>
    public void Forget(nint handle)
    {
        lock (_gate)
        {
            if (_modified.Remove((long)handle))
            {
                Save();
            }
        }
    }

    /// <summary>元の値に復元し、失敗した対象の退避値は次回のために残す。</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach (var handle in _modified.Keys.ToArray())
            {
                try
                {
                    Restore((nint)handle);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _log?.Invoke($"角設定の復元記録の保存に失敗: {handle}: {exception.Message}");
                }
            }
        }
    }

    private bool Restore(nint handle)
    {
        if (!_modified.TryGetValue((long)handle, out var original))
        {
            return true;
        }

        var current = _api.Read(handle);
        if (current is null && _api.IsWindow(handle))
        {
            _log?.Invoke($"角設定の復元対象を確認できない: {handle}");
            return false;
        }

        if (current is not null && SameOwner(original, current) && !_api.TrySet(handle, original.Original))
        {
            _log?.Invoke($"角設定の復元に失敗: {handle}");
            return false;
        }

        _modified.Remove((long)handle);
        Save();
        return true;
    }

    private static bool SameOwner(CornerState left, CornerState right) =>
        left.ProcessId == right.ProcessId && left.ProcessStartTicks == right.ProcessStartTicks;

    private void Save()
    {
        if (_statePath is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_statePath))!);
        var temporary = _statePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, _modified.Values.ToList(), CornerStateJsonContext.Default.ListCornerState);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, _statePath, overwrite: true);
    }
}

[JsonSerializable(typeof(List<CornerState>))]
internal sealed partial class CornerStateJsonContext : JsonSerializerContext;
