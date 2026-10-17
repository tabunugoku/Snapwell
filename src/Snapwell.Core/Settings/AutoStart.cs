namespace Snapwell.Core.Settings;

/// <summary>サインイン時の自動起動に使うレジストリ値の境界。</summary>
public interface IRunKey
{
    /// <summary>値が無い場合は null を返す。</summary>
    string? Read(string name);

    /// <summary>指定した名前の値を置き換える。</summary>
    void Write(string name, string value);

    /// <summary>指定した名前の値を削除する。</summary>
    void Delete(string name);
}

/// <summary>レジストリだけを正とする自動起動の状態。</summary>
public enum AutoStartState
{
    Off, On, Elsewhere,
}

/// <summary>現在の配布フォルダーと自動起動の登録を比較し、利用者の操作だけを反映する。</summary>
public sealed class AutoStart(IRunKey runKey, string executablePath)
{
    public const string ValueName = "Snapwell";
    private readonly string _executablePath = Path.GetFullPath(executablePath);

    /// <summary>引用符と前後の空白、大文字小文字の違いを無視して登録先を判定する。</summary>
    public AutoStartState GetState()
    {
        var value = runKey.Read(ValueName);
        if (value is null)
        {
            return AutoStartState.Off;
        }

        var path = value.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            path = path[1..^1].Trim();
        }

        return string.Equals(path, _executablePath, StringComparison.OrdinalIgnoreCase)
            ? AutoStartState.On : AutoStartState.Elsewhere;
    }

    /// <summary>オンなら現在のパスで登録し、オフなら既存の値だけを消す。</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            runKey.Write(ValueName, $"\"{_executablePath}\"");
        }
        else if (runKey.Read(ValueName) is not null)
        {
            runKey.Delete(ValueName);
        }
    }
}
