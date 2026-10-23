using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static WinHttp;

try
{
    var uri = ProbeReport.ParseUrl(args);
    ProbeReport.Before();
    var clock = Stopwatch.StartNew();
    var (status, bytes) = Get(uri);
    clock.Stop();
    ProbeReport.After("WinHttpProbe", status, bytes, clock.Elapsed.TotalMilliseconds);
    return 0;
}
catch (Exception exception)
{
    var nativeError = exception is Win32Exception win32 ? $" native_error={win32.NativeErrorCode}" : "";
    Console.Error.WriteLine($"error={exception.GetType().Name}{nativeError} message={exception.Message}");
    return 1;
}

static (uint Status, int Bytes) Get(Uri uri)
{
    var session = WinHttpOpen("Snapwell/0.0.0", 4, null, null, 0); // AUTOMATIC_PROXY
    Check(session != 0, nameof(WinHttpOpen));
    try
    {
        uint protocols = 0x00000800 | 0x00002000; // TLS 1.2 と TLS 1.3 だけを許可する。
        Check(WinHttpSetOption(session, 84, ref protocols, sizeof(uint)), "WinHttpSetOption(SECURE_PROTOCOLS)");
        Check(WinHttpSetTimeouts(session, 30000, 30000, 30000, 30000), nameof(WinHttpSetTimeouts));
        var connection = WinHttpConnect(session, uri.IdnHost, (ushort)uri.Port, 0);
        Check(connection != 0, nameof(WinHttpConnect));
        try
        {
            var request = WinHttpOpenRequest(connection, "GET", uri.PathAndQuery, null, null, 0,
                uri.Scheme == Uri.UriSchemeHttps ? 0x00800000u : 0);
            Check(request != 0, nameof(WinHttpOpenRequest));
            try
            {
                // 別ホストへの転送は既定に任せ、HTTPS から HTTP への降格だけを禁じる。
                uint redirectPolicy = 1; // REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP
                Check(WinHttpSetOption(request, 88, ref redirectPolicy, sizeof(uint)),
                    "WinHttpSetOption(REDIRECT_POLICY)");
                Check(WinHttpSendRequest(request, null, 0, 0, 0, 0, 0), nameof(WinHttpSendRequest));
                Check(WinHttpReceiveResponse(request, 0), nameof(WinHttpReceiveResponse));
                uint size = sizeof(uint);
                Check(WinHttpQueryHeaders(request, 19 | 0x20000000, null, out var status, ref size, 0),
                    nameof(WinHttpQueryHeaders));
                var buffer = new byte[ProbeReport.MaxBytes];
                var bytes = 0;
                while (bytes < buffer.Length)
                {
                    Check(WinHttpReadData(request, buffer, (uint)(buffer.Length - bytes), out var read),
                        nameof(WinHttpReadData));
                    if (read == 0)
                    {
                        break;
                    }
                    bytes += checked((int)read);
                }
                return (status, bytes);
            }
            finally
            {
                Check(WinHttpCloseHandle(request), nameof(WinHttpCloseHandle));
            }
        }
        finally
        {
            Check(WinHttpCloseHandle(connection), nameof(WinHttpCloseHandle));
        }
    }
    finally
    {
        Check(WinHttpCloseHandle(session), nameof(WinHttpCloseHandle));
    }
}

static void Check(bool success, string operation)
{
    if (!success)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
}

internal static class WinHttp
{
    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint WinHttpOpen(string agent, uint accessType, string? proxy, string? bypass, uint flags);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint WinHttpConnect(nint session, string server, ushort port, uint reserved);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint WinHttpOpenRequest(nint connection, string verb, string path,
        string? version, string? referrer, nint acceptTypes, uint flags);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpSendRequest(nint request, string? headers, uint headersLength,
        nint optional, uint optionalLength, uint totalLength, nuint context);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpReceiveResponse(nint request, nint reserved);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpQueryHeaders(nint request, uint infoLevel, string? name,
        out uint buffer, ref uint bufferLength, nint index);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpReadData(nint request, [Out] byte[] buffer, uint bytesToRead, out uint bytesRead);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpSetOption(nint handle, uint option, ref uint buffer, uint bufferLength);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpSetTimeouts(nint session, int resolve, int connect, int send, int receive);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinHttpCloseHandle(nint handle);
}
