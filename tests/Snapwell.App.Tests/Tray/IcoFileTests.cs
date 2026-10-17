using System.Buffers.Binary;
using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class IcoFileTests
{
    [Fact]
    public void ParsesFrameSizesAndData()
    {
        var file = Build((16, [1, 2, 3]), (0, [9, 9]));
        var frames = IcoFile.Parse(file);
        Assert.Equal([16, 256], frames.Select(frame => frame.Size));
        Assert.Equal<byte>([1, 2, 3], frames[0].Data.ToArray());
        Assert.Equal<byte>([9, 9], frames[1].Data.ToArray());
    }

    [Fact]
    public void ParsesTheShippedAppIcon()
    {
        var path = Path.Combine(FindAssets(), "snapwell.ico");
        Assert.Equal([256, 48, 32, 24, 20, 16], IcoFile.Parse(File.ReadAllBytes(path)).Select(frame => frame.Size));
    }

    [Fact]
    public void ShippedTrayIconsHaveEveryDpiSize()
    {
        foreach (var path in Directory.GetFiles(FindAssets(), "tray-*.ico"))
        {
            Assert.Equal([16, 20, 24, 32], IcoFile.Parse(File.ReadAllBytes(path)).Select(frame => frame.Size).Order());
        }
    }

    [Fact]
    public void RejectsBadHeader() =>
        Assert.Throws<InvalidDataException>(() => IcoFile.Parse(new byte[] { 0, 0, 2, 0, 0, 0 }));

    [Fact]
    public void RejectsFrameOutsideTheFile()
    {
        var file = Build((16, [1, 2, 3]));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(6 + 8), 100);
        Assert.Throws<InvalidDataException>(() => IcoFile.Parse(file));
    }

    private static byte[] Build(params (int Size, byte[] Data)[] frames)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);
        var offset = 6 + (16 * frames.Length);
        foreach (var (size, data) in frames)
        {
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)data.Length);
            writer.Write((uint)offset);
            offset += data.Length;
        }

        foreach (var (_, data) in frames)
        {
            writer.Write(data);
        }

        return stream.ToArray();
    }

    private static string FindAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Snapwell.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("ルートが無い。"), "assets", "icons");
    }
}
