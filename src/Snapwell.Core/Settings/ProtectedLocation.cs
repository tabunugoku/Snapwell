namespace Snapwell.Core.Settings;

/// <summary>ACL の Windows 非依存の判断材料。</summary>
public sealed record LocationAcl(string OwnerSid, bool HasDacl, bool HasUnsupportedAce,
    IReadOnlyList<LocationAce> Entries);

/// <summary>ACL の許可・拒否と、そのオブジェクトに適用されるアクセスマスク。</summary>
public sealed record LocationAce(string Sid, uint Mask, bool Allow, bool InheritOnly);

/// <summary>ファイルまたは親階層の差し替え可能性。</summary>
public sealed record LocationEntry(bool IsReparsePoint, LocationAcl Acl);

/// <summary>ドライブ種別と exe からルートまでの実在する階層を読む境界。</summary>
public interface IExecutableLocation
{
    /// <summary>exe のあるボリュームの種別を返す。</summary>
    DriveType GetDriveType(string path);

    /// <summary>exe、直接の親、以降の祖先をルートまで順に返す。取得失敗は例外にする。</summary>
    IEnumerable<LocationEntry> ReadHierarchy(string path);
}

/// <summary>昇格して実行するファイルの差し替えを防ぐ、保守的な ACL 判定。</summary>
public static class ProtectedLocation
{
    /// <summary>Task Scheduler が実行時に環境変数を展開するパスを拒否する。</summary>
    public static bool IsLiteralTaskPath(string executablePath) => !executablePath.Contains('%');

    /// <summary>ローカル固定ディスクだけを許し、各階層の再解析ポイントと ACL を確認する。</summary>
    public static bool IsProtected(string executablePath, IExecutableLocation source)
    {
        // UNC・デバイス名前空間・相対パスは ACL を読む前に拒否する。
        if (!IsLiteralTaskPath(executablePath) || executablePath.Length < 3 ||
            !char.IsAsciiLetter(executablePath[0]) || executablePath[1] != ':' || executablePath[2] != '\\' ||
            source.GetDriveType(executablePath) != DriveType.Fixed)
        {
            return false;
        }

        var count = 0;
        foreach (var entry in source.ReadHierarchy(executablePath))
        {
            if (entry.IsReparsePoint || !IsProtected(entry.Acl, ancestor: count > 1))
            {
                return false;
            }

            count++;
        }

        return count >= 2;
    }

    // SYSTEM、Administrators、TrustedInstaller 以外の書き込み許可は拒否する。
    private static bool IsTrusted(string sid) => sid is "S-1-5-18" or "S-1-5-32-544" or
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>所有者・DACL が不明な場合も拒否し、拒否 ACE による例外扱いはしない。</summary>
    public static bool IsProtected(LocationAcl acl, bool ancestor = false)
    {
        // 上位フォルダーでは新規作成だけは許すが、削除・ACL 変更・所有権取得は許さない。
        const uint replace = 0x00010000 | 0x00040000 | 0x00080000 | 0x40 | 0x10000000 | 0x40000000;
        const uint write = replace | 0x2 | 0x4 | 0x10 | 0x100;
        return IsTrusted(acl.OwnerSid) && acl.HasDacl && !acl.HasUnsupportedAce &&
            !acl.Entries.Any(ace => ace.Allow && !ace.InheritOnly && !IsTrusted(ace.Sid) &&
                (ace.Mask & (ancestor ? replace : write)) != 0);
    }
}
