using System.Security.Cryptography;

namespace Snapwell.Core.Updates;

/// <summary>配布時に埋め込まれた P-256 公開鍵を読み取る。</summary>
public static class UpdatePublicKey
{
    /// <summary>埋め込みがない場合や不正な場合は null を返す。</summary>
    public static string? Current { get; } = Validate(ReadResource());

    private static string? ReadResource()
    {
        using var stream = typeof(UpdatePublicKey).Assembly.GetManifestResourceStream("Snapwell.UpdatePublicKey");
        if (stream is null) { return null; }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return null; }
        try
        {
            var trimmed = text.Trim();
            var bytes = Convert.FromBase64String(trimmed);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var consumed);
            return consumed == bytes.Length && key.ExportParameters(false).Curve.Oid.Value
                == ECCurve.NamedCurves.nistP256.Oid.Value ? trimmed : null;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
