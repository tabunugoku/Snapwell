using System.Globalization;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Overlay;

/// <summary>開いた時点の一覧の文言。範囲のドラッグ中にカタログを参照しない。</summary>
public sealed record LauncherWindowListText(string Selected, string NoneSelected, string Heading,
    string Previous, string Next, string Main, string Stack, string Hint, IReadOnlyList<string> ArrangeLabels)
{
    /// <summary>表示を始める時点の言語で一覧の全文言を取得する。</summary>
    public static LauncherWindowListText Create() => new(
        UiText.Current.Get("LauncherUi.Selected"), UiText.Current.Get("LauncherUi.NoneSelected"),
        UiText.Current.Get("LauncherUi.Heading"), UiText.Current.Get("LauncherUi.Previous"),
        UiText.Current.Get("LauncherUi.Next"), UiText.Current.Get("LauncherUi.Main"),
        UiText.Current.Get("LauncherUi.Stack"), UiText.Current.Get("LauncherUi.WindowHint"),
        [UiText.Current.Get("LauncherUi.Arrange.Grid"), UiText.Current.Get("LauncherUi.Arrange.Columns"),
         UiText.Current.Get("LauncherUi.Arrange.Rows"), UiText.Current.Get("LauncherUi.Arrange.MainAndStack")]);

    /// <summary>選択数を、取得済みのテンプレートへ埋め込む。</summary>
    public string SelectionCount(int count) => count == 0 ? NoneSelected : Selected.Replace("{count}",
        count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
