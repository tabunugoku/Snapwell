namespace Snapwell.Core.Updates;

/// <summary>署名付きリリース情報の固定の取得先。</summary>
public static class UpdateEndpoints
{
    /// <summary>リリース情報の HTTPS URL。</summary>
    public static readonly Uri Manifest = new("https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json");
    /// <summary>切り離した署名の HTTPS URL。</summary>
    public static readonly Uri Signature = new("https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json.sig");
}
