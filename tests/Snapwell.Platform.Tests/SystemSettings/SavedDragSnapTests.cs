using Snapwell.Platform.SystemSettings;

namespace Snapwell.Platform.Tests.SystemSettings;

/// <summary>退避値の表示用読み取りは OS と元ファイルを変更しない。</summary>
public sealed class SavedDragSnapTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-saved-snap-" + Guid.NewGuid());

    /// <summary>対応する版だけを返し、破損・欠落・未来の版もそのまま残す。</summary>
    [Theory]
    [InlineData("{\"Version\":1,\"DragSnapEnabled\":true}", true)]
    [InlineData("{\"Version\":1,\"DragSnapEnabled\":false}", false)]
    [InlineData("{", null)]
    [InlineData("{\"Version\":2,\"DragSnapEnabled\":true}", null)]
    [InlineData("{\"Version\":1}", null)]
    [InlineData("{\"DragSnapEnabled\":true}", null)]
    [InlineData("null", null)]
    public void ReadPreservesFile(string json, bool? expected)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "os-state.json");
        File.WriteAllText(path, json);

        Assert.Equal(expected, SnapSettingsGuard.ReadSavedDragSnap(path));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    /// <summary>本体の一時ファイルや隔離済みファイルを退避値として使わない。</summary>
    [Fact]
    public void MissingFileIgnoresTemporaryAndBrokenFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "os-state.json");
        const string json = "{\"Version\":1,\"DragSnapEnabled\":true}";
        File.WriteAllText(path + ".pending.tmp", json);
        File.WriteAllText(path + ".broken", json);

        Assert.Null(SnapSettingsGuard.ReadSavedDragSnap(path));
        Assert.False(File.Exists(path));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    /// <summary>共有違反の間は値を返さず、ロックが解ければ読み取れる。</summary>
    [Fact]
    public void LockedFileReturnsNull()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "os-state.json");
        File.WriteAllText(path, "{\"Version\":1,\"DragSnapEnabled\":false}");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(SnapSettingsGuard.ReadSavedDragSnap(path));
        }

        Assert.Equal((bool?)false, SnapSettingsGuard.ReadSavedDragSnap(path));
    }

    /// <summary>不正なパスも呼び出し元へ例外を返さない。</summary>
    [Fact]
    public void InvalidPathReturnsNull() => Assert.Null(SnapSettingsGuard.ReadSavedDragSnap("\0"));

    /// <summary>このテストだけの退避値を片付ける。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
