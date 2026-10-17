using System.Security.Principal;
using System.Threading.Channels;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.SystemSettings;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Lifecycle;

/// <summary>所有権ではなく名前付きオブジェクトの寿命で二重起動を防ぐ。</summary>
internal sealed class UserInstanceMutex : ISingleInstance
{
    private Mutex? _mutex;

    public bool TryAcquire()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("ユーザーの SID を取得できない。");
        var mutex = new Mutex(false, @"Local\Snapwell." + sid, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    public void Release() => Interlocked.Exchange(ref _mutex, null)?.Dispose();
}

/// <summary>Platform が持つ退避ファイルと復旧処理をそのまま使う。</summary>
internal sealed class SnapGuardService(SnapSettingsGuard guard) : ISnapGuard
{
    public bool RecoverOnStartup() => guard.RecoverOnStartup();
    public bool Disable() => guard.Disable();
    public bool Restore() => guard.Restore();
}

/// <summary>Platform の停止待ちに上限を付け、セッション終了時の復元へ進める。</summary>
internal sealed class HookService(HookThread hooks, IAppLog log) : IHookService
{
    public ChannelReader<HookEvent> Events => hooks.Events;
    public bool MouseTrackingEnabled { set => hooks.MouseTrackingEnabled = value; }
    public Exception? Failure => hooks.Failure;
    public bool Start() => hooks.Start();
    public void Stop()
    {
        // Platform の Stop は Join に上限がない。OS 側の応答を待ち続けないよう別タスクで行う。
        var stop = Task.Run(() =>
        {
            try
            {
                if (!hooks.Stop())
                {
                    log.Error(new InvalidOperationException("フックを解除できない。"));
                }
            }
            catch (Exception exception)
            {
                try
                {
                    log.Error(exception);
                }
                catch (Exception)
                {
                }
            }
        });
        if (!stop.Wait(TimeSpan.FromMilliseconds(250)))
        {
            log.Error(new TimeoutException("フックの停止完了を待たず、OS 設定の復元を続ける。"));
        }
    }
}

/// <summary>変更する設定と復元の条件を明示して同意を得る。</summary>
internal sealed class WindowsConsentPrompt : IConsentPrompt
{
    public bool ConfirmStandardSnapChange() => PInvoke.MessageBox(default,
        "設定アプリの「ウィンドウのスナップ」(SPI_SETWINARRANGING) をこのセッションで停止します。\n" +
        "Snapwell の一時停止時と終了時には、変更前の設定に戻します。\n\n停止してよいですか？",
        "Snapwell — Windows の設定変更", MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONQUESTION) ==
        MESSAGEBOX_RESULT.IDYES;
}

/// <summary>外部へ送らず、端末内だけに障害を記録する。</summary>
internal sealed class FileAppLog : IAppLog
{
    private readonly object _gate = new();
    public void Error(Exception exception)
    {
        lock (_gate)
        {
            var directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Snapwell");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "app.log"),
                DateTimeOffset.Now.ToString("O") + " " + exception + Environment.NewLine);
        }
    }
}
