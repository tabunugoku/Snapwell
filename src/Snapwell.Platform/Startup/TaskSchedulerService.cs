using System.Runtime.InteropServices;
using Snapwell.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.TaskScheduler;

namespace Snapwell.Platform.Startup;

/// <summary>CsWin32 の unmanaged COM 呼び出しで、自分のタスクだけを操作する。</summary>
public sealed unsafe class TaskSchedulerService : IAdminTaskService
{
    /// <summary>読み取りは通常権限で行い、存在しない場合だけ null を返す。</summary>
    public string? ReadCommand()
    {
        using var connection = new Connection();
        using var name = new Bstr(AdminTaskXml.TaskName);
        IRegisteredTask* task = null;
        BSTR xml = default;
        try
        {
            try
            {
                connection.Folder->GetTask(name.Value, &task);
            }
            catch (Exception exception) when (exception.HResult == unchecked((int)0x80070002))
            {
                return null;
            }

            xml = task->Xml;
            return AdminTaskXml.ReadCommand(xml.ToString());
        }
        finally
        {
            Marshal.FreeBSTR((nint)xml);
            if (task != null)
            {
                task->Release();
            }
        }
    }

    /// <summary>現在のユーザーに限った対話ログオンのタスクを作成または更新する。</summary>
    public void Register(string executablePath, string userSid)
    {
        using var connection = new Connection();
        using var name = new Bstr(AdminTaskXml.TaskName);
        using var xml = new Bstr(AdminTaskXml.Create(executablePath, userSid));
        IRegisteredTask* task = null;
        try
        {
            // VT_EMPTY の userId は XML の Principal を使う。パスワードも保持しない。
            connection.Folder->RegisterTask(name.Value, xml.Value, 6, default, default,
                TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN, default, &task);
        }
        finally
        {
            if (task != null)
            {
                task->Release();
            }
        }
    }

    /// <summary>未登録の場合も成功として扱い、それ以外の失敗は呼び出し側へ返す。</summary>
    public void Delete()
    {
        using var connection = new Connection();
        using var name = new Bstr(AdminTaskXml.TaskName);
        try
        {
            connection.Folder->DeleteTask(name.Value, 0);
        }
        catch (Exception exception) when (exception.HResult == unchecked((int)0x80070002))
        {
        }
    }

    private sealed class Bstr(string text) : IDisposable
    {
        internal BSTR Value { get; } = new(Marshal.StringToBSTR(text));
        public void Dispose() => Marshal.FreeBSTR((nint)Value);
    }

    private sealed class Connection : IDisposable
    {
        private readonly ITaskService* _service;
        private readonly bool _uninitialize;
        internal readonly ITaskFolder* Folder;

        internal Connection()
        {
            var result = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            // UI の STA は既に初期化済み。RPC_E_CHANGED_MODE の場合はその apartment を使う。
            if (result.Value != unchecked((int)0x80010106))
            {
                result.ThrowOnFailure();
                _uninitialize = true;
            }

            try
            {
                var clsid = new Guid("0f87369f-a4e5-4cfc-bd3e-73e6154572dd");
                var iid = ITaskService.IID_Guid;
                void* service = null;
                PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &service).ThrowOnFailure();
                _service = (ITaskService*)service;
                _service->Connect(default, default, default, default);
                using var root = new Bstr(@"\");
                ITaskFolder* folder = null;
                _service->GetFolder(root.Value, &folder);
                Folder = folder;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Folder != null)
            {
                Folder->Release();
            }

            if (_service != null)
            {
                _service->Release();
            }

            if (_uninitialize)
            {
                PInvoke.CoUninitialize();
            }
        }
    }
}
