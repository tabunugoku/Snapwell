using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Settings;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>ネイティブメニューの選択後の整列イベントを検証する。</summary>
public sealed class TrayArrangementTests
{
    /// <summary>選択時の接続・一時停止・削除を確認してランチャー要求を通知する。</summary>
    [Fact]
    public void LauncherSelectionRechecksAvailabilityPauseAndRemoval()
    {
        var tray = new TrayIcon(new NoLog(), SettingsStores.Device());
        var calls = new List<nint>();
        tray.LauncherRequested += (target, _) => calls.Add(target);
        tray.SelectMenuItem(7);
        Assert.Empty(calls);
        tray.Update(new(true, false, LauncherAvailable: true));
        tray.SelectMenuItem(7);
        Assert.Single(calls);
        tray.Update(new(false, false, LauncherAvailable: true));
        tray.SelectMenuItem(7);
        tray.Update(new(true, false));
        tray.SelectMenuItem(7);
        Assert.Single(calls);
        tray.Update(new(true, false, LauncherAvailable: true));
        tray.SelectMenuItem(7);
        Assert.Equal(2, calls.Count);
        tray.Remove();
        tray.SelectMenuItem(7);
        Assert.Equal(2, calls.Count);
    }

    /// <summary>メニューを選んだ時点の ms 時計を要求へ渡す。</summary>
    [Fact]
    public void LauncherSelectionIncludesCurrentTickCount()
    {
        var tray = new TrayIcon(new NoLog(), SettingsStores.Device());
        tray.Update(new(true, false, LauncherAvailable: true));
        var calls = new List<uint>();
        tray.LauncherRequested += (_, timeMs) => calls.Add(timeMs);
        var beforeMs = unchecked((uint)Environment.TickCount);
        tray.SelectMenuItem(7);
        var afterMs = unchecked((uint)Environment.TickCount);
        var selectedMs = Assert.Single(calls);
        Assert.True(PointerOrdering.IsAtOrBefore(beforeMs, selectedMs));
        Assert.True(PointerOrdering.IsAtOrBefore(selectedMs, afterMs));
        tray.Remove();
    }

    /// <summary>有効な整列項目の選択を一度だけ通知する。</summary>
    [Fact]
    public void SelectingArrangementRaisesExactlyOneEvent()
    {
        var tray = new TrayIcon(new NoLog(), SettingsStores.Device());
        var calls = 0;
        tray.ArrangeRequested += () => calls++;

        tray.SelectMenuItem(6);

        Assert.Equal(1, calls);
    }

    /// <summary>表示後の一時停止や削除で選択を無効にし、再開後は選択を受ける。</summary>
    [Fact]
    public void SelectionRechecksPauseAndRemovalAfterMenuWasOpened()
    {
        var tray = new TrayIcon(new NoLog(), SettingsStores.Device());
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
