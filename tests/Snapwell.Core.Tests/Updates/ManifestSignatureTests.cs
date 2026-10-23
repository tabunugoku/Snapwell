using System.Security.Cryptography;
using System.Text;
using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>署名の相互運用、改変の拒否、不正な入力とサイズの境界を確認する。</summary>
public sealed class ManifestSignatureTests
{
    /// <summary>同じ入力への複数回の署名が正しい形式で検証できる。</summary>
    [Fact]
    public void RepeatedSignaturesVerifyWithP1363AndSha256()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var manifest = "{\"version\":\"1.0.0\"}"u8.ToArray();
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(keys.PublicKeySpkiBase64), out _);
        Assert.Equal(ECCurve.NamedCurves.nistP256.Oid.Value, verifier.ExportParameters(false).Curve.Oid.Value);
        for (var i = 0; i < 2; i++)
        {
            var signature = ManifestSignature.Sign(manifest, keys.PrivateKeyPkcs8Base64);
            Assert.True(ManifestSignature.Verify(manifest, signature, keys.PublicKeySpkiBase64));
            var raw = Convert.FromBase64String(Encoding.UTF8.GetString(signature));
            Assert.Equal(64, raw.Length);
            Assert.True(verifier.VerifyData(manifest, raw, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
    }

    /// <summary>メッセージの一バイト、署名の一文字、署名した鍵の違いを検出する。</summary>
    [Fact]
    public void TamperedManifestSignatureAndDifferentKeyFail()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var manifest = "manifest"u8.ToArray();
        var signature = ManifestSignature.Sign(manifest, keys.PrivateKeyPkcs8Base64);
        var changedManifest = (byte[])manifest.Clone();
        changedManifest[0] ^= 1;
        Assert.False(ManifestSignature.Verify(changedManifest, signature, keys.PublicKeySpkiBase64));
        var changedSignature = (byte[])signature.Clone();
        changedSignature[0] = signature[0] == (byte)'A' ? (byte)'B' : (byte)'A';
        Assert.False(ManifestSignature.Verify(manifest, changedSignature, keys.PublicKeySpkiBase64));
        var other = ManifestSignature.GenerateKeyPair();
        Assert.False(ManifestSignature.Verify(manifest,
            ManifestSignature.Sign(manifest, other.PrivateKeyPkcs8Base64), keys.PublicKeySpkiBase64));
    }

    /// <summary>空、Base64 不正、途中で切れた鍵、余分な DER、別の方式や曲線を例外なしで拒否する。</summary>
    [Fact]
    public void InvalidPublicKeysFailWithoutThrowing()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var signature = ManifestSignature.Sign("manifest"u8, keys.PrivateKeyPkcs8Base64);
        var der = Convert.FromBase64String(keys.PublicKeySpkiBase64);
        using var rsa = RSA.Create(2048);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        foreach (var key in new[] { "", "not base64!", Convert.ToBase64String(der[..^1]),
            Convert.ToBase64String(der.Append((byte)0).ToArray()),
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()) })
        {
            Assert.False(ManifestSignature.Verify("manifest"u8, signature, key));
        }
    }

    /// <summary>空、63 バイト、65 バイト、Base64 不正の署名を例外なしで拒否する。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(-1)]
    public void InvalidSignaturesFailWithoutThrowing(int length)
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var signature = length < 0 ? "not base64!" : Convert.ToBase64String(new byte[length]);
        Assert.False(ManifestSignature.Verify("manifest"u8, Encoding.UTF8.GetBytes(signature),
            keys.PublicKeySpkiBase64));
    }

    /// <summary>署名の前後の空白と改行を許す。</summary>
    [Fact]
    public void SurroundingWhitespaceIsAccepted()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var signature = ManifestSignature.Sign("manifest"u8, keys.PrivateKeyPkcs8Base64);
        var padded = Encoding.UTF8.GetBytes(" \r\n\t" + Encoding.UTF8.GetString(signature) + "\n \r\n");
        Assert.True(ManifestSignature.Verify("manifest"u8, padded, keys.PublicKeySpkiBase64));
    }

    /// <summary>64 KiB ちょうどを許し、署名が正しくても一バイト超過を拒否する。</summary>
    [Fact]
    public void SizeLimitIsEnforcedEvenForValidSignatures()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        foreach (var length in new[] { UpdateManifestParser.MaximumBytes, UpdateManifestParser.MaximumBytes + 1 })
        {
            var manifest = new byte[length];
            var signature = ManifestSignature.Sign(manifest, keys.PrivateKeyPkcs8Base64);
            Assert.Equal(length == UpdateManifestParser.MaximumBytes,
                ManifestSignature.Verify(manifest, signature, keys.PublicKeySpkiBase64));
        }
    }

    /// <summary>不正な秘密鍵は値を含めず ArgumentException にする。</summary>
    [Fact]
    public void InvalidPrivateKeysThrowArgumentException()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        var der = Convert.FromBase64String(keys.PrivateKeyPkcs8Base64);
        using var rsa = RSA.Create(2048);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        foreach (var key in new[] { "", "not base64!", keys.PublicKeySpkiBase64,
            Convert.ToBase64String(der[..^1]), Convert.ToBase64String(der.Append((byte)0).ToArray()),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Convert.ToBase64String(p384.ExportPkcs8PrivateKey()) })
        {
            Assert.Throws<ArgumentException>(() => ManifestSignature.Sign("manifest"u8, key));
        }
    }

    /// <summary>複数のスレッドで署名と検証を同時に呼べる。</summary>
    [Fact]
    public void ConcurrentSigningAndVerificationHaveNoSharedState()
    {
        var keys = ManifestSignature.GenerateKeyPair();
        Parallel.For(0, 16, i =>
        {
            var manifest = BitConverter.GetBytes(i);
            var signature = ManifestSignature.Sign(manifest, keys.PrivateKeyPkcs8Base64);
            Assert.True(ManifestSignature.Verify(manifest, signature, keys.PublicKeySpkiBase64));
        });
    }
}
