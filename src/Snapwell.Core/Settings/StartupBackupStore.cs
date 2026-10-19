using System.Text;
using System.Text.Json;

namespace Snapwell.Core.Settings;

/// <summary>端末内の退避ファイルを偽に差し替える境界。</summary>
public interface IStartupBackupFile
{
    /// <summary>存在しない場合だけ null を返す。読み取り不能は例外にする。</summary>
    string? Read();

    /// <summary>原子的に置き換え、null のときだけ削除する。</summary>
    void Write(string? content);
}

/// <summary>復元待ちと元の未登録を区別し、壊れた退避を上書きさせない。</summary>
public sealed class StartupBackupStore(IStartupBackupFile file) : IStartupMemory
{
    /// <summary>既知の形式だけを読み、解析不能な退避は失敗にする。</summary>
    public StartupRunBackup? Read()
    {
        var content = file.Read();
        if (content is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out var version) &&
                version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1 &&
                root.TryGetProperty("command", out var command) &&
                command.ValueKind is JsonValueKind.String or JsonValueKind.Null &&
                root.EnumerateObject().Count() == 2)
            {
                return new(command.GetString());
            }
        }
        catch (JsonException exception)
        {
            throw new IOException("元の自動起動の退避情報を読み取れない。", exception);
        }

        throw new IOException("元の自動起動の退避情報の形式が不正。");
    }

    /// <summary>未登録だった場合も有効な記録として書き、復元完了後だけ削除する。</summary>
    public void Write(StartupRunBackup? value)
    {
        if (value is null)
        {
            file.Write(null);
            return;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("command", value.Command);
            writer.WriteEndObject();
        }

        file.Write(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
