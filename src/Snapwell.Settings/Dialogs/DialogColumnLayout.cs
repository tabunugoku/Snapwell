namespace Snapwell.Settings.Dialogs;

/// <summary>ダイアログの列を、非表示・等分するボタン・間隔に分ける。</summary>
internal enum DialogColumnKind
{
    Collapsed,
    Button,
    Spacing,
}

/// <summary>テンプレートの列番号に依存せず、ボタンの列と間隔を判定する。</summary>
internal static class DialogColumnLayout
{
    /// <summary>すべてのボタンの占有列を保護し、表示するボタン間の空列だけを間隔にする。</summary>
    public static DialogColumnKind[] Calculate(int columnCount, int[] columns, bool[] visible)
    {
        var occupied = new bool[columnCount];
        var result = new DialogColumnKind[columnCount];
        for (var index = 0; index < columns.Length; index++)
        {
            occupied[columns[index]] = true;
            if (visible[index]) { result[columns[index]] = DialogColumnKind.Button; }
        }

        var previous = -1;
        for (var column = 0; column < columnCount; column++)
        {
            if (result[column] != DialogColumnKind.Button) { continue; }
            if (previous >= 0 && column > previous + 1 && !occupied[column - 1])
            {
                result[column - 1] = DialogColumnKind.Spacing;
            }
            previous = column;
        }
        return result;
    }
}
