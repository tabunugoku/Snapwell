using Snapwell.Core.Geometry;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tray;

/// <summary>既存のトレイのコマンド番号に対応する操作。</summary>
public enum TrayMenuCommand : uint { Toggle = 1, Exit = 4, Settings = 5, Arrange = 6, Launcher = 7, Update = 8 }

/// <summary>メニュー項目の名前と選択可否。</summary>
public sealed record TrayMenuEntry(TrayMenuCommand Command, string Name, bool Enabled);

/// <summary>Win32 に依存しないトレイメニューの項目と入力の判断。</summary>
public static class TrayMenuModel
{
    /// <summary>通常の四項目と、案内する版がある場合だけ先頭の更新項目を返す。</summary>
    public static IReadOnlyList<TrayMenuEntry> Create(bool enabled, bool launcherAvailable = false,
        string? updateVersion = null)
    {
        List<TrayMenuEntry> items =
        [
            new(TrayMenuCommand.Arrange, UiText.Current.Get("Tray.Arrange"), enabled),
            new(TrayMenuCommand.Launcher, UiText.Current.Get("Tray.Launcher"), enabled && launcherAvailable),
            new(TrayMenuCommand.Settings, UiText.Current.Get("Tray.Settings"), true),
            new(TrayMenuCommand.Exit, UiText.Current.Get("Tray.Exit"), true),
        ];
        if (updateVersion is not null)
        {
            items.Insert(0, new(TrayMenuCommand.Update,
                UiText.Current.Format("Tray.UpdateAvailable", ("version", updateVersion)), true));
        }
        return items.AsReadOnly();
    }

    /// <summary>無効な項目を飛ばして循環する。先頭の 0 はヘッダーのスイッチ。</summary>
    public static int Move(IReadOnlyList<TrayMenuEntry> items, int selected, int direction)
    {
        var count = items.Count + 1;
        if (selected < 0 || selected >= count)
        {
            selected = direction < 0 ? 0 : -1;
        }

        for (var step = 0; step < count; step++)
        {
            selected = (selected + (direction < 0 ? -1 : 1) + count) % count;
            if (selected == 0 || items[selected - 1].Enabled)
            {
                return selected;
            }
        }

        return -1;
    }

    /// <summary>スイッチまたは有効な項目だけを操作へ変換する。</summary>
    public static TrayMenuCommand? CommandAt(IReadOnlyList<TrayMenuEntry> items, int index) =>
        index == 0 ? TrayMenuCommand.Toggle
        : index > 0 && index <= items.Count && items[index - 1].Enabled ? items[index - 1].Command : null;

    /// <summary>描画と同じ矩形で判定し、区切り線や無効な項目を除く。</summary>
    public static int HitTest(TrayMenuLayout layout, IReadOnlyList<TrayMenuEntry> items, Vec2 point)
    {
        if (!layout.Panel.Contains(point)) { return -1; }
        if (layout.Toggle.Contains(point))
        {
            return 0;
        }

        if (!layout.ItemsViewport.Contains(point)) { return -1; }
        // 項目数が増えた直後はレイアウトの行数とずれうるので、短い方で回す。
        for (var index = 0; index < Math.Min(items.Count, layout.Rows.Count); index++)
        {
            if (items[index].Enabled && layout.Rows[index].Contains(point))
            {
                return index + 1;
            }
        }

        return -1;
    }
}
