using System.Globalization;
using Snapwell.Core.Settings;

namespace Snapwell.App.Lifecycle;

/// <summary>専用モードの検証済み引数。インストール先や現在の版は受け取らない。</summary>
internal sealed record UpdateCommandLine(bool Elevated, string Sid, string Manifest, string Signature,
    string Zip, int? WaitPid)
{
    internal static bool TryParse(string[] args, out UpdateCommandLine? command)
    {
        command = null;
        if (args.Length is not (9 or 11) || args[0] is not ("--apply-update" or "--apply-update-elevated")
            || args[1] != "--for-sid" || args[3] != "--manifest" || args[5] != "--sig" || args[7] != "--zip")
        {
            return false;
        }
        // SID の受理範囲を既存の登録ヘルパーとそろえる。
        if (!AdminTaskHelper.TryParse(["--set-admin-task", "on", "--for-sid", args[2]], out _, out _)
            || !ValidPath(args[4]) || !ValidPath(args[6]) || !ValidPath(args[8])) { return false; }
        int? pid = null;
        if (args.Length == 11)
        {
            if (args[9] != "--wait-pid" || !int.TryParse(args[10], NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) { return false; }
            pid = parsed;
        }
        command = new(args[0] == "--apply-update-elevated", args[2], args[4], args[6], args[8], pid);
        return true;
    }

    internal string HelperArguments => "--apply-update-elevated --for-sid " + Sid
        + " --manifest " + Quote(Manifest) + " --sig " + Quote(Signature) + " --zip " + Quote(Zip);

    private static bool ValidPath(string path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && path.IndexOfAny(['\0', '"']) < 0;

    // Windows のコマンドラインでは、閉じる引用符直前のバックスラッシュを二重にする。
    private static string Quote(string value) => "\"" + value
        + new string('\\', value.Length - value.TrimEnd('\\').Length) + "\"";
}
