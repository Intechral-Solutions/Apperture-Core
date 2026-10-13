using System.Text;
using System.Text.RegularExpressions;

namespace Apperture.UnitTests.Audit;

/// <summary>One production source file of the repository (<c>src/**</c>).</summary>
internal sealed class SourceFile
{
    private readonly Lazy<string> _code;

    public SourceFile(string relativePath, string text)
    {
        RelativePath = relativePath;
        Text = text;
        _code = new Lazy<string>(() => CSharpSource.StripComments(text));
    }

    /// <summary>The path below <c>src/</c>, with forward slashes.</summary>
    public string RelativePath { get; }

    /// <summary>The file as written.</summary>
    public string Text { get; }

    /// <summary>The file with comments removed. String literals are kept. Computed once, on first use.</summary>
    public string Code => _code.Value;

    /// <summary>True when <paramref name="identifier"/> appears in code (not only in a comment) as a whole word.</summary>
    public bool Mentions(string identifier) =>
        Text.Contains(identifier, StringComparison.Ordinal) && CSharpSource.ContainsWord(Code, identifier);

    public override string ToString() => RelativePath;
}

/// <summary>
/// Reads the repository's production C# source once and caches it, so every closure guard shares one pass over <c>src/**</c>.
/// The guards are identifier based: they look for names and string literals in code, never for line numbers, formatting or
/// whole-file text.
/// </summary>
internal static class RepositorySource
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", "Migrations", "node_modules", ".git", "TestResults"];

    private static readonly Lazy<string> RootDirectory = new(FindRoot);
    private static readonly Lazy<IReadOnlyList<SourceFile>> CSharpFiles = new(() => Read("*.cs"));
    private static readonly Lazy<IReadOnlyList<SourceFile>> UiFiles = new(() => ReadMany("*.razor", "*.cshtml"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ConstantTable = new(BuildConstantTable);

    /// <summary>The repository root: the nearest folder above the test binaries that holds <c>AGENTS.md</c> or a <c>.git</c> entry.</summary>
    public static string Root => RootDirectory.Value;

    /// <summary>Every production C# file below <c>src/</c> (migrations excluded), in path order.</summary>
    public static IReadOnlyList<SourceFile> Production => CSharpFiles.Value;

    /// <summary>The production Razor and view files below <c>src/</c>, which can also name an action.</summary>
    public static IReadOnlyList<SourceFile> ProductionUi => UiFiles.Value;

    public static SourceFile? Find(string relativePath) =>
        Production.FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.Ordinal));

    /// <summary>
    /// Every <c>const string</c> in production code, keyed <c>Type.Name</c>, with its value. An alias of another constant
    /// (<c>X = Other.Y</c>) is resolved; a concatenation is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Constants => ConstantTable.Value;

    /// <summary>The string constants declared in one file, by bare name (for a private constant used in its own file).</summary>
    public static IReadOnlyDictionary<string, string> LocalConstants(SourceFile file)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in ConstantDeclarations(file))
        {
            var value = Evaluate(declaration.Expression, declaration.TypeName);
            if (value is not null)
                map[declaration.Name] = value;
        }

        return map;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) || Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found: no folder above the test binaries holds AGENTS.md or .git.");
    }

    private static IReadOnlyList<SourceFile> Read(string pattern) => ReadMany(pattern);

    private static IReadOnlyList<SourceFile> ReadMany(params string[] patterns)
    {
        var src = Path.Combine(Root, "src");
        var files = new List<SourceFile>();
        Walk(src, src, patterns, files);
        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return files;
    }

    private static void Walk(string src, string directory, string[] patterns, List<SourceFile> into)
    {
        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
                into.Add(new SourceFile(relative, File.ReadAllText(path)));
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            Walk(src, child, patterns, into);
        }
    }

    // ---- constants ----

    private static readonly Regex ConstRegex = new(
        @"\bconst\s+string\s+(?<name>\w+)\s*=\s*(?<expr>[^;]+);",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TypeRegex = new(
        @"\b(?:class|struct|record|interface)\s+(?<type>\w+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record ConstantDeclaration(string TypeName, string Name, string Expression);

    private static IEnumerable<ConstantDeclaration> ConstantDeclarations(SourceFile file)
    {
        if (!file.Text.Contains("const string", StringComparison.Ordinal))
            yield break;

        var code = file.Code;
        var types = TypeRegex.Matches(code).Select(m => (m.Index, Name: m.Groups["type"].Value)).ToList();
        foreach (Match match in ConstRegex.Matches(code))
        {
            var typeName = types.LastOrDefault(t => t.Index < match.Index).Name ?? string.Empty;
            yield return new ConstantDeclaration(typeName, match.Groups["name"].Value, match.Groups["expr"].Value.Trim());
        }
    }

    private static IReadOnlyDictionary<string, string> BuildConstantTable()
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        var expressions = new List<(string Key, string Type, string Expression)>();
        foreach (var file in Production)
        {
            foreach (var d in ConstantDeclarations(file))
                expressions.Add(($"{d.TypeName}.{d.Name}", d.TypeName, d.Expression));
        }

        // Literals first, then aliases, up to a few levels deep.
        var pending = new List<(string Key, string Type, string Expression)>();
        foreach (var (key, type, expression) in expressions)
        {
            if (TryLiteral(expression, out var literal))
                declarations[key] = literal;
            else
                pending.Add((key, type, expression));
        }

        for (var pass = 0; pass < 5 && pending.Count > 0; pass++)
        {
            var next = new List<(string Key, string Type, string Expression)>();
            foreach (var item in pending)
            {
                var aliasKey = AliasKey(item.Expression, item.Type);
                if (aliasKey is not null && declarations.TryGetValue(aliasKey, out var resolved))
                    declarations[item.Key] = resolved;
                else
                    next.Add(item);
            }

            pending = next;
        }

        return declarations;
    }

    private static string? Evaluate(string expression, string typeName)
    {
        if (TryLiteral(expression, out var literal))
            return literal;
        var key = AliasKey(expression, typeName);
        return key is not null && Constants.TryGetValue(key, out var value) ? value : null;
    }

    private static bool TryLiteral(string expression, out string value)
    {
        var match = Regex.Match(expression, "^\"(?<v>[^\"\\\\]*)\"$", RegexOptions.CultureInvariant);
        value = match.Success ? match.Groups["v"].Value : string.Empty;
        return match.Success;
    }

    private static string? AliasKey(string expression, string typeName)
    {
        if (!Regex.IsMatch(expression, @"^[\w.]+$", RegexOptions.CultureInvariant))
            return null;
        var parts = expression.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : $"{typeName}.{parts[0]}";
    }
}

