using System.Security.AccessControl;
using Snapwell.Core.Settings;
using Windows.Win32;

namespace Snapwell.Platform.Startup;

/// <summary>exe と親階層の ACL を読み、通常権限からの差し替えが可能な場所を拒否する。</summary>
public static class ExecutableLocation
{
    /// <summary>ACL を読めない場合や reparse point を含む場合も、保護された場所とは扱わない。</summary>
    public static bool IsProtected(string executablePath)
    {
        try
        {
            return ProtectedLocation.IsProtected(executablePath, new LocationSource());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private sealed class LocationSource : IExecutableLocation
    {
        public DriveType GetDriveType(string path) => (DriveType)PInvoke.GetDriveType(Path.GetPathRoot(path)!);

        public IEnumerable<LocationEntry> ReadHierarchy(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists)
            {
                throw new FileNotFoundException();
            }

            // 再解析ポイントの先の ACL は信頼せず、Core に拒否させる。
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                yield return new(true, new("", false, false, []));
                yield break;
            }

            yield return new(false, Read(file.GetAccessControl()));
            for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            {
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    yield return new(true, new("", false, false, []));
                    yield break;
                }

                yield return new(false, Read(parent.GetAccessControl()));
            }
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
