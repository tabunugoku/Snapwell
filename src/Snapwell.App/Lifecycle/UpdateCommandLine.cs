using System.Globalization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>専用モードの検証済み引数。インストール先や現在の版は受け取らない。</summary>
internal sealed record UpdateCommandLine(bool Elevated, string Sid, string Manifest, string Signature,
    string Zip, int? WaitPid)
{
    internal static bool TryParse(string[] args, out UpdateCommandLine? command)
    {
        command = null;
        if (args.Length is not (9 or 11) || args[0] is not (UpdateApplyArguments.Apply or UpdateApplyArguments.Elevated)
            || args[1] != UpdateApplyArguments.ForSid || args[3] != UpdateApplyArguments.Manifest
            || args[5] != UpdateApplyArguments.Signature || args[7] != UpdateApplyArguments.Zip)
        {
            return false;
        }
        // SID の受理範囲を既存の登録ヘルパーとそろえる。
        if (!AdminTaskHelper.TryParse(["--set-admin-task", "on", UpdateApplyArguments.ForSid, args[2]], out _, out _)
            || !ValidPath(args[4]) || !ValidPath(args[6]) || !ValidPath(args[8])) { return false; }
        int? pid = null;
        if (args.Length == 11)
        {
            if (args[9] != UpdateApplyArguments.WaitPid || !int.TryParse(args[10], NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) { return false; }
            pid = parsed;
        }
        command = new(args[0] == UpdateApplyArguments.Elevated, args[2], args[4], args[6], args[8], pid);
        return true;
    }

    internal string HelperArguments => UpdateApplyArguments.Elevated + " " + UpdateApplyArguments.ForSid + " " + Sid
        + " " + UpdateApplyArguments.Manifest + " " + Quote(Manifest)
        + " " + UpdateApplyArguments.Signature + " " + Quote(Signature)
        + " " + UpdateApplyArguments.Zip + " " + Quote(Zip);

    private static bool ValidPath(string path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && path.IndexOfAny(['\0', '"']) < 0;

    // Windows のコマンドラインでは、閉じる引用符直前のバックスラッシュを二重にする。
    private static string Quote(string value) => "\"" + value
        + new string('\\', value.Length - value.TrimEnd('\\').Length) + "\"";
}
