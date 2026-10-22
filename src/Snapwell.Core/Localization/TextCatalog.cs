using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Snapwell.Core.Localization;

/// <summary>変更しない 1 言語分の文言と英語の落とし先。</summary>
public sealed class TextCatalog
{
    private readonly IReadOnlyDictionary<string, string> _strings;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    internal TextCatalog(UiLanguage language, IReadOnlyDictionary<string, string> strings,
        IReadOnlyDictionary<string, string> fallback)
    {
        Language = language;
        _strings = new Dictionary<string, string>(strings, StringComparer.Ordinal);
        _fallback = new Dictionary<string, string>(fallback, StringComparer.Ordinal);
    }

    /// <summary>このカタログの言語。</summary>
    public UiLanguage Language { get; }

    /// <summary>選んだ言語に含まれるキー。</summary>
    public IEnumerable<string> Keys => _strings.Keys;

    /// <summary>Core アセンブリに埋め込んだ文言を読む。</summary>
    public static TextCatalog Load(UiLanguage language)
    {
        var strings = ReadResource(language);
        return new(language, strings, language == UiLanguage.English ? strings : ReadResource(UiLanguage.English));
    }

    private static Dictionary<string, string> ReadResource(UiLanguage language)
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(
            $"i18n/{language.ToPreference()}.json")
            ?? throw new InvalidOperationException("文言のカタログが見つからない。");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return ReadStrings(buffer.ToArray());
    }

    internal static Dictionary<string, string> ReadStrings(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { data = data[3..]; }
        var reader = new Utf8JsonReader(data, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) { throw new JsonException(); }
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) { throw new JsonException(); }
            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String) { throw new JsonException(); }
            if (!strings.TryAdd(key, reader.GetString()!)) { throw new JsonException($"重複したキー: {key}"); }
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) { throw new JsonException(); }
        return strings;
    }

    /// <summary>選んだ言語、英語、キーの順に文言を探す。</summary>
    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value)) { return value; }
#if DEBUG
        return $"⟦{key}⟧";
#else
        return key;
#endif
    }

    /// <summary>名前つきの値を、数値の文化に依存せず埋め込む。</summary>
    public string Format(string key, params (string Name, object Value)[] args) => Interpolate(Get(key), args);

    /// <summary>英語だけ単数形を選び、件数を自動で埋め込む。</summary>
    public string Plural(string key, int count, params (string Name, object Value)[] args)
    {
        var values = args.Where(arg => arg.Name != "count").Append(("count", (object)count)).ToArray();
        return Format(key + (Language == UiLanguage.English && count == 1 ? ".One" : ".Other"), values);
    }

    private static string Interpolate(string template, (string Name, object Value)[] args)
    {
        var result = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character is '{' or '}' && index + 1 < template.Length && template[index + 1] == character)
            {
                result.Append(character);
                index++;
            }
            else if (character == '{' && template.IndexOf('}', index + 1) is var end && end >= 0)
            {
                var name = template[(index + 1)..end];
                var argument = Array.FindIndex(args, arg => arg.Name == name);
                result.Append(argument < 0 ? template[index..(end + 1)]
                    : Convert.ToString(args[argument].Value, CultureInfo.InvariantCulture));
                index = end;
            }
            else { result.Append(character); }
        }
        return result.ToString();
    }
}
