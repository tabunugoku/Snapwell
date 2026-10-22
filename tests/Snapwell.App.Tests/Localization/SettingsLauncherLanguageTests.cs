using System.Diagnostics;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Localization;
using Snapwell.Platform.Startup;

namespace Snapwell.App.Tests.Localization;

/// <summary>設定画面の起動失敗を、表示時の言語で知らせる。</summary>
[Collection("UI language")]
public sealed class SettingsLauncherLanguageTests
{
    /// <summary>設定画面の欠落と起動失敗を英語にし、例外の本文は表示しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchFailureUsesCurrentLanguageAndHidesExceptionDetails(bool exists)
    {
        var previous = UiText.Current.Language;
        try
        {
            string? message = null;
            var launcher = new SettingsLauncher(new NoLog(), exists: _ => exists,
                start: _ => throw new IOException("private path"), show: value => message = value,
                userProcess: new UserProcess());
            UiText.Use(UiLanguage.English);
            launcher.Open();
            Assert.Equal(exists ? "Couldn't open settings." :
                "Can't find Snapwell.Settings.exe. Place it in the same folder as Snapwell.", message);
        }
        finally { UiText.Use(previous); }
    }

    private sealed class NoLog : IAppLog
    {
        /// <summary>実際のログには書き込まない。</summary>
        public void Error(Exception exception) { }
    }

    private sealed class UserProcess : IUserProcessLauncher
    {
        public bool IsElevated => false;
        /// <summary>通常権限の試験で昇格解除の経路を呼ばない。</summary>
        public void StartUnelevated(ProcessStartInfo info) => throw new InvalidOperationException();
    }
}
