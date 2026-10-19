using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Snapwell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Snapwell.Platform.Monitors;

/// <summary>Per-Monitor DPI Aware v2 の呼び出し元でモニターを列挙する。</summary>
[SupportedOSPlatform("windows10.0.22000")]
public sealed class Win32MonitorSource : IMonitorSource
{
    /// <summary>作業領域・デバイス名・実効 DPI を取得する。</summary>
    public unsafe IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var state = new EnumerationState();
        var handle = GCHandle.Alloc(state);
        try
        {
            var success = PInvoke.EnumDisplayMonitors(default, null, &Enumerate, (nint)GCHandle.ToIntPtr(handle));
            if (state.Error is not null)
            {
                throw new InvalidOperationException("モニター情報を取得できない。", state.Error);
            }

            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return state.Monitors.AsReadOnly();
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe BOOL Enumerate(HMONITOR monitor, HDC deviceContext, RECT* rectangle, LPARAM data)
    {
        var state = (EnumerationState)GCHandle.FromIntPtr(data.Value).Target!;
        try
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            // 抜き差しの最中に失敗した一台だけを飛ばし、残りのモニターは返す。
            if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                return true;
            }

            var result = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
                out var dpiX, out var dpiY);
            if (result.Failed)
            {
                return true;
            }

            state.Monitors.Add(new MonitorInfo(info.szDevice.ToString(), ConvertRect(info.monitorInfo.rcMonitor),
                ConvertRect(info.monitorInfo.rcWork), dpiX, dpiY, (info.monitorInfo.dwFlags & 1) != 0));
            return true;
        }
        catch (Exception error)
        {
            // 例外をネイティブの列挙スタックへ出さない。
            state.Error = error;
            return false;
        }
    }

    private static PixelRect ConvertRect(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

    private sealed class EnumerationState
    {
        internal readonly List<MonitorInfo> Monitors = [];
        internal Exception? Error;
    }
}
