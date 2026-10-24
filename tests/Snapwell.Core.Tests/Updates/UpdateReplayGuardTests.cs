using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>署名済み情報の版の巻き戻りを検証する。</summary>
public sealed class UpdateReplayGuardTests
{
    /// <summary>同じ版以上だけを受け入れ、正式版はプレリリースより新しいとする。</summary>
    [Theory]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.3.0", "1.2.0", true)]
    [InlineData("1.1.0", null, true)]
    [InlineData("1.2.0", "1.2.0-rc.1", true)]
    [InlineData("1.2.0-rc.1", "1.2.0", false)]
    public void AcceptUsesSemver(string latest, string? highest, bool accepted)
    {
        var manifest = ManifestTestData.Parse(ManifestTestData.Json.Replace("\"1.0.1\"", "\"" + latest + "\""));
        ReleaseVersion.TryParse(highest, out var seen);
        Assert.Equal(accepted, UpdateReplayGuard.Accept(manifest, seen));
    }
}
