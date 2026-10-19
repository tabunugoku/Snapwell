using System.Security.AccessControl;
using Snapwell.Core.Settings;

namespace Snapwell.Platform.Startup;

/// <summary>exe と親階層の ACL を読み、通常権限からの差し替えが可能な場所を拒否する。</summary>
public static class ExecutableLocation
{
    /// <summary>ACL を読めない場合や reparse point を含む場合も、保護された場所とは扱わない。</summary>
    public static bool IsProtected(string executablePath)
    {
        try
        {
            var file = new FileInfo(Path.GetFullPath(executablePath));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !ProtectedLocation.IsProtected(Read(file.GetAccessControl())))
            {
                return false;
            }

            var parent = file.Directory;
            var ancestor = false;
            while (parent is not null)
            {
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !ProtectedLocation.IsProtected(Read(parent.GetAccessControl()), ancestor))
                {
                    return false;
                }

                ancestor = true;
                parent = parent.Parent;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private static LocationAcl Read(FileSystemSecurity security)
    {
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        var entries = new List<LocationAce>();
        var unsupported = false;
        if (descriptor.DiscretionaryAcl is { } dacl)
        {
            foreach (GenericAce ace in dacl)
            {
                if (ace is CommonAce common && !common.IsCallback)
                {
                    entries.Add(new(common.SecurityIdentifier.Value, unchecked((uint)common.AccessMask),
                        common.AceQualifier == AceQualifier.AccessAllowed,
                        (common.AceFlags & AceFlags.InheritOnly) != 0));
                }
                else
                {
                    unsupported = true;
                }
            }
        }

        return new(descriptor.Owner?.Value ?? "", descriptor.DiscretionaryAcl is not null, unsupported, entries);
    }
}
