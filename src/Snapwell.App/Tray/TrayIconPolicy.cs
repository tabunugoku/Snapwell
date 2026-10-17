using Snapwell.App.Lifecycle;

namespace Snapwell.App.Tray;

/// <summary>トレイのアイコンの種類。形でも区別する (有効=塗り、一時停止=薄く中空、注意=「!」の丸)。</summary>
public enum TrayIconKind
{
    Active,
    Paused,
    Attention,
}

/// <summary>トレイメニューの表示と選択可否。</summary>
internal sealed record TrayMenuItem(uint Id, string Text, bool Enabled = true, bool Checked = false);

/// <summary>状態・タスクバーの明暗・DPI からトレイのアイコンを選ぶ判断。Win32 に触れない。</summary>
internal static class TrayIconPolicy
{
    /// <summary>設定の直後に整列を置き、一時停止中の整列を無効にする。</summary>
    public static IReadOnlyList<TrayMenuItem> MenuItems(TrayState state)
    {
        var items = new List<TrayMenuItem>
        {
            new(5, "設定を開く"),
            new(6, "現在のモニターを整列", state.Enabled),
            new(1, state.Enabled ? "一時停止" : "再開"),
            new(2, "Windows 標準のスナップを止める", Checked: state.DisableStandardSnap),
            new(3, "標準の動作に戻す"),
        };
        if (state.Status is { } status)
        {
            items.Add(new(0, status, Enabled: false));
        }

        items.Add(new(4, "終了"));
        return items;
    }

    /// <summary>注意 (Status あり) を最優先し、次に一時停止中、それ以外は有効。</summary>
    public static TrayIconKind KindOf(TrayState state) =>
        state.Status is not null ? TrayIconKind.Attention
        : !state.Enabled ? TrayIconKind.Paused
        : TrayIconKind.Active;

    /// <summary>埋め込みリソースの名前。assets/icons の tray-*.ico に対応する。</summary>
    public static string ResourceName(TrayIconKind kind, bool lightTaskbar) =>
        $"tray/tray-{kind.ToString().ToLowerInvariant()}-{(lightTaskbar ? "light" : "dark")}.ico";

    /// <summary>ホバーの文言。色だけに頼らず、状態を文字でも伝える。</summary>
    public static string Tip(TrayState state) => KindOf(state) switch
    {
        TrayIconKind.Attention => $"Snapwell: {state.Status}",
        TrayIconKind.Paused => "Snapwell (一時停止中)",
        _ => "Snapwell",
    };

    /// <summary>
    /// 要求されたサイズに最も近いフレームを選ぶ。同じ近さなら大きいほう (縮小のほうが拡大より崩れない)。
    /// </summary>
    public static int PickFrameSize(int requested, IReadOnlyList<int> available)
    {
        if (available.Count == 0)
        {
            throw new ArgumentException("フレームが無い。", nameof(available));
        }

        var best = available[0];
        foreach (var size in available)
        {
            var distance = Math.Abs(size - requested);
            var bestDistance = Math.Abs(best - requested);
            if (distance < bestDistance || (distance == bestDistance && size > best))
            {
                best = size;
            }
        }

        return best;
    }
}
