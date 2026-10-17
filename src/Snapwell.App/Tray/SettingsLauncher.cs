using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Snapwell.App.Lifecycle;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Tray;

/// <summary>トレイから設定画面を開く境界。</summary>
internal interface ISettingsLauncher
{
    void Open();
}

/// <summary>同じ配布フォルダーの設定画面を起動し、欠落と起動失敗を知らせる。</summary>
internal sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IAppLog _log;
    private readonly string _path;
    private readonly Func<string, bool> _exists;
    private readonly Action<ProcessStartInfo> _start;
    private readonly Action<string> _show;

    internal SettingsLauncher(IAppLog log, string? baseDirectory = null, Func<string, bool>? exists = null,
        Action<ProcessStartInfo>? start = null, Action<string>? show = null)
    {
        _log = log;
        _path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Snapwell.Settings.exe");
        _exists = exists ?? File.Exists;
        _start = start ?? (info => { using var process = Process.Start(info); });
        _show = show ?? ShowMessage;
    }

    /// <summary>利用者に結果を知らせ、起動時の例外をトレイの配送へ返さない。</summary>
    public void Open()
    {
        if (!_exists(_path))
        {
            _show("設定画面 (Snapwell.Settings.exe) が見つかりません。Snapwell と同じフォルダーに置いてください。");
            return;
        }

        try
        {
            _start(new(_path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_path)! });
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or
            InvalidOperationException or SecurityException)
        {
            try
            {
                _log.Error(exception);
            }
            catch (Exception)
            {
                // 記録が失敗しても、起動失敗の表示は行う。
            }

            _show("設定画面を起動できませんでした。");
        }
    }

    private static void ShowMessage(string message) => PInvoke.MessageBox(default, message, "Snapwell",
        MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONINFORMATION);
}
