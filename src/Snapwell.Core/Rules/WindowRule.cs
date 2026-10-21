using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapwell.Core.Rules;

/// <summary>ルールで上書きする配置の扱い。</summary>
[JsonConverter(typeof(PlacementRuleJsonConverter))]
public enum PlacementRule
{
    Inherit, Exclude, PositionOnly, ForceTarget,
}

/// <summary>ルールで上書きするウィンドウの角。</summary>
[JsonConverter(typeof(CornerRuleJsonConverter))]
public enum CornerRule
{
    Inherit, SystemDefault, Round, RoundSmall, DoNotRound,
}

/// <summary>指定した条件すべてに一致するウィンドウの扱い。</summary>
public sealed record WindowRule
{
    public bool Enabled { get; init; } = true;
    public string ProcessName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string TitlePattern { get; init; } = string.Empty;
    public PlacementRule Placement { get; init; } = PlacementRule.Inherit;
    public CornerRule Corner { get; init; } = CornerRule.Inherit;
}

/// <summary>判定対象の識別情報。プロセス名の null は取得失敗を表す。</summary>
public readonly record struct WindowIdentity(string? ProcessName, string ClassName, string Title);

/// <summary>配置と角を別々に解決した結果。</summary>
public readonly record struct RuleOutcome(PlacementRule Placement, CornerRule Corner);

internal sealed class PlacementRuleJsonConverter : RuleEnumJsonConverter<PlacementRule>;
internal sealed class CornerRuleJsonConverter : RuleEnumJsonConverter<CornerRule>;

// ジェネリックな列挙 API と明示した変換器で、NativeAOT でも未知の値を Inherit に戻す。
internal abstract class RuleEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<T>(reader.GetString(), true, out var value) && Enum.IsDefined(value))
        {
            return value;
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue((Enum.IsDefined(value) ? value : default).ToString());
}
