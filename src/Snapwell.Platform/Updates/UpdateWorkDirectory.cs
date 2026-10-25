using System.Security.AccessControl;
using System.Security.Principal;

namespace Snapwell.Platform.Updates;

/// <summary>ヘルパーだけが書き込める新しい作業フォルダーを所有する。</summary>
public sealed class UpdateWorkDirectory : IDisposable
{
    /// <summary>空で新規作成された作業フォルダー。</summary>
    public string Path { get; }

    /// <summary>巻き戻しが不完全な場合はジャーナルと展開先を残す。</summary>
    public bool Preserve { get; set; }

    /// <summary>保護されたインストール先では、利用者に書かせない ProgramData 配下を使う。</summary>
    public UpdateWorkDirectory(bool isProtected)
    {
        if (isProtected)
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (var segment in new[] { "Snapwell", "update", Guid.NewGuid().ToString("N") })
            {
                root = System.IO.Path.Combine(root, segment);
                var directory = new DirectoryInfo(root);
                directory.Create(ProtectedSecurity());
                ValidateProtected(directory);
            }
            Path = root;
        }
        else
        {
            Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Snapwell", "update", "work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
    }

    /// <summary>完了した作業フォルダーだけを削除する。</summary>
    public void Dispose()
    {
        if (Preserve) { return; }
        try { Directory.Delete(Path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 後始末の失敗で、完了した差し替えの結果を変えない。
        }
    }

    private static DirectorySecurity ProtectedSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(administrators);
        foreach (var sid in new[] { administrators, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
        {
            security.AddAccessRule(new(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        return security;
    }

    private static void ValidateProtected(DirectoryInfo directory)
    {
        var security = directory.GetAccessControl();
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        var trustedOwner = descriptor.Owner?.Value is "S-1-5-18" or "S-1-5-32-544";
        // 先に作られた利用者所有の親や再解析ポイントは採用しない。
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || !trustedOwner
            || !security.AreAccessRulesProtected || descriptor.DiscretionaryAcl is not { } dacl
            || dacl.Cast<GenericAce>().Any(ace => ace is not CommonAce common || common.IsCallback
                || common.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544")))
        {
            // i18n-keep: 画面に出さない開発者向けの例外。
            throw new UnauthorizedAccessException("The update work directory is not protected.");
        }
    }
}
