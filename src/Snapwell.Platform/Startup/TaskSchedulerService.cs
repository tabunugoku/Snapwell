using System.Runtime.InteropServices;
using System.Security.Principal;
using Snapwell.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.TaskScheduler;
using Windows.Win32.System.Variant;

namespace Snapwell.Platform.Startup;

/// <summary>CsWin32 の unmanaged COM 呼び出しで、自分のタスクだけを操作する。</summary>
public sealed unsafe class TaskSchedulerService : IAdminTaskService
{
    private readonly AdminTaskRegistration _registration;

    /// <summary>接続前に現在のユーザーの SID を固定する。</summary>
    public TaskSchedulerService()
    {
        using var identity = WindowsIdentity.GetCurrent();
        _registration = new(new SchedulerStore(),
            identity.User?.Value ?? throw new InvalidOperationException("SID を取得できない。"));
    }

    /// <summary>現在のタスクの実行先を読む。</summary>
    public string? ReadCommand() => _registration.ReadCommand();

    /// <summary>安全な DACL の新規タスクに置き換える。</summary>
    public void Register(string executablePath, string userSid) => _registration.Register(executablePath, userSid);

    /// <summary>自分のタスクを削除する。</summary>
    public void Delete() => _registration.Delete();

    private sealed class SchedulerStore : IAdminTaskStore
    {
        /// <summary>読み取りは通常権限で行い、存在しない場合だけ null を返す。</summary>
        public string? ReadXml(string taskName)
        {
            using var connection = new Connection();
            using var name = new Bstr(taskName);
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
                return xml.ToString();
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

        /// <summary>セキュリティ記述子を指定し、既存タスクを更新しない。</summary>
        public void Create(string taskName, string taskXml, string sddl)
        {
            using var connection = new Connection();
            using var name = new Bstr(taskName);
            using var xml = new Bstr(taskXml);
            using var descriptor = new Bstr(sddl);
            var security = new VARIANT();
            security.Anonymous.Anonymous.vt = VARENUM.VT_BSTR;
            security.Anonymous.Anonymous.Anonymous.bstrVal = descriptor.Value;
            IRegisteredTask* task = null;
            try
            {
                // VT_EMPTY の userId は XML の Principal を使う。パスワードも保持しない。
                // 新規作成のみ。同名を先に作られた場合は失敗し、更新へ切り替えない。
                const int TaskCreate = 2;
                // Principal の ACE をサービスに自動追加させず、指定した DACL だけを使う。
                const int TaskDontAddPrincipalAce = 0x10;
                connection.Folder->RegisterTask(name.Value, xml.Value, TaskCreate | TaskDontAddPrincipalAce,
                    default, default, TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN, security, &task);
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
        public void Delete(string taskName)
        {
            using var connection = new Connection();
            using var name = new Bstr(taskName);
            try
            {
                connection.Folder->DeleteTask(name.Value, 0);
            }
            catch (Exception exception) when (exception.HResult == unchecked((int)0x80070002))
            {
            }
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
