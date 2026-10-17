using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>ネイティブメニューの選択後の整列イベントを検証する。</summary>
public sealed class TrayArrangementTests
{
    /// <summary>有効な整列項目の選択を一度だけ通知する。</summary>
    [Fact]
    public void SelectingArrangementRaisesExactlyOneEvent()
    {
        var tray = new TrayIcon(new NoLog());
        var calls = 0;
        tray.ArrangeRequested += () => calls++;

        tray.SelectMenuItem(6);

        Assert.Equal(1, calls);
    }

    /// <summary>表示後の一時停止や削除で選択を無効にし、再開後は選択を受ける。</summary>
    [Fact]
    public void SelectionRechecksPauseAndRemovalAfterMenuWasOpened()
    {
        var tray = new TrayIcon(new NoLog());
        var calls = 0;
        tray.ArrangeRequested += () => calls++;
        tray.Update(new(false, false));
        tray.SelectMenuItem(6);
        Assert.Equal(0, calls);
        tray.Update(new(true, false));
        tray.SelectMenuItem(6);
        Assert.Equal(1, calls);
        tray.Remove();
        tray.SelectMenuItem(6);
        Assert.Equal(1, calls);
    }

    private sealed class NoLog : IAppLog
    {
        public void Error(Exception exception) => throw new InvalidOperationException("Unexpected log", exception);
    }
}
