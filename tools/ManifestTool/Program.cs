using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snapwell.Core.Updates;

namespace ManifestTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { throw new ToolException("Expected keygen, generate, sign, or verify."); }
            var allowed = args[0] switch
            {
                "keygen" => new[] { "private", "public" },
                "generate" => ["version", "tag", "repo", "dist", "summary", "out", "published-at"],
                "sign" => ["key-env", "in", "out"],
                "verify" => ["public", "in", "sig"],
                _ => throw new ToolException("Unknown command. Expected keygen, generate, sign, or verify."),
            };
            var options = ParseOptions(args, allowed);
            return args[0] switch
            {
                "keygen" => Keygen(options),
                "generate" => Generate(options),
                "sign" => Sign(options),
                "verify" => Verify(options),
                _ => 1,
            };
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or CryptographicException or FormatException or JsonException or InvalidOperationException)
        {
            // 例外には入力やパスが含まれ得るため、鍵の値を診断に流さない。
            Console.Error.WriteLine("Command failed. Check options, input files, and key format.");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args, string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length
                || !allowed.Contains(args[i][2..], StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(args[i + 1]) || !options.TryAdd(args[i][2..], args[i + 1]))
            {
                throw new ToolException("Invalid, duplicate, or missing command option.");
            }
        }

        foreach (var name in allowed.Where(name => name != "published-at"))
        {
            if (!options.ContainsKey(name)) { throw new ToolException($"Missing --{name}."); }
        }

        return options;
    }

    private static int Keygen(Dictionary<string, string> options)
    {
        var privatePath = Path.GetFullPath(options["private"]);
        var publicPath = Path.GetFullPath(options["public"]);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (File.Exists(privatePath) || File.Exists(publicPath))
        {
            throw new ToolException("Key files already exist; nothing was overwritten.", 2);
        }

        if (string.Equals(privatePath, publicPath, comparison))
        {
            throw new ToolException("Private and public key files must have different paths.");
        }

        using var privateFile = CreateKeyFile(privatePath);
        using var publicFile = CreateKeyFile(publicPath);
        var keys = ManifestSignature.GenerateKeyPair();
        privateFile.Write(Encoding.UTF8.GetBytes(keys.PrivateKeyPkcs8Base64));
        publicFile.Write(Encoding.UTF8.GetBytes(keys.PublicKeySpkiBase64));
        return 0;
    }

    private static FileStream CreateKeyFile(string path)
    {
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            return new FileStream(path, options);
        }
        catch (IOException) when (File.Exists(path))
        {
            throw new ToolException("Key files already exist; nothing was overwritten.", 2);
        }
    }

    private static int Generate(Dictionary<string, string> options)
    {
        if (!ReleaseVersion.TryParse(options["version"], out var version))
        {
            throw new ToolException("Invalid release version.");
        }

        var repo = options["repo"].Split('/');
        if (repo.Length != 2 || repo.Any(part => part.Length == 0
            || part is "." or ".." || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
        {
            throw new ToolException("Expected --repo owner/name.");
        }

        var publishedAt = DateTimeOffset.UtcNow;
        if (options.TryGetValue("published-at", out var suppliedDate)
            && !DateTimeOffset.TryParseExact(suppliedDate,
                ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out publishedAt))
        {
            throw new ToolException("Invalid --published-at; expected an ISO 8601 timestamp with a time zone.");
        }

        using var summaryDocument = JsonDocument.Parse(File.ReadAllBytes(options["summary"]));
        var root = summaryDocument.RootElement;
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object
            || !summary.TryGetProperty("ja", out var japanese) || japanese.ValueKind != JsonValueKind.String
            || !summary.TryGetProperty("en", out var english) || english.ValueKind != JsonValueKind.String)
        {
            throw new ToolException("The summary file must contain summary.ja and summary.en strings.");
        }

        var releaseUrl = $"https://github.com/{options["repo"]}/releases";
        var tag = Uri.EscapeDataString(options["tag"]);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("version", version.ToString());
            writer.WriteString("publishedAt", publishedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartObject("summary");
            writer.WriteString("ja", japanese.GetString());
            writer.WriteString("en", english.GetString());
            writer.WriteEndObject();
            writer.WriteString("releaseNotesUrl", $"{releaseUrl}/tag/{tag}");
            writer.WriteStartObject("files");
            foreach (var rid in new[] { "win-x64", "win-arm64" })
            {
                var name = $"Snapwell-{version}-{rid}.zip";
                var path = Path.Combine(options["dist"], name);
                var checksumParts = File.ReadAllText(path + ".sha256").Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);
                if (checksumParts.Length == 0 || checksumParts[0].Length != 64
                    || !checksumParts[0].All(Uri.IsHexDigit))
                {
                    throw new ToolException($"Invalid SHA256 file for {rid}.");
                }

                using var zip = File.OpenRead(path);
                var expectedHash = checksumParts[0].ToLowerInvariant();
                var actualHash = Convert.ToHexStringLower(SHA256.HashData(zip));
                if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                {
                    throw new ToolException($"SHA256 mismatch for {rid}.");
                }

                writer.WriteStartObject(rid);
                writer.WriteString("url", $"{releaseUrl}/download/{tag}/{Uri.EscapeDataString(name)}");
                writer.WriteString("sha256", expectedHash);
                writer.WriteNumber("size", zip.Length);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            if (root.TryGetProperty("minimumSupported", out var minimum))
            {
                writer.WritePropertyName("minimumSupported");
                minimum.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var bytes = buffer.ToArray();
        if (!UpdateManifestParser.Parse(bytes).IsSuccess) { throw new ToolException("Generated manifest is invalid."); }
        File.WriteAllBytes(options["out"], bytes);
        if (!UpdateManifestParser.Parse(File.ReadAllBytes(options["out"])).IsSuccess)
        {
            throw new ToolException("Manifest readback failed.");
        }

        return 0;
    }

    private static int Sign(Dictionary<string, string> options)
    {
        var privateKey = Environment.GetEnvironmentVariable(options["key-env"]);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new ToolException("The signing key environment variable is missing or empty.");
        }
        var manifest = File.ReadAllBytes(options["in"]);
        var signature = ManifestSignature.Sign(manifest, privateKey);
        var keyBytes = Convert.FromBase64String(privateKey);
        string publicKey;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);
            publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }

        if (!ManifestSignature.Verify(manifest, signature, publicKey))
        {
            throw new ToolException("Signature verification failed.");
        }

        File.WriteAllBytes(options["out"], signature);
        if (!ManifestSignature.Verify(File.ReadAllBytes(options["in"]), File.ReadAllBytes(options["out"]), publicKey))
        {
            throw new ToolException("Signature readback failed.");
        }

        // ワークフローはこの公開鍵だけを次の検証手順へ渡す。秘密鍵は出力しない。
        Console.WriteLine(publicKey);
        return 0;
    }

    private static int Verify(Dictionary<string, string> options)
    {
        var publicKey = File.Exists(options["public"]) ? File.ReadAllText(options["public"]) : options["public"];
        if (ManifestSignature.Verify(File.ReadAllBytes(options["in"]), File.ReadAllBytes(options["sig"]), publicKey))
        {
            return 0;
        }

        throw new ToolException("Signature verification failed.");
    }

    private sealed class ToolException(string message, int exitCode = 1) : Exception(message)
    {
        internal int ExitCode { get; } = exitCode;
    }
}
