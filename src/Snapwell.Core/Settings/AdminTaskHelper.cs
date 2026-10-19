namespace Snapwell.Core.Settings;

/// <summary>昇格ヘルパーの厳密な引数解析と、OS に触る前の検証。</summary>
public static class AdminTaskHelper
{
    /// <summary>専用の引数だけを認め、任意の実行パスは受け取らない。</summary>
    public static bool TryParse(string[] args, out bool enabled, out string userSid)
    {
        enabled = false;
        userSid = string.Empty;
        if (args.Length != 4 || args[0] != "--set-admin-task" || args[1] is not ("on" or "off") ||
            args[2] != "--for-sid" || !IsSid(args[3]))
        {
            return false;
        }

        enabled = args[1] == "on";
        userSid = args[3];
        return true;
    }

    internal static bool IsSid(string value)
    {
        var parts = value.Split('-');
        return parts.Length is >= 4 and <= 18 && parts[0] == "S" && parts[1] == "1" &&
            parts.Skip(2).All(part => part.Length > 0 && part.All(char.IsAsciiDigit)) &&
            ulong.TryParse(parts[2], out var authority) && authority <= 0xFFFFFFFFFFFF &&
            parts.Skip(3).All(part => uint.TryParse(part, out _));
    }

    /// <summary>SID と場所の検証後だけ、偽に差し替えられるタスクサービスへ渡す。</summary>
    public static int Execute(string[] args, string currentSid, string executablePath,
        Func<TaskExecutablePath, bool> isProtected, IAdminTaskService service,
        Func<string, string>? normalizePath = null)
    {
        if (!TryParse(args, out var enabled, out var requestedSid))
        {
            return 1;
        }

        if (!string.Equals(requestedSid, currentSid, StringComparison.Ordinal))
        {
            return 2;
        }

        try
        {
            if (enabled)
            {
                var path = TaskExecutablePath.Create(executablePath, normalizePath);
                if (!path.IsLiteral)
                {
                    return 4;
                }

                if (!isProtected(path))
                {
                    return 3;
                }

                service.Register(path, currentSid);
            }
            else
            {
                service.Delete();
            }

            return 0;
        }
        catch (Exception)
        {
            // 専用プロセスの境界で失敗コードへ変換し、常駐部分には進めない。
            return 1;
        }
    }
}
