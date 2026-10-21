using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Snapwell.Platform.Windows;

/// <summary>プロセス ID から実行ファイル名を取得する境界。</summary>
public interface IProcessNameSource
{
    /// <summary>取得できない場合は null を返す。</summary>
    string? GetName(uint processId);
}

/// <summary>公開 API で名前を取得し、開始時刻を照合してプロセス ID の再利用を区別する。</summary>
public sealed class Win32ProcessNameSource : IProcessNameSource
{
    private readonly object _gate = new();
    private readonly IProcessNameApi _api;
    private readonly Dictionary<uint, (ulong StartTime, string Name)> _cache = [];

    /// <summary>プロセスへの問い合わせは GetName が呼ばれるまで行わない。</summary>
    public Win32ProcessNameSource() : this(new Win32ProcessNameApi()) { }

    internal Win32ProcessNameSource(IProcessNameApi api) => _api = api;

    /// <summary>問い合わせ用ハンドルはキャッシュせず、呼び出しごとに閉じる。</summary>
    public string? GetName(uint processId)
    {
        lock (_gate)
        {
            var handle = _api.Open(processId);
            if (handle == 0)
            {
                _cache.Remove(processId);
                return null;
            }

            try
            {
                var start = _api.GetStartTime(handle);
                if (start is { } time && _cache.TryGetValue(processId, out var cached) && cached.StartTime == time)
                {
                    return cached.Name;
                }

                _cache.Remove(processId);
                var path = _api.GetImagePath(handle);
                var name = path is null ? null : Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                {
                    return null;
                }

                if (start is { } startTime)
                {
                    _cache[processId] = (startTime, name);
                }

                return name;
            }
            finally
            {
                _api.Close(handle);
            }
        }
    }
}

internal interface IProcessNameApi
{
    nint Open(uint processId);
    ulong? GetStartTime(nint handle);
    string? GetImagePath(nint handle);
    void Close(nint handle);
}

internal sealed unsafe class Win32ProcessNameApi : IProcessNameApi
{
    public nint Open(uint processId) => (nint)PInvoke.OpenProcess(
        PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId).Value;

    public unsafe ulong? GetStartTime(nint handle)
    {
        FILETIME creation, exit, kernel, user;
        return PInvoke.GetProcessTimes(new HANDLE(handle), &creation, &exit, &kernel, &user)
            ? ((ulong)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime : null;
    }

    public unsafe string? GetImagePath(nint handle)
    {
        // Win32 の拡張パスも切り詰めず取得する。
        var buffer = new char[32768];
        var length = (uint)buffer.Length;
        fixed (char* path = buffer)
        {
            return PInvoke.QueryFullProcessImageName(new HANDLE(handle), 0, path, &length)
                ? new string(path, 0, (int)length) : null;
        }
    }

    public void Close(nint handle) => PInvoke.CloseHandle(new HANDLE(handle));
}
