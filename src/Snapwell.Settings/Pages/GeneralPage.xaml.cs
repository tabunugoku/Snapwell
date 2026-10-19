using System.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Settings;

namespace Snapwell.Settings.Pages;

/// <summary>自動起動の登録を編集する。</summary>
public sealed partial class GeneralPage : Page
{
    private readonly AutoStart _autoStart;
    private bool _refreshing = true;
    private AutoStartState _lastState;

    /// <summary>同じフォルダーに本体がある場合だけ自動起動を操作できる画面を作る。</summary>
    public GeneralPage(SettingsSession session)
    {
        InitializeComponent();

        var executable = Path.Combine(AppContext.BaseDirectory, "Snapwell.exe");
        _autoStart = new(new RegistryRunKey(), executable);
        if (!File.Exists(executable))
        {
            AutoStartToggle.IsEnabled = false;
            RegistrationStatus.Text = "Snapwell.exe が同じフォルダーに無いため、自動起動を設定できません";
            RegistrationStatus.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            Refresh();
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            ReportFailure();
            AutoStartToggle.IsEnabled = false;
        }
    }

    private void Refresh()
    {
        _lastState = _autoStart.GetState();
        SetToggle(_lastState != AutoStartState.Off);
        ShowStatus();
    }

    // 登録の説明だけを更新する。トグル自身は書き換えない。
    private void ShowStatus()
    {
        RegistrationStatus.Text = _lastState == AutoStartState.Elsewhere
            ? "別の場所の Snapwell が登録されています。オフにしてからオンにすると、このフォルダーの Snapwell に置き換えます"
            : "";
        RegistrationStatus.Visibility = RegistrationStatus.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // 表示を合わせるだけの IsOn の書き込み。Toggled を登録の操作として扱わせない。
    private void SetToggle(bool isOn)
    {
        _refreshing = true;
        try
        {
            AutoStartToggle.IsOn = isOn;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        ErrorNotice.IsOpen = false;
        try
        {
            // 成功したらトグルはそのまま（利用者が切り替えた表示が正）。ばねの動きの途中で IsOn を書き戻さない。
            _autoStart.SetEnabled(AutoStartToggle.IsOn);
            _lastState = _autoStart.GetState();
            ShowStatus();
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            ReportFailure();
            try
            {
                Refresh();
            }
            catch (Exception readException) when (IsRegistryFailure(readException))
            {
                // 再取得も拒否された場合は、最後に読めた表示に戻して操作を止める。
                SetToggle(_lastState != AutoStartState.Off);
                AutoStartToggle.IsEnabled = false;
            }
        }
    }

    private void ReportFailure()
    {
        ErrorNotice.Message = "自動起動の登録を読み書きできませんでした。アクセス権を確認してください。";
        ErrorNotice.IsOpen = true;
    }

    private static bool IsRegistryFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException;
}
