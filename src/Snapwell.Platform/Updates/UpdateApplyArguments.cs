using System.Diagnostics;
using System.Globalization;

namespace Snapwell.Platform.Updates;

/// <summary>設定画面と進行役で共有する更新モードの引数。</summary>
public static class UpdateApplyArguments
{
    public const string Apply = "--apply-update";
    public const string Elevated = "--apply-update-elevated";
    public const string ForSid = "--for-sid";
    public const string Manifest = "--manifest";
    public const string Signature = "--sig";
    public const string Zip = "--zip";
    public const string WaitPid = "--wait-pid";

    /// <summary>空白を含むパスも ArgumentList で安全に渡す通常権限の起動情報を作る。</summary>
    public static ProcessStartInfo Create(string executable, string sid, string manifest, string signature,
        string zip, int waitPid)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in new[] { Apply, ForSid, sid, Manifest, manifest, Signature, signature,
            Zip, zip, WaitPid, waitPid.ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
