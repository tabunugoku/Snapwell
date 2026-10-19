using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class ProtectedLocationTests
{
    private const string Administrators = "S-1-5-32-544";

    [Theory]
    [InlineData(@"\\server\share\Snapwell.exe", DriveType.Fixed)]
    [InlineData(@"\\?\UNC\server\share\Snapwell.exe", DriveType.Fixed)]
    [InlineData(@"Z:\Snapwell.exe", DriveType.Network)]
    [InlineData(@"E:\Snapwell.exe", DriveType.Removable)]
    [InlineData(@"E:\Snapwell.exe", DriveType.CDRom)]
    [InlineData(@"E:\Snapwell.exe", DriveType.Ram)]
    [InlineData(@"E:\Snapwell.exe", DriveType.Unknown)]
    [InlineData(@"E:\Snapwell.exe", DriveType.NoRootDirectory)]
    public void NonLocalFixedDiskPathsAreRejectedBeforeReadingAcl(string path, DriveType drive)
    {
        var source = new FakeLocation { Drive = drive };
        Assert.False(ProtectedLocation.IsProtected(path, source));
        Assert.False(source.Read);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReparsePointsAtEveryLevelAreRejectedEvenWithTrustedAcl(int index)
    {
        var source = new FakeLocation();
        source.Entries[index] = source.Entries[index] with { IsReparsePoint = true };
        Assert.False(ProtectedLocation.IsProtected(@"C:\Program Files\Snapwell.exe", source));
    }

    [Fact]
    public void FixedDiskProgramFilesRequiresTrustedAclAtEveryLevel()
    {
        var source = new FakeLocation();
        Assert.True(ProtectedLocation.IsProtected(@"C:\Program Files\Snapwell.exe", source));
        source.Entries[1] = new(false, new(Administrators, true, false, [new("S-1-1-0", 2, true, false)]));
        Assert.False(ProtectedLocation.IsProtected(@"C:\Program Files\Snapwell.exe", source));
    }

    private sealed class FakeLocation : IExecutableLocation
    {
        internal DriveType Drive = DriveType.Fixed;
        internal bool Read;
        internal LocationEntry[] Entries = Enumerable.Range(0, 3)
            .Select(_ => new LocationEntry(false, new(Administrators, true, false, []))).ToArray();
        public DriveType GetDriveType(string path) => Drive;
        public IEnumerable<LocationEntry> ReadHierarchy(string path)
        {
            Read = true;
            return Entries;
        }
    }

    [Fact]
    public void AdministratorOwnedReadOnlyDirectoryAllowsTrustedWriters()
    {
        Assert.True(ProtectedLocation.IsProtected(new(Administrators, true, false,
            [new(Administrators, uint.MaxValue, true, false), new("S-1-5-18", uint.MaxValue, true, false),
                new("S-1-1-0", 0x1200A9, true, false)])));
    }

    [Theory]
    [InlineData("S-1-1-0", 2u)]
    [InlineData("S-1-5-32-545", 4u)]
    [InlineData("S-1-5-11", 0x40u)]
    [InlineData("S-1-5-21-1001", 0x40000u)]
    [InlineData("S-1-3-4", 0x80000u)]
    [InlineData("S-1-5-32-545", 0x40000000u)]
    public void AnyUntrustedWriteAllowIsRejectedEvenWhenDenyAlsoExists(string sid, uint mask)
    {
        Assert.False(ProtectedLocation.IsProtected(new(Administrators, true, false,
            [new(sid, mask, false, false), new(sid, mask, true, false)])));
    }

    [Fact]
    public void UnknownOwnerNullDaclAndUnsupportedAceAreRejected()
    {
        Assert.False(ProtectedLocation.IsProtected(new("S-1-5-21-1001", true, false, [])));
        Assert.False(ProtectedLocation.IsProtected(new(Administrators, false, false, [])));
        Assert.False(ProtectedLocation.IsProtected(new(Administrators, true, true, [])));
    }

    [Fact]
    public void AncestorMayAllowCreationButCannotAllowReplacement()
    {
        var create = new LocationAcl(Administrators, true, false, [new("S-1-5-11", 4, true, false)]);
        Assert.True(ProtectedLocation.IsProtected(create, ancestor: true));
        Assert.False(ProtectedLocation.IsProtected(create));
        Assert.False(ProtectedLocation.IsProtected(create with { Entries = [new("S-1-5-11", 0x40, true, false)] },
            ancestor: true));
        Assert.True(ProtectedLocation.IsProtected(create with { Entries = [new("S-1-5-11", 2, true, true)] }));
    }
}
