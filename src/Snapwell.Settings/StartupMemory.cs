using Snapwell.Core.Settings;

namespace Snapwell.Settings;

/// <summary>Run への復元情報を同期対象外の端末内ファイルへ保存する。</summary>
internal sealed class StartupMemory : IStartupMemory
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "Snapwell", "startup-run.txt");

    /// <summary>元の登録がない場合は null を返す。</summary>
    public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;

    /// <summary>昇格処理より先に復元情報を原子的に置き換える。</summary>
    public void Write(string? value)
    {
        if (value is null)
        {
            File.Delete(_path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, value);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
