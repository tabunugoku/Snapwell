using System.Text;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>更新開始の条件と容量の境界を検証する。</summary>
public sealed class UpdateInstallPlannerTests
{
    /// <summary>押せない理由を区別し、容量がちょうど二倍なら開始できる。</summary>
    [Theory]
    [InlineData("valid", UpdateInstallBlock.None)]
    [InlineData("development", UpdateInstallBlock.DevelopmentBuild)]
    [InlineData("null-version", UpdateInstallBlock.DevelopmentBuild)]
    [InlineData("no-offer", UpdateInstallBlock.NoOffer)]
    [InlineData("manifest", UpdateInstallBlock.MissingCache)]
    [InlineData("signature", UpdateInstallBlock.MissingCache)]
    [InlineData("architecture", UpdateInstallBlock.ArchitectureUnsupported)]
    [InlineData("url", UpdateInstallBlock.DownloadForbidden)]
    [InlineData("size", UpdateInstallBlock.DownloadForbidden)]
    [InlineData("executable", UpdateInstallBlock.MissingExecutable)]
    [InlineData("space", UpdateInstallBlock.InsufficientSpace)]
    public void EvaluatesConditions(string condition, UpdateInstallBlock expected)
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://github.com/notes",
            "files":{"win-arm64":{"url":"https://github.com/package.zip",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
            """);
        var manifest = UpdateManifestParser.Parse(bytes).Manifest!;
        var asset = manifest.Files["win-arm64"];
        if (condition == "url") { asset = asset with { Url = new("http://github.com/package.zip") }; }
        if (condition == "size") { asset = asset with { Size = long.MaxValue }; }
        var offer = new UpdateOffer(manifest, asset, false);
        Assert.Equal(expected, UpdateInstallPlanner.Evaluate(condition switch
        {
            "development" => "0.0.0",
            "null-version" => null,
            _ => "1.0.0",
        }, condition == "no-offer" ? null : offer, condition == "manifest" ? [] : bytes,
            condition == "signature" ? null : [1], condition == "architecture"
                ? UpdateOsArchitecture.Other : UpdateOsArchitecture.Arm64, "exe", "pending",
            _ => condition != "executable", _ => condition == "space" ? 245 : 246));
    }
}
