using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;

namespace Snapwell.Core.Tray;

/// <summary>既存のトレイのコマンド番号に対応する操作。</summary>
public enum TrayMenuCommand : uint { Toggle = 1, Exit = 4, Settings = 5, Arrange = 6, Launcher = 7 }

/// <summary>メニュー項目の名前と選択可否。</summary>
public sealed record TrayMenuEntry(TrayMenuCommand Command, string Name, bool Enabled,
    IReadOnlyList<string> Keys);

/// <summary>Win32 に依存しないトレイメニューの項目と入力の判断。</summary>
public static class TrayMenuModel
{
    /// <summary>
    /// 有効な割り当てから、トレイにある操作の表示部品だけを返す。整列は対象のモニターが違う
    /// (トレイはカーソル、キーは前面ウィンドウ) ため、同じ操作として表示しない。
    /// </summary>
    public static IReadOnlyDictionary<TrayMenuCommand, IReadOnlyList<string>> Shortcuts(
        IEnumerable<KeyValuePair<WindowCommand, KeyChordSpec>> effective)
    {
        var shortcuts = new Dictionary<TrayMenuCommand, IReadOnlyList<string>>();
        foreach (var (command, chord) in effective)
        {
            TrayMenuCommand? menuCommand = command switch
            {
                WindowCommand.ToggleEnabled => TrayMenuCommand.Toggle,
                WindowCommand.OpenLauncher => TrayMenuCommand.Launcher,
                _ => null,
            };
            if (menuCommand is { } mapped)
            {
                shortcuts[mapped] = HotkeyCatalog.FormatChordParts(chord);
            }
        }

        return shortcuts;
    }

    /// <summary>割り当てを受け取れない操作には、推測したキーを表示しない。</summary>
    public static IReadOnlyList<TrayMenuEntry> Create(bool enabled, bool launcherAvailable = false,
        IReadOnlyDictionary<TrayMenuCommand, IReadOnlyList<string>>? shortcuts = null)
    {
        TrayMenuEntry Item(TrayMenuCommand command, string name, bool available = true) =>
            new(command, name, available, shortcuts?.GetValueOrDefault(command) ?? []);
        return Array.AsReadOnly<TrayMenuEntry>(
        [
            Item(TrayMenuCommand.Arrange, "現在のモニターを整列", enabled),
            Item(TrayMenuCommand.Launcher, "ランチャーを開く", enabled && launcherAvailable),
            Item(TrayMenuCommand.Toggle, enabled ? "一時的に無効化" : "再開"),
            Item(TrayMenuCommand.Settings, "設定を開く"),
            Item(TrayMenuCommand.Exit, "終了（標準の動作に戻す）"),
        ]);
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
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Enabled && layout.Rows[index].Contains(point))
            {
                return index + 1;
            }
        }

        return -1;
    }
}
