using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Snapwell.Platform.Updates;

/// <summary>SID から更新専用の名前付きイベント名を作る。</summary>
public sealed class UpdateSignalNames(string sid, string prefix = @"Local\Snapwell.Update")
{
    private readonly string _hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)));

    /// <summary>常駐の正常終了を要求するイベント名。</summary>
    public string Quit => prefix + ".Quit." + _hash;

    /// <summary>新版の配置完了を通知するイベント名。</summary>
    public string Applied => prefix + ".Applied." + _hash;

    /// <summary>新版の起動成功を通知するイベント名。</summary>
    public string Ready => prefix + ".Ready." + _hash;

    /// <summary>進行役が保持する Applied の存在を調べ、アクセス拒否も進行中として扱う。</summary>
    public bool IsUpdateRunning()
    {
        try
        {
            using var applied = EventWaitHandle.OpenExisting(Applied);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>現在の利用者と Administrators だけに全権を与えて新規作成する。</summary>
    public static unsafe EventWaitHandle Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new RawSecurityDescriptor($"D:P(A;;GA;;;{identity.User!.Value})(A;;GA;;;BA)");
        var bytes = new byte[security.BinaryLength];
        security.GetBinaryForm(bytes, 0);
        fixed (byte* descriptor = bytes)
        {
            var attributes = new UpdateEventNative.SecurityAttributes
            {
                Length = (uint)sizeof(UpdateEventNative.SecurityAttributes),
                Descriptor = descriptor,
            };
            var raw = UpdateEventNative.CreateEventEx(&attributes, name, 1, 0x1f0003);
            var error = Marshal.GetLastPInvokeError();
            if (raw == 0) { throw new Win32Exception(error); }
            var handle = Wrap(raw);
            if (error != 183) { return handle; }
            handle.Dispose();
        }
        // i18n-keep: 画面に出さない開発者向けの例外。
        throw new IOException("An update event already exists.");
    }

    /// <summary>既存のイベントだけを開き、待機または通知に必要な権限だけを要求する。</summary>
    public static EventWaitHandle Open(string name, bool signal = false)
    {
        var raw = UpdateEventNative.OpenEvent(signal ? 2u : 0x100000u, 0, name);
        if (raw != 0) { return Wrap(raw); }
        var error = Marshal.GetLastPInvokeError();
        // i18n-keep: 画面に出さない開発者向けの例外。
        if (error == 2) { throw new WaitHandleCannotBeOpenedException("The update event does not exist."); }
        if (error == 5) { throw new UnauthorizedAccessException("The update event cannot be opened."); }
        throw new Win32Exception(error);
    }

    private static EventWaitHandle Wrap(nint raw)
    {
        var handle = new EventWaitHandle(false, EventResetMode.ManualReset);
        var original = handle.SafeWaitHandle;
        handle.SafeWaitHandle = new SafeWaitHandle(raw, true);
        original.Dispose();
        return handle;
    }

    /// <summary>作成済みのイベントだけを開いて通知し、通常起動では何もしない。</summary>
    public static bool TrySignal(string name)
    {
        try
        {
            using var handle = Open(name, signal: true);
            return handle.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
            or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>昇格ヘルパーが進行役のイベントだけを開いて使う。</summary>
public sealed class UpdateSignals(UpdateSignalNames names) : IUpdateSignals, IDisposable
{
    private EventWaitHandle? _ready;

    /// <summary>配置完了を進行役に通知する。</summary>
    public void SignalApplied()
    {
        // 進行役は Ready を見た直後に終了するため、通知前から Ready の寿命を保持する。
        try { _ready ??= UpdateSignalNames.Open(names.Ready); }
        catch (WaitHandleCannotBeOpenedException) { }
        using var applied = UpdateSignalNames.Open(names.Applied, signal: true);
        applied.Set();
    }

    /// <summary>Ready が開けるまでの再試行も含め、指定の上限内で起動を待つ。</summary>
    public bool WaitReady(TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        do
        {
            try
            {
                _ready ??= UpdateSignalNames.Open(names.Ready);
                var remaining = timeout - Stopwatch.GetElapsedTime(start);
                return _ready.WaitOne(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                var remaining = timeout - Stopwatch.GetElapsedTime(start);
                if (remaining <= TimeSpan.Zero) { break; }
                Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
            }
        } while (Stopwatch.GetElapsedTime(start) < timeout);
        return false;
    }

    /// <summary>起動確認まで保持したイベントを解放する。</summary>
    public void Dispose() => _ready?.Dispose();
}

// System.Threading.AccessControl は追加パッケージが要るため、文書化された API をソース生成で呼ぶ。
internal static partial class UpdateEventNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SecurityAttributes
    {
        internal uint Length;
        internal void* Descriptor;
        internal int Inherit;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial nint CreateEventEx(SecurityAttributes* attributes, string name, uint flags,
        uint desiredAccess);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenEventW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenEvent(uint desiredAccess, int inherit, string name);
}
