using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;
using Snapwell.Platform.Monitors;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Snapwell.App.Tray.Menu;

/// <summary>トレイの位置の取得を偽に置き換えられる境界。</summary>
internal interface ITrayMenuAnchor
{
    PixelRect Read();
}

/// <summary>Shell のアイコン矩形を優先し、取得できなければカーソルを使う。</summary>
internal sealed class TrayMenuAnchor(HWND owner) : ITrayMenuAnchor
{
    public unsafe PixelRect Read()
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            hWnd = owner,
            uID = TrayIcon.IconId,
        };
        if (PInvoke.Shell_NotifyIconGetRect(in identifier, out var rectangle).Succeeded &&
            rectangle.right > rectangle.left && rectangle.bottom > rectangle.top)
        {
            return new(rectangle.left, rectangle.top, rectangle.right - rectangle.left, rectangle.bottom - rectangle.top);
        }

        if (!PInvoke.GetCursorPos(out var point))
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            throw new InvalidOperationException("トレイとカーソルの位置を取得できない。");
        }

        return new(point.X, point.Y, 1, 1);
    }
}

/// <summary>アイコンのあるモニターの拡大率と作業領域だけで配置する。</summary>
internal static class TrayMenuPlacement
{
    internal static (TrayMenuLayout Layout, int MonitorCount) Read(ITrayMenuAnchor anchor,
        IMonitorSource monitors, bool hasStatus, int itemCount = 4)
    {
        var rectangle = anchor.Read();
        var screens = monitors.GetMonitors();
        var monitor = screens.FirstOrDefault(screen => screen.Bounds.Contains(rectangle.Center)) ??
            screens.MinBy(screen => Distance(screen.Bounds, rectangle.Center)) ??
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            throw new InvalidOperationException("モニターがない。");
        var edge = TrayMenuLayout.DetectEdge(monitor.Bounds, monitor.WorkArea, rectangle);
        return (TrayMenuLayout.Create(rectangle, monitor.WorkArea, edge, monitor.DpiX / 96.0,
            hasStatus, itemCount), screens.Count);
    }

    private static double Distance(PixelRect bounds, Vec2 point)
    {
        var x = Math.Max(0, Math.Max(bounds.X - point.X, point.X - bounds.Right));
        var y = Math.Max(0, Math.Max(bounds.Y - point.Y, point.Y - bounds.Bottom));
        return x * x + y * y;
    }
}
