namespace Snapwell.Core.Settings;

/// <summary>一度だけ正規化し、検査とタスクの実行先で共有するパス。</summary>
public sealed class TaskExecutablePath
{
    private TaskExecutablePath(string value) => Value = value;

    /// <summary>正規化後の実行パス。</summary>
    public string Value { get; }

    /// <summary>Task Scheduler が環境変数として展開する文字を含まないかを返す。</summary>
    public bool IsLiteral => !Value.Contains('%');

    /// <summary>短い名前の展開も済ませた同じ文字列を、以降の検査と登録に渡す。</summary>
    public static TaskExecutablePath Create(string path, Func<string, string>? normalizePath = null) =>
        new((normalizePath ?? Path.GetFullPath)(path));
}
