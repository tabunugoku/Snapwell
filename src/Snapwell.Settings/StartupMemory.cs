using Snapwell.Core.Settings;

namespace Snapwell.Settings;

/// <summary>Run への復元情報を同期対象外の端末内ファイルへ保存する。</summary>
internal sealed class StartupMemory : IStartupMemory
{
    private readonly StartupBackupStore _store = new(new BackupFile());

    /// <summary>壊れた退避情報は上書きせず、操作を止める。</summary>
    public StartupRunBackup? Read() => _store.Read();

    /// <summary>Run を変更する前に退避し、復元成功後だけ削除する。</summary>
    public void Write(StartupRunBackup? value) => _store.Write(value);

    private sealed class BackupFile : IStartupBackupFile
    {
        private readonly string _path = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Snapwell", "startup-run.txt");

        /// <summary>元の登録がない場合は null を返す。</summary>
        public string? Read()
        {
            try
            {
                return File.ReadAllText(_path);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

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
}
