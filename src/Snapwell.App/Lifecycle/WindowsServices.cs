using System.Security.Principal;
using System.Threading.Channels;
using Snapwell.Core.Hooks;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.SystemSettings;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Lifecycle;

/// <summary>所有権ではなく名前付きオブジェクトの寿命で二重起動を防ぐ。</summary>
internal sealed class UserInstanceMutex(
    Func<string>? sidProvider = null, Func<string, (Mutex Mutex, bool CreatedNew)>? create = null) : ISingleInstance
{
    private SingleInstance? _mutex;

    public bool TryAcquire()
    {
        var sid = (sidProvider ?? CurrentSid)();
        _mutex = SingleInstance.TryAcquireNamed(@"Local\Snapwell." + sid, create);
        return _mutex is not null;
    }

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        // i18n-keep: 画面に出さない開発者向けの例外・ログ。
        return identity.User?.Value ?? throw new InvalidOperationException("ユーザーの SID を取得できない。");
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
internal sealed class HookService : IHookService
{
    private readonly HookThread _hooks;
    private readonly IAppLog _log;

    public HookService(HookThread hooks, IAppLog log)
    {
        _hooks = hooks;
        _log = log;
        // 張り直しは稀で、原因の調査に要る。端末内の app.log にだけ残す。呼び出しはフックのスレッドの外。
        hooks.Rehooked += report => _log.Error(new InvalidOperationException(
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            $"低レベルフックを張り直した (理由={report.Reason}、成功={report.Succeeded}、{report.Count} 回目、" +
            // i18n-keep: 同上。
            $"証拠={report.Evidence}、OS の入力とフックの差={report.GapMs}ms、" +
            // i18n-keep: 同上。
            $"キーボードの無音={report.SinceKeyboardMs}ms、マウスの無音={report.SinceMouseMs}ms、" +
            // i18n-keep: 同上。
            $"OS の入力からの経過={report.SinceInputMs}ms、バックオフ={report.BackoffMs}ms)。"));
    }

    /// <summary>ロック・復帰などを伝える。ウィンドウプロシージャから呼ぶので、すぐ返る。</summary>
    public void Notify(HookEnvironmentChange change) => _hooks.Notify(change);

    public ChannelReader<HookEvent> Events => _hooks.Events;
    public bool MouseTrackingEnabled { set => _hooks.MouseTrackingEnabled = value; }
    public Exception? Failure => _hooks.Failure;
    public bool Start() => _hooks.Start();
    public void Stop()
    {
        // Platform の Stop は Join に上限がない。OS 側の応答を待ち続けないよう別タスクで行う。
        var stop = Task.Run(() =>
        {
            try
            {
                if (!_hooks.Stop())
                {
                    // i18n-keep: 画面に出さない開発者向けの例外・ログ。
                    _log.Error(new InvalidOperationException("フックを解除できない。"));
                }
            }
            catch (Exception exception)
            {
                try
                {
                    _log.Error(exception);
                }
                catch (Exception)
                {
                }
            }
        });
        if (!stop.Wait(TimeSpan.FromMilliseconds(250)))
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            _log.Error(new TimeoutException("フックの停止完了を待たず、OS 設定の復元を続ける。"));
        }
    }
}

/// <summary>変更する設定と復元の条件を明示して同意を得る。</summary>
internal sealed class WindowsConsentPrompt(Func<string, string, bool>? show = null) : IConsentPrompt
{
    /// <summary>本文とタイトルをアプリの言語にし、OS の「はい」だけを同意として扱う。</summary>
    public bool ConfirmStandardSnapChange() => (show ?? Show)(UiText.Current.Get("Consent.StandardSnap.Body"),
        UiText.Current.Get("Consent.StandardSnap.Title"));

    private static bool Show(string body, string title) => PInvoke.MessageBox(default, body, title,
        MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONQUESTION) == MESSAGEBOX_RESULT.IDYES;
}

/// <summary>外部へ送らず、端末内だけに障害を記録する。</summary>
internal sealed class FileAppLog : IAppLog
{
    private const long MaximumBytes = 1024 * 1024;
    private readonly object _gate = new();
    public void Error(Exception exception)
    {
        lock (_gate)
        {
            var directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Snapwell");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "app.log");
            // 同じ失敗が続いても無制限に増やさない。上限を超えたら一世代だけ残して入れ替える。
            if (File.Exists(path) && new FileInfo(path).Length > MaximumBytes)
            {
                File.Move(path, path + ".1", overwrite: true);
            }

            File.AppendAllText(path,
                DateTimeOffset.Now.ToString("O") + " " + exception + Environment.NewLine);
        }
    }
}
