using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Snapwell.Platform.Updates;

/// <summary>インストール先ごとに更新の適用と後始末を排他する。</summary>
public interface IUpdateInstallLock
{
    /// <summary>即時に取得し、使用中なら null を返す。取得したスレッドで解放する。</summary>
    IDisposable? TryAcquire(string installDir);
}

/// <summary>セッションをまたぐ名前付きミューテックスで更新ヘルパーを排他する。</summary>
public sealed class UpdateInstallLock(bool isProtected) : IUpdateInstallLock
{
    [ThreadStatic]
    private static HashSet<string>? _heldNames;

    /// <summary>完全パスを小文字にした SHA256 の名前で取得し、同じスレッドで解放する所有権を返す。</summary>
    public IDisposable? TryAcquire(string installDir)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)).ToLowerInvariant();
        var name = @"Global\Snapwell.Update.Install." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        // Mutex は同じスレッドからの再取得を許すため、ヘルパーの再入も排他する。
        if (_heldNames?.Contains(name) == true) { return null; }
        var mutex = Create(name);
        try
        {
            try
            {
                if (!mutex.WaitOne(0)) { mutex.Dispose(); return null; }
            }
            catch (AbandonedMutexException)
            {
                // 所有権は取得済み。前回の復旧資料の判断は PreviousUpdateUnresolved に任せる。
            }
            (_heldNames ??= []).Add(name);
            return new Ownership(mutex, name);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private unsafe Mutex Create(string name)
    {
        var sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)";
        if (!isProtected)
        {
            using var identity = WindowsIdentity.GetCurrent();
            sddl += $"(A;;GA;;;{identity.User!.Value})";
        }
        // UpdateSignals と同じく、追加パッケージを使わず作成時に DACL を渡す。
        var security = new RawSecurityDescriptor(sddl);
        var bytes = new byte[security.BinaryLength];
        security.GetBinaryForm(bytes, 0);
        fixed (byte* descriptor = bytes)
        {
            var attributes = new UpdateEventNative.SecurityAttributes
            {
                Length = (uint)sizeof(UpdateEventNative.SecurityAttributes),
                Descriptor = descriptor,
            };
            var raw = UpdateMutexNative.CreateMutexEx(&attributes, name, 0, 0x1f0001);
            if (raw == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            var mutex = new Mutex();
            var original = mutex.SafeWaitHandle;
            mutex.SafeWaitHandle = new SafeWaitHandle(raw, true);
            original.Dispose();
            return mutex;
        }
    }

    private sealed class Ownership(Mutex mutex, string name) : IDisposable
    {
        private Mutex? _mutex = mutex;

        /// <summary>取得したスレッドで所有権とハンドルを解放する。</summary>
        public void Dispose()
        {
            if (_mutex is not { } owned) { return; }
            owned.ReleaseMutex();
            owned.Dispose();
            _mutex = null;
            _heldNames!.Remove(name);
        }
    }
}

internal static partial class UpdateMutexNative
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial nint CreateMutexEx(UpdateEventNative.SecurityAttributes* attributes, string name,
        uint flags, uint desiredAccess);
}