/// <summary>Small, format-independent helpers for reading C# text. Nothing here parses C#: it removes comments and cuts statements.</summary>
internal static class CSharpSource
{
    /// <summary>
    /// Removes <c>//</c> and <c>/* */</c> comments and leaves everything else, including string and character literals, as written.
    /// Regular, verbatim, interpolated and raw string literals are skipped as units, so a comment marker inside one is not a comment.
    /// </summary>
    public static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        var n = source.Length;
        while (i < n)
        {
            var c = source[i];
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                sb.Append(' ');
                continue;
            }

            if (c == '\'')
            {
                var j = i + 1;
                while (j < n && source[j] != '\'')
                {
                    if (source[j] == '\\')
                        j++;
                    j++;
                }

                var stop = Math.Min(j + 1, n);
                sb.Append(source, i, stop - i);
                i = stop;
                continue;
            }

            if (c is '"' or '@' or '$')
            {
                var j = i;
                var verbatim = false;
                var interpolated = false;
                while (j < n && source[j] is '@' or '$')
                {
                    if (source[j] == '@')
                        verbatim = true;
                    else
                        interpolated = true;
                    j++;
                }

                if (j < n && source[j] == '"')
                {
                    var stop = SkipString(source, j, verbatim, interpolated);
                    sb.Append(source, i, stop - i);
                    i = stop;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>The index just past the string literal whose opening quote is at <paramref name="quote"/>.</summary>
    private static int SkipString(string s, int quote, bool verbatim, bool interpolated)
    {
        var n = s.Length;
        if (quote + 2 < n && s[quote + 1] == '"' && s[quote + 2] == '"')
        {
            var quotes = 3;
            while (quote + quotes < n && s[quote + quotes] == '"')
                quotes++;
            var closing = new string('"', quotes);
            var end = s.IndexOf(closing, quote + quotes, StringComparison.Ordinal);
            return end < 0 ? n : end + quotes;
        }

        var k = quote + 1;
        while (k < n)
        {
            var c = s[k];
            if (verbatim)
            {
                if (c == '"')
                {
                    if (k + 1 < n && s[k + 1] == '"')
                    {
                        k += 2;
                        continue;
                    }

                    return k + 1;
                }

                k++;
                continue;
            }

            if (c == '\\')
            {
                k += 2;
                continue;
            }

            if (c == '"')
                return k + 1;

            if (interpolated && c == '{')
            {
                if (k + 1 < n && s[k + 1] == '{')
                {
                    k += 2;
                    continue;
                }

                k = SkipHole(s, k);
                continue;
            }

            k++;
        }

        return n;
    }

    private static int SkipHole(string s, int open)
    {
        var n = s.Length;
        var depth = 1;
        var k = open + 1;
        while (k < n && depth > 0)
        {
            var c = s[k];
            if (c == '"')
            {
                k = SkipString(s, k, verbatim: false, interpolated: false);
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;
            k++;
        }

        return k;
    }

    /// <summary>True when <paramref name="word"/> occurs in <paramref name="code"/> bounded by non-identifier characters.</summary>
    public static bool ContainsWord(string code, string word)
    {
        var from = 0;
        while (true)
        {
            var index = code.IndexOf(word, from, StringComparison.Ordinal);
            if (index < 0)
                return false;
            var before = index == 0 || !IsIdentifierChar(code[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= code.Length || !IsIdentifierChar(code[afterIndex]);
            if (before && after)
                return true;
            from = index + 1;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The text from <paramref name="index"/> to the next <c>;</c> (or the end of the code).</summary>
    public static string TailStatement(string code, int index)
    {
        var end = code.IndexOf(';', index);
        return end < 0 ? code[index..] : code[index..end];
    }

    /// <summary>
    /// The string literals of comment-stripped code, as spans (start inclusive, end exclusive) over <paramref name="code"/>. Regular,
    /// verbatim, interpolated and raw literals are one span each. Used to read SQL text and to skip literals while counting braces.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> StringSpans(string code)
    {
        var spans = new List<(int Start, int End)>();
        var n = code.Length;
        var i = 0;
        while (i < n)
        {
            var c = code[i];
            if (c == '\'')
            {
                var j = i + 1;
                while (j < n && code[j] != '\'')
                {
                    if (code[j] == '\\')
                        j++;
                    j++;
                }

                i = Math.Min(j + 1, n);
                continue;
            }

            if (c is '"' or '@' or '$')
            {
                var j = i;
                var verbatim = false;
                var interpolated = false;
                while (j < n && code[j] is '@' or '$')
                {
                    if (code[j] == '@')
                        verbatim = true;
                    else
                        interpolated = true;
                    j++;
                }

                if (j < n && code[j] == '"')
                {
                    var stop = SkipString(code, j, verbatim, interpolated);
                    spans.Add((i, stop));
                    i = stop;
                    continue;
                }
            }

            i++;
        }

        return spans;
    }

    /// <summary>The index just past the <c>}</c> that closes the <c>{</c> at <paramref name="open"/>, ignoring braces inside literals.</summary>
    public static int MatchingBrace(string code, int open, IReadOnlyList<(int Start, int End)> spans)
    {
        var depth = 0;
        var span = 0;
        for (var i = open; i < code.Length; i++)
        {
            while (span < spans.Count && spans[span].End <= i)
                span++;
            if (span < spans.Count && spans[span].Start <= i)
            {
                i = spans[span].End - 1;
                continue;
            }

            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return i + 1;
        }

        return code.Length;
    }

    /// <summary>The index where the statement containing <paramref name="index"/> starts: just past the previous <c>;</c>, <c>{</c> or <c>}</c>.</summary>
    public static int StatementStart(string code, int index) =>
        index == 0 ? 0 : Math.Max(code.LastIndexOf(';', index - 1), Math.Max(code.LastIndexOf('{', index - 1), code.LastIndexOf('}', index - 1))) + 1;

    /// <summary>
    /// The statement that contains <paramref name="index"/>: from the previous <c>;</c>, <c>{</c> or <c>}</c> to the next <c>;</c>.
    /// Approximate by design; the guards use it only to collect the identifiers a call names.
    /// </summary>
    public static string StatementAround(string code, int index)
    {
        var from = index == 0 ? -1 : Math.Max(code.LastIndexOf(';', index - 1), Math.Max(code.LastIndexOf('{', index - 1), code.LastIndexOf('}', index - 1)));
        var start = from + 1;
        var end = code.IndexOf(';', index);
        return end < 0 ? code[start..] : code[start..end];
    }
}
