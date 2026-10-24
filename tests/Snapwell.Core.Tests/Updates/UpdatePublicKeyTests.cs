using System.Security.Cryptography;
using Snapwell.Core.Tests.Localization;
using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>埋め込みの有無と SPKI の検証を確かめる。</summary>
public sealed class UpdatePublicKeyTests
{
    /// <summary>埋め込まれた公開鍵は、リポジトリの鍵ファイルと同じで P-256 として読める。鍵ファイルがなければ null。</summary>
    [Fact]
    public void EmbeddedKeyMatchesRepositoryFile()
    {
        var path = Path.Combine(SourceScan.RepositoryRoot(), "release", "update-public-key.txt");
        Assert.Equal(File.Exists(path) ? UpdatePublicKey.Validate(File.ReadAllText(path)) : null, UpdatePublicKey.Current);
    }

    /// <summary>空や不正な Base64 を拒否する。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n ")]
    [InlineData("not base64")]
    [InlineData("AQID")]
    public void InvalidTextReturnsNull(string? text) => Assert.Null(UpdatePublicKey.Validate(text));

    /// <summary>他の曲線と鍵の末尾への追加を拒否する。</summary>
    [Fact]
    public void OnlyP256SpkiIsAccepted()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(UpdatePublicKey.Validate(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())));
        var (_, key) = ManifestSignature.GenerateKeyPair();
        Assert.Equal(key, UpdatePublicKey.Validate(" \n" + key + "\r\n "));
        Assert.Null(UpdatePublicKey.Validate(Convert.ToBase64String([.. Convert.FromBase64String(key), 0])));
    }
}
