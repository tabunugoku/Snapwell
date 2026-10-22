using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Localization;
using Snapwell.Platform.Startup;
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
    private readonly IUserProcessLauncher _userProcess;

    internal SettingsLauncher(IAppLog log, string? baseDirectory = null, Func<string, bool>? exists = null,
        Action<ProcessStartInfo>? start = null, Action<string>? show = null, IUserProcessLauncher? userProcess = null)
    {
        _log = log;
        _path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Snapwell.Settings.exe");
        _exists = exists ?? File.Exists;
        _start = start ?? (info => { using var process = Process.Start(info); });
        _show = show ?? ShowMessage;
        _userProcess = userProcess ?? new UserProcessLauncher();
    }

    /// <summary>利用者に結果を知らせ、起動時の例外をトレイの配送へ返さない。</summary>
    public void Open() => Open([]);

    /// <summary>引数をシェルに解釈させず、設定画面へ渡す。</summary>
    public void Open(params string[] arguments)
    {
        if (!_exists(_path))
        {
            _show(UiText.Current.Get("Status.SettingsMissing"));
            return;
        }

        try
        {
            var info = new ProcessStartInfo(_path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(_path)!,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            if (_userProcess.IsElevated)
            {
                _userProcess.StartUnelevated(info);
            }
            else
            {
                _start(info);
            }
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

            _show(UiText.Current.Get("Status.SettingsLaunchFailure"));
        }
    }

    private static void ShowMessage(string message) => PInvoke.MessageBox(default, message, "Snapwell",
        MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONINFORMATION);
}
