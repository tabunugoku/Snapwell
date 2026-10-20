using System.Threading.Channels;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐本体の同期の選択とローカル変更を受け取る境界。</summary>
internal interface ISettingsSyncService : IDisposable
{
    /// <summary>端末固有の同期の選択を反映する。</summary>
    void Configure(DeviceState state);
    /// <summary>ローカル設定の変更を同期へ渡す。</summary>
    void Request();
}

/// <summary>設定の変更を間引いて通知する監視の境界。</summary>
internal interface ISettingsWatcher : IDisposable
{
    event Action? Changed;
    void Start();
}

/// <summary>同じユーザーの二重起動を防ぐ境界。</summary>
internal interface ISingleInstance
{
    bool TryAcquire();
    void Release();
}

/// <summary>OS 設定の退避と復旧の境界。</summary>
internal interface ISnapGuard
{
    bool RecoverOnStartup();
    bool Disable();
    bool Restore();
}

/// <summary>フックの登録と配送元の境界。</summary>
internal interface IHookService
{
    bool Start();
    void Stop();
    ChannelReader<HookEvent> Events { get; }
    bool MouseTrackingEnabled { set; }
    Exception? Failure { get; }
}

/// <summary>初回の OS 設定変更の同意。</summary>
internal interface IConsentPrompt
{
    bool ConfirmStandardSnapChange();
}

/// <summary>常駐部分の診断の記録。</summary>
internal interface IAppLog
{
    void Error(Exception exception);
}

/// <summary>配送する操作と移動スレッドの所有者。</summary>
internal interface IAppRuntime : IDisposable
{
    void SetEnabled(bool enabled);
    void Handle(HookEvent value);
    void ArrangeCurrentMonitor();
    /// <summary>指定した対象でランチャーを開く。</summary>
    void OpenLauncher(nint target, uint ignorePointerUpToMs);
    void CancelAll();
}

/// <summary>トレイに表示する状態のスナップショット。</summary>
internal sealed record TrayState(bool Enabled, bool DisableStandardSnap, string? Status = null,
    bool LauncherAvailable = false);

/// <summary>メインスレッドのトレイとメッセージループ。</summary>
internal interface ITrayService
{
    event Action? ToggleRequested;
    event Action? StandardSnapRequested;
    event Action? RestoreRequested;
    event Action? ArrangeRequested;
    event Action<nint, uint>? LauncherRequested;
    event Action? ExitRequested;
    event Action? SessionEnding;
    void Show(TrayState state);
    void Update(TrayState state);
    void Remove();
    void Post(Action action);
    void RunMessageLoop();
}

/// <summary>読み込み済みの設定から、今回の起動に使う操作サービスを作る。</summary>
internal delegate IAppRuntime RuntimeFactory(PortableSettings settings, Func<bool> enabled, Action toggle);
