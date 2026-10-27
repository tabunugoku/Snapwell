using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>ソース中の文言の参照がカタログに存在することを確かめる。</summary>
public sealed class SourceReferenceTests
{
    /// <summary>空振りを許さず、C# のリテラルと XAML の Uid を検査する。</summary>
    [Fact]
    public void EverySourceReferenceExistsInJapaneseCatalog()
    {
        var root = SourceScan.RepositoryRoot();
        var keys = TextCatalog.Load(UiLanguage.Japanese).Keys.ToHashSet(StringComparer.Ordinal);
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*",
            SearchOption.AllDirectories).Where(path => path.EndsWith(".cs") || path.EndsWith(".xaml"))
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "obj" or "bin")))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, "UiText\\.Current\\.(Get|Format|Plural)\\(\\s*\"([^\"]+)\""))
            {
                count++;
                var key = match.Groups[2].Value;
                if (match.Groups[1].Value == "Plural")
                {
                    Assert.Contains(key + ".One", keys);
                    Assert.Contains(key + ".Other", keys);
                }
                else { Assert.Contains(key, keys); }
            }
            // 補間文字列のキーは値を決められないため、最初の { までの接頭辞を持つキーがあることを確かめる。
            foreach (Match match in Regex.Matches(source, "UiText\\.Current\\.Get\\(\\s*\\$\"([^\"{]*)\\{"))
            {
                count++;
                Assert.Contains(keys, key => key.StartsWith(match.Groups[1].Value, StringComparison.Ordinal));
            }
            foreach (Match match in Regex.Matches(source, "Loc\\.Uid=\"([^\"]+)\""))
            {
                count++;
                Assert.Contains(keys, key => key.StartsWith(match.Groups[1].Value + ".", StringComparison.Ordinal));
            }
        }
        Assert.True(count > 0, "文言の参照を 1 件も検出できない。");
    }
}
