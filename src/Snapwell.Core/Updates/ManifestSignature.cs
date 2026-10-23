using System.Security.Cryptography;
using System.Text;

namespace Snapwell.Core.Updates;

/// <summary>マニフェストのバイト列を P-256・SHA-256・IEEE P1363 で署名し検証する。</summary>
public static class ManifestSignature
{
    /// <summary>Base64 の署名と SPKI 公開鍵を検証し、不正な入力は false を返す。</summary>
    public static bool Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureBase64,
        string publicKeySpkiBase64)
    {
        if (manifest.Length > UpdateManifestParser.MaximumBytes) { return false; }

        try
        {
            Span<byte> signature = stackalloc byte[64];
            if (!Convert.TryFromBase64Chars(Encoding.UTF8.GetString(signatureBase64), signature, out var written)
                || written != signature.Length)
            {
                return false;
            }

            var key = Convert.FromBase64String(publicKeySpkiBase64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out var consumed);
            return consumed == key.Length && IsP256(ecdsa)
                && ecdsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>PKCS#8 秘密鍵で署名し Base64 の UTF-8 バイト列を返し、不正な鍵は ArgumentException にする。</summary>
    public static byte[] Sign(ReadOnlySpan<byte> manifest, string privateKeyPkcs8Base64)
    {
        byte[]? key = null;
        try
        {
            key = Convert.FromBase64String(privateKeyPkcs8Base64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(key, out var consumed);
            if (consumed != key.Length || !IsP256(ecdsa))
            {
                throw new ArgumentException("The private key must be a P-256 PKCS#8 key.");
            }

            var signature = ecdsa.SignData(manifest, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return Encoding.UTF8.GetBytes(Convert.ToBase64String(signature));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            throw new ArgumentException("The private key must be a valid P-256 PKCS#8 Base64 key.",
                nameof(privateKeyPkcs8Base64));
        }
        finally
        {
            if (key is not null) { CryptographicOperations.ZeroMemory(key); }
        }
    }

    /// <summary>P-256 の秘密鍵を PKCS#8、公開鍵を SPKI の Base64 として生成する。</summary>
    public static (string PrivateKeyPkcs8Base64, string PublicKeySpkiBase64) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = ecdsa.ExportPkcs8PrivateKey();
        try
        {
            return (Convert.ToBase64String(key), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool IsP256(ECDsa ecdsa) => ecdsa.ExportParameters(false).Curve.Oid.Value
        == ECCurve.NamedCurves.nistP256.Oid.Value;
}
