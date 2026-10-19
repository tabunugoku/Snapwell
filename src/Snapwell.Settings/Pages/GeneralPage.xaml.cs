using System.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;
using Snapwell.Platform.Startup;

namespace Snapwell.Settings.Pages;

/// <summary>有効・無効 (端末状態) と自動起動の登録を編集する。</summary>
public sealed partial class GeneralPage : Page
{
    private readonly SettingsSession _session;
    private readonly StartupController _startup;
    private readonly string _executable;
    private bool _startupBusy;
    private readonly bool _startupAvailable;
    private readonly ISettingsStore<DeviceState> _deviceStore = SettingsStores.Device();
    private bool _syncingEnabled = true;
    private bool _refreshing = true;
    private bool _syncingFixedSize = true;
    private (StartupMode Mode, AutoStartState State) _lastStartup;

    /// <summary>同じフォルダーに本体がある場合だけ自動起動を操作できる画面を作る。</summary>
    public GeneralPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        SyncFixedSizeToggle();
        RefreshEnabledToggle();
        Loaded += (_, _) =>
        {
            _session.Changed += Session_Changed;
            SyncFixedSizeToggle();
        };
        Unloaded += (_, _) => _session.Changed -= Session_Changed;

        var executable = Path.Combine(AppContext.BaseDirectory, "Snapwell.exe");
        _executable = executable;
        _startup = new(new RegistryRunKey(), new TaskSchedulerService(), new ElevatedRunner(executable),
            new StartupMemory(), executable);
        if (!File.Exists(executable))
        {
            AutoStartToggle.IsEnabled = false;
            AdminStartToggle.IsEnabled = false;
            RegistrationStatus.Text = "Snapwell.exe が同じフォルダーに無いため、自動起動を設定できません";
            RegistrationStatus.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _startupAvailable = true;
            Refresh();
        }
        catch (Exception exception) when (IsStartupFailure(exception))
        {
            ReportFailure();
            AutoStartToggle.IsEnabled = false;
            AdminStartToggle.IsEnabled = false;
        }
    }

    // 画面を開くたびに端末状態を読み直す。開いている間のトレイからの切り替えは、開き直すと反映される。
    private void RefreshEnabledToggle()
    {
        try
        {
            var result = _deviceStore.Load();
            SetEnabledToggle(result.Value.Enabled);
            EnabledToggle.IsEnabled = !result.IsReadOnly;
            if (result.IsReadOnly)
            {
                ReportEnabledFailure("新しい版の端末状態のため、有効・無効を切り替えられません。Snapwell を更新してください。");
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            EnabledToggle.IsEnabled = false;
            ReportEnabledFailure("端末状態を読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。");
        }
        finally
        {
            _syncingEnabled = false;
        }
    }

    // 表示を合わせるだけの IsOn の書き込み。Toggled を保存として扱わせない。
    private void SetEnabledToggle(bool isOn)
    {
        var previous = _syncingEnabled;
        _syncingEnabled = true;
        try
        {
            EnabledToggle.IsOn = isOn;
        }
        finally
        {
            _syncingEnabled = previous;
        }
    }

    private void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingEnabled)
        {
            return;
        }

        ErrorNotice.IsOpen = false;
        try
        {
            // 起動中の Snapwell は device.json の変更を監視して追従する。保存ボタンは待たない。
            DeviceStateChanges.SetEnabled(_deviceStore, EnabledToggle.IsOn);
        }
        catch (FutureSettingsVersionException)
        {
            RevertEnabled("新しい版の端末状態のため、有効・無効を切り替えられません。Snapwell を更新してください。");
            EnabledToggle.IsEnabled = false;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            RevertEnabled("有効・無効を保存できませんでした。ファイルの場所とアクセス権を確認してください。");
        }
    }

    // 失敗したときは、ファイルに残っている値へ表示を戻す。
    private void RevertEnabled(string message)
    {
        ReportEnabledFailure(message);
        try
        {
            SetEnabledToggle(_deviceStore.Load().Value.Enabled);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            SetEnabledToggle(!EnabledToggle.IsOn);
            EnabledToggle.IsEnabled = false;
        }
    }

    private void ReportEnabledFailure(string message)
    {
        ErrorNotice.Message = message;
        ErrorNotice.IsOpen = true;
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or System.Text.Json.JsonException;

    private void Session_Changed(object? sender, EventArgs e) => SyncFixedSizeToggle();

    // 設定側の値を表示へ反映するだけ。Toggled を設定の更新として扱わせない。
    private void SyncFixedSizeToggle()
    {
        _syncingFixedSize = true;
        try
        {
            MoveFixedSizeToggle.IsEnabled = !_session.IsReadOnly;
            MoveFixedSizeToggle.IsOn = _session.Current.MoveFixedSizeWindows;
        }
        finally
        {
            _syncingFixedSize = false;
        }
    }

    private void MoveFixedSize_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingFixedSize)
        {
            _session.Update(_session.Current with { MoveFixedSizeWindows = MoveFixedSizeToggle.IsOn });
        }
    }

    private void Refresh()
    {
        _lastStartup = _startup.Read();
        SetStartupToggles();
        ShowStatus();
    }

    // 登録の説明だけを更新する。トグル自身は書き換えない。
    private void ShowStatus()
    {
        RegistrationStatus.Text = _lastStartup.State == AutoStartState.Elsewhere
            ? "別の場所の Snapwell が登録されています。オフにしてからオンにすると、このフォルダーの Snapwell に置き換えます"
            : "";
        RegistrationStatus.Visibility = RegistrationStatus.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var protectedLocation = ExecutableLocation.IsProtected(_executable);
        AdminStartToggle.IsEnabled = _startupAvailable && !_startupBusy &&
            (protectedLocation || _lastStartup.Mode == StartupMode.Admin);
        AutoStartToggle.IsEnabled = _startupAvailable && !_startupBusy;
        AdminStartStatus.Text = protectedLocation ? "" :
            "管理者以外が書き込めない場所（Program Files など）に配置すると、オンにできます";
        AdminStartStatus.Visibility = protectedLocation ? Visibility.Collapsed : Visibility.Visible;
    }

    // 表示を合わせるだけの IsOn の書き込み。Toggled を登録の操作として扱わせない。
    private void SetStartupToggles()
    {
        _refreshing = true;
        try
        {
            AutoStartToggle.IsOn = _lastStartup.Mode != StartupMode.None;
            AdminStartToggle.IsOn = _lastStartup.Mode == StartupMode.Admin;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _startupBusy)
        {
            return;
        }

        _ = ChangeStartupAsync(admin: false, AutoStartToggle.IsOn);
    }

    private void AdminStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _startupBusy)
        {
            return;
        }

        _ = ChangeStartupAsync(admin: true, AdminStartToggle.IsOn);
    }

    private async Task ChangeStartupAsync(bool admin, bool enabled)
    {
        _startupBusy = true;
        AutoStartToggle.IsEnabled = false;
        AdminStartToggle.IsEnabled = false;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Severity = InfoBarSeverity.Error;
        try
        {
            var result = admin
                ? await _startup.SetAdminAsync(enabled)
                : await _startup.SetAutoStartAsync(enabled);
            if (result == StartupChangeResult.Cancelled)
            {
                ErrorNotice.Severity = InfoBarSeverity.Informational;
                ErrorNotice.Message = "管理者権限の許可が得られなかったため変更していません";
                ErrorNotice.IsOpen = true;
            }
            else if (result != StartupChangeResult.Success)
            {
                ErrorNotice.Message = result == StartupChangeResult.UnprotectedLocation
                    ? "管理者以外が書き込めない場所に配置してください"
                    : "タスク スケジューラに登録できませんでした";
                ErrorNotice.IsOpen = true;
            }
            else if (admin && enabled)
            {
                ErrorNotice.Severity = InfoBarSeverity.Informational;
                ErrorNotice.Message = "次のサインインから管理者として起動します";
                ErrorNotice.IsOpen = true;
            }
        }
        catch (Exception exception) when (IsStartupFailure(exception))
        {
            ReportFailure();
        }
        finally
        {
            // Toggled の呼び出しが戻った後だけ表示を合わせ、NativeAOT のアニメーション再入を避ける。
            DispatcherQueue.TryEnqueue(FinishStartupChange);
        }
    }

    private void FinishStartupChange()
    {
        _startupBusy = false;
        try
        {
            Refresh();
        }
        catch (Exception exception) when (IsStartupFailure(exception))
        {
            ReportFailure();
            SetStartupToggles();
            AutoStartToggle.IsEnabled = false;
            AdminStartToggle.IsEnabled = false;
        }
    }

    private void ReportFailure()
    {
        ErrorNotice.Severity = InfoBarSeverity.Error;
        ErrorNotice.Message = "自動起動の登録を読み書きできませんでした。アクセス権を確認してください。";
        ErrorNotice.IsOpen = true;
    }

    private static bool IsStartupFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException or
            System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or
            System.Xml.XmlException or InvalidOperationException;
}
