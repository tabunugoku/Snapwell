using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>展開せずに更新用 zip の拒否条件と境界を確認する。</summary>
public sealed class UpdateZipPolicyTests
{
    /// <summary>通常の配布物を受け入れ、区切りをそろえてファイルだけを返す。</summary>
    [Fact]
    public void ValidArchiveReturnsNormalizedFilesWithoutDirectories()
    {
        UpdateZipEntry[] entries =
        [
            new("Snapwell.exe", 100, false),
            new("Snapwell.Settings.exe", 200, false),
            new("resources/", 0, true),
            new("resources\\ja\\", 0, true),
            new("resources\\ja/strings.dll", 300, false),
            new("resources/en/strings.dll", 300, false),
        ];

        var result = UpdateZipPolicy.Validate(entries);

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "Snapwell.exe", "Snapwell.Settings.exe", "resources/ja/strings.dll",
            "resources/en/strings.dll" }, result.Files);
    }

    /// <summary>絶対パスや Windows 上で危険な名前をファイルでもディレクトリでも拒否する。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/root")]
    [InlineData("\\root")]
    [InlineData("C:")]
    [InlineData("C:/root")]
    [InlineData("C:\\root")]
    [InlineData("C:root")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("//server/share/file")]
    [InlineData("..")]
    [InlineData("../file")]
    [InlineData("a/../file")]
    [InlineData("a\\..\\file")]
    [InlineData("a/..")]
    [InlineData(".")]
    [InlineData("./file")]
    [InlineData("a/./file")]
    [InlineData("a\\.\\file")]
    [InlineData("a/.")]
    [InlineData("a//file")]
    [InlineData("a\\\\file")]
    [InlineData("a/\\file")]
    [InlineData("a\\/file")]
    [InlineData("a//")]
    [InlineData("a\\\\")]
    [InlineData("a\0file")]
    [InlineData("a\u0001file")]
    [InlineData("a\tfile")]
    [InlineData("a\nfile")]
    [InlineData("a\u007ffile")]
    [InlineData("a\u0085file")]
    [InlineData("file:stream")]
    [InlineData("a/file:stream")]
    [InlineData("a.")]
    [InlineData("a./file")]
    [InlineData("a ")]
    [InlineData("a /file")]
    [InlineData("a*file")]
    [InlineData("a?file")]
    [InlineData("a\"file")]
    [InlineData("a<file")]
    [InlineData("a>file")]
    [InlineData("a|file")]
    public void InvalidPathsAreRejected(string name)
    {
        AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new(name, 0, false)]));
        AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new(name, 0, true)]));
    }

    /// <summary>予約名とその拡張子付きの名前をどの階層でも拒否する。</summary>
    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM2")]
    [InlineData("COM3")]
    [InlineData("COM4")]
    [InlineData("COM5")]
    [InlineData("COM6")]
    [InlineData("COM7")]
    [InlineData("COM8")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT2")]
    [InlineData("LPT3")]
    [InlineData("LPT4")]
    [InlineData("LPT5")]
    [InlineData("LPT6")]
    [InlineData("LPT7")]
    [InlineData("LPT8")]
    [InlineData("LPT9")]
    public void ReservedNamesAreRejectedWithExtensionsAndRegardlessOfCase(string name)
    {
        foreach (var path in new[] { name, name.ToLowerInvariant() + ".txt", "folder/" + name + ".txt",
            name.ToLowerInvariant() + ".txt/file", name + " .txt" })
        {
            AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new(path, 0, false)]));
            AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new(path, 0, true)]));
        }
    }

    /// <summary>要素全体が禁止名と一致しない普通の名前を受け入れる。</summary>
    [Theory]
    [InlineData("..hidden")]
    [InlineData("folder/..hidden")]
    [InlineData(".hidden")]
    [InlineData("COM0.txt")]
    [InlineData("COM10.txt")]
    [InlineData("LPT0")]
    [InlineData("LPT10")]
    [InlineData("CONSOLE.txt")]
    [InlineData("日本語/ファイル.txt")]
    public void SafeNamesAreAccepted(string name)
    {
        var result = UpdateZipPolicy.Validate([MainFile(), new(name, 0, false)]);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(new[] { "Snapwell.exe", name }, result.Files);
    }

    /// <summary>ファイルの末尾の区切りを空の要素として拒否する。</summary>
    [Theory]
    [InlineData("folder/")]
    [InlineData("folder\\")]
    public void TrailingSeparatorsRequireDirectoryEntries(string name)
        => AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new(name, 0, false)]));

    /// <summary>重複と親ファイルの衝突を入力順や区切りや大文字小文字によらず拒否する。</summary>
    [Theory]
    [InlineData("a", false, "a", false)]
    [InlineData("a", false, "A", false)]
    [InlineData("a", true, "A/", true)]
    [InlineData("a", false, "A/", true)]
    [InlineData("a/b", false, "A\\B", false)]
    [InlineData("a", false, "a/b", false)]
    [InlineData("a", false, "A\\b\\", true)]
    [InlineData("a/b", false, "A\\B/c", false)]
    public void DuplicateAndConflictingPathsAreRejectedInEitherOrder(
        string first, bool firstIsDirectory, string second, bool secondIsDirectory)
    {
        var firstEntry = new UpdateZipEntry(first, 0, firstIsDirectory);
        var secondEntry = new UpdateZipEntry(second, 0, secondIsDirectory);
        AssertInvalid(UpdateZipPolicy.Validate([firstEntry, secondEntry, MainFile()]));
        AssertInvalid(UpdateZipPolicy.Validate([secondEntry, firstEntry, MainFile()]));
    }

    /// <summary>明示したディレクトリと子ファイルはどちらの順序でも共存できる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitDirectoryAndChildrenAreAccepted(bool reverse)
    {
        UpdateZipEntry[] entries = [MainFile(), new("folder/", 0, true), new("FOLDER/child", 1, false)];
        var result = UpdateZipPolicy.Validate(reverse ? entries.Reverse().ToArray() : entries);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Files.Count);
    }

    /// <summary>トップレベルの本体がファイルとして必要で、大文字小文字は区別しない。</summary>
    [Theory]
    [InlineData("other.exe", false, false)]
    [InlineData("folder/Snapwell.exe", false, false)]
    [InlineData("Snapwell.exe", true, false)]
    [InlineData("Snapwell.exe/", true, false)]
    [InlineData("SNAPWELL.EXE", false, true)]
    public void MainExecutableMustBeATopLevelFile(string name, bool isDirectory, bool expected)
    {
        var result = UpdateZipPolicy.Validate([new(name, 0, isDirectory)]);
        if (expected) { Assert.True(result.IsValid, result.Error); }
        else { AssertInvalid(result); }
    }

    /// <summary>空の一覧と上限を超えるエントリ数を拒否し、5000 個ちょうどは許可する。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void EntryCountBoundariesAreEnforced(int count, bool expected)
    {
        var entries = Enumerable.Range(0, count)
            .Select(index => index == 0 ? MainFile() : new UpdateZipEntry($"folder/{index}", 0, false)).ToArray();
        var result = UpdateZipPolicy.Validate(entries);
        if (expected) { Assert.True(result.IsValid, result.Error); }
        else { AssertInvalid(result); }
    }

    /// <summary>ディレクトリも数の上限に含める。</summary>
    [Fact]
    public void DirectoriesCountTowardsEntryLimit()
    {
        var entries = Enumerable.Range(0, UpdateZipPolicy.MaximumEntries)
            .Select(index => new UpdateZipEntry($"folder{index}/", 0, true)).Prepend(MainFile()).ToArray();
        AssertInvalid(UpdateZipPolicy.Validate(entries));
    }

    /// <summary>負のサイズと単一・合計の上限超過を拒否し、1 GiB ちょうどは許可する。</summary>
    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(long.MinValue, 0, false)]
    [InlineData(1073741825, 0, false)]
    [InlineData(0, 1073741825, false)]
    [InlineData(long.MaxValue, 0, false)]
    [InlineData(1073741824, 1, false)]
    [InlineData(536870912, 536870913, false)]
    [InlineData(0, 0, true)]
    [InlineData(1073741824, 0, true)]
    [InlineData(0, 1073741824, true)]
    [InlineData(536870912, 536870912, true)]
    public void SizeBoundariesAreEnforced(long mainSize, long otherSize, bool expected)
    {
        var result = UpdateZipPolicy.Validate([new("Snapwell.exe", mainSize, false), new("other", otherSize, false)]);
        if (expected) { Assert.True(result.IsValid, result.Error); }
        else { AssertInvalid(result); }
    }

    /// <summary>ディレクトリのメタデータもサイズの検査と合計から除外しない。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1073741825)]
    [InlineData(1073741824)]
    public void DirectorySizesAreValidatedAndCounted(long size)
        => AssertInvalid(UpdateZipPolicy.Validate([MainFile(), new("folder/", size, true)]));

    /// <summary>末尾の区切りを含めて 260 文字ちょうどを許し、1 文字超えは拒否する。</summary>
    [Theory]
    [InlineData(260, false, true)]
    [InlineData(261, false, false)]
    [InlineData(260, true, true)]
    [InlineData(261, true, false)]
    public void PathLengthBoundariesAreEnforced(int length, bool isDirectory, bool expected)
    {
        var suffix = isDirectory ? "/" : "";
        var path = "folder/" + new string('a', length - "folder/".Length - suffix.Length) + suffix;
        var result = UpdateZipPolicy.Validate([MainFile(), new(path, 0, isDirectory)]);
        if (expected) { Assert.True(result.IsValid, result.Error); }
        else { AssertInvalid(result); }
    }

    private static UpdateZipEntry MainFile() => new("Snapwell.exe", 1, false);

    private static void AssertInvalid(UpdateZipValidation result)
    {
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(result.Files);
    }
}
