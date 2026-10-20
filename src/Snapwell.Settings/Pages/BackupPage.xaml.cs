using Microsoft.UI.Xaml.Controls;

namespace Snapwell.Settings.Pages;

/// <summary>ポータブル設定の書き出しと取り込みの入口。</summary>
public sealed partial class BackupPage : Page
{
    /// <summary>ホストのウィンドウと編集中の設定をファイル操作へ渡す。</summary>
    public BackupPage(SettingsSession session, nint owner)
    {
        InitializeComponent();
        ConfigureTransfers(session, owner);
        ConfigureSync(session, owner);
    }

    partial void ConfigureTransfers(SettingsSession session, nint owner);
    partial void ConfigureSync(SettingsSession session, nint owner);
}
