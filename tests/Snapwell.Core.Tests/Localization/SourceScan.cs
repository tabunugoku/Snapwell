using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行したソースに、例外指定のない日本語のリテラルが残っていないか調べる。</summary>
internal static class SourceScan
{
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Snapwell.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つからない。");
    }

    internal static void AssertNoJapaneseLiterals(params string[] paths)
    {
        var root = RepositoryRoot();
        var failures = paths.SelectMany(path => FindJapaneseLiterals(File.ReadAllText(Path.Combine(root, path)),
            Path.GetExtension(path)).Select(line => $"{path}:{line}")).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    internal static IReadOnlyList<int> FindJapaneseLiterals(string source, string extension)
    {
        var found = new HashSet<int>();
        var allowed = new HashSet<int>();
        var lineStarts = new[] { 0 }.Concat(source.Select((character, index) => (character, index))
            .Where(item => item.character == '\n').Select(item => item.index + 1)).ToArray();
        int LineAt(int position) => source.AsSpan(0, position).Count('\n') + 1;
        void Check(string value, int line)
        {
            foreach (var character in value)
            {
                if (IsJapanese(character)) { found.Add(line); }
                if (character == '\n') { line++; }
            }
        }
        void Allow(string comment, int line, bool xaml)
        {
            var marker = (xaml ? comment[4..^3] : comment[2..]).Trim();
            const string prefix = "i18n-keep:";
            if (!marker.StartsWith(prefix, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(marker[prefix.Length..])) { return; }
            var endLine = line + comment.Count(character => character == '\n');
            allowed.Add(endLine);
            allowed.Add(endLine + 1);
        }

        if (extension == ".xaml")
        {
            var document = XDocument.Parse(source, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            foreach (var node in document.DescendantNodes())
            {
                var line = ((IXmlLineInfo)node).LineNumber;
                if (node is XComment comment)
                {
                    var info = (IXmlLineInfo)comment;
                    var start = lineStarts[line - 1] + info.LinePosition - 1;
                    var end = source.IndexOf("-->", start, StringComparison.Ordinal) + 3;
                    var lineEnd = source.IndexOf('\n', end);
                    if (lineEnd < 0) { lineEnd = source.Length; }
                    if (string.IsNullOrWhiteSpace(source[end..lineEnd]))
                    {
                        Allow("<!--" + comment.Value + "-->", line, true);
                    }
                }
                else if (node is XText text) { Check(text.Value, line); }
                else if (node is XElement element)
                {
                    foreach (var attribute in element.Attributes())
                    {
                        // XML の属性値は改行が空白に正規化されるため、例外指定の行は元のソースで求める。
                        var info = (IXmlLineInfo)attribute;
                        var start = lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
                        var quote = source.IndexOfAny(['"', '\''], start);
                        var end = source.IndexOf(source[quote], quote + 1);
                        Check(WebUtility.HtmlDecode(source[(quote + 1)..end]), LineAt(quote));
                    }
                }
            }
        }
        else
        {
            var position = 0;
            void ScanCode(int closingBraces = 0)
            {
                var depth = 0;
                while (position < source.Length)
                {
                    if (source.AsSpan(position).StartsWith("//"))
                    {
                        var end = source.IndexOf('\n', position);
                        if (end < 0) { end = source.Length; }
                        Allow(source[position..end], LineAt(position), false);
                        position = end;
                    }
                    else if (source.AsSpan(position).StartsWith("/*"))
                    {
                        var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                        position = end < 0 ? source.Length : end + 2;
                    }
                    else if (source[position] == '\'')
                    {
                        // 文字リテラルの引用符を、文字列の開始と取り違えない。
                        position++;
                        while (position < source.Length && source[position] != '\'')
                        {
                            position += source[position] == '\\' ? 2 : 1;
                        }
                        position = Math.Min(source.Length, position + 1);
                    }
                    else if (source[position] == '"') { ScanString(); }
                    else if (source[position] == '{') { depth++; position++; }
                    else if (source[position] == '}' && closingBraces > 0 && depth == 0
                        && RunLength(position, '}') >= closingBraces)
                    {
                        position += closingBraces;
                        return;
                    }
                    else
                    {
                        if (closingBraces > 0 && IsJapanese(source[position])) { found.Add(LineAt(position)); }
                        if (source[position] == '}') { depth--; }
                        position++;
                    }
                }
            }
            int RunLength(int start, char character)
            {
                var end = start;
                while (end < source.Length && source[end] == character) { end++; }
                return end - start;
            }
            void ScanString()
            {
                var quoteCount = RunLength(position, '"');
                var raw = quoteCount >= 3;
                var prefixStart = position;
                while (prefixStart > 0 && source[prefixStart - 1] is '$' or '@') { prefixStart--; }
                var prefix = source[prefixStart..position];
                var dollars = prefix.Count(character => character == '$');
                var verbatim = prefix.Contains('@');
                position += raw ? quoteCount : 1;
                while (position < source.Length)
                {
                    var character = source[position];
                    if (character == '"')
                    {
                        if (raw)
                        {
                            if (RunLength(position, '"') >= quoteCount) { position += quoteCount; return; }
                        }
                        else if (verbatim && RunLength(position, '"') >= 2) { position += 2; continue; }
                        else { position++; return; }
                    }
                    if (!raw && !verbatim && character == '\\') { position += 2; continue; }
                    if (dollars > 0 && character == '{')
                    {
                        var braces = RunLength(position, '{');
                        if (!raw && braces >= 2) { position += 2; continue; }
                        var delimiter = raw ? dollars : 1;
                        if (braces >= delimiter)
                        {
                            position += braces;
                            ScanCode(delimiter);
                            continue;
                        }
                    }
                    if (IsJapanese(character)) { found.Add(LineAt(position)); }
                    position++;
                }
            }
            ScanCode();
        }

        // 許可はコメントからだけ採り、文字列に書かれたマーカーでは検査を逃れられないようにする。
        return found.Where(line => !allowed.Contains(line)).Order().ToArray();
    }

    private static bool IsJapanese(char character) => character is >= '\u3040' and <= '\u30FF'
        or >= '\u4E00' and <= '\u9FFF' or >= '\uFF66' and <= '\uFF9F';
}
