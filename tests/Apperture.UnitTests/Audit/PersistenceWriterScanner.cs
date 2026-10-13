using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.UnitTests.Audit;

/// <summary>What the writer scanner needs to know about the context: the in-scope sets, their entity types and every other set.</summary>
/// <param name="InScopeSets">The DbSet names (also the table names) whose writers are reviewed.</param>
/// <param name="SetToEntity">The element type name of each in-scope set.</param>
/// <param name="AllDbSets">Every DbSet name of the context (a write to any of them is a persist signal).</param>
internal sealed record WriterScanModel(
    IReadOnlyList<string> InScopeSets,
    IReadOnlyDictionary<string, string> SetToEntity,
    IReadOnlyCollection<string> AllDbSets)
{
    public static WriterScanModel From(Type context, IReadOnlyList<string> inScopeSets)
    {
        var all = DbSetsOf(context);
        var inScope = inScopeSets.Where(all.ContainsKey).ToDictionary(s => s, s => all[s], StringComparer.Ordinal);
        return new WriterScanModel(inScopeSets, inScope, all.Keys.ToList());
    }

    /// <summary>The DbSet properties of a context: property name to element type name.</summary>
    public static Dictionary<string, string> DbSetsOf(Type context) =>
        context.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(p => p.Name, p => p.PropertyType.GetGenericArguments()[0].Name, StringComparer.Ordinal);
}

/// <summary>What the scanner found in one file.</summary>
internal sealed class FileScan(string path)
{
    public string Path { get; } = path;

    /// <summary>The in-scope sets the file writes, by any recognised idiom.</summary>
    public SortedSet<string> Sets { get; } = new(StringComparer.Ordinal);

    /// <summary>How each set was recognised, for the failure message and the self-tests.</summary>
    public SortedSet<string> Idioms { get; } = new(StringComparer.Ordinal);

    /// <summary>The in-scope entity types the file names in code.</summary>
    public SortedSet<string> NamedEntities { get; } = new(StringComparer.Ordinal);

    /// <summary>The persist signals the file carries (SaveChanges, ExecuteUpdate, a context-level verb, a state assignment, a write SQL literal).</summary>
    public SortedSet<string> PersistSignals { get; } = new(StringComparer.Ordinal);

    /// <summary>The second, file-level rule: a file that names an in-scope entity type and persists anything must be reviewed.</summary>
    public bool NamesEntityAndPersists => NamedEntities.Count > 0 && PersistSignals.Count > 0;
}

/// <summary>
/// The identifier-based scan behind guard G5 (writers of the in-scope tables). It takes <see cref="SourceFile"/> values (a path and
/// a text), so the self-tests can feed it synthetic files and the repository walk feeds it the production tree. It parses no C#.
///
/// <para>Rule one (set level): a file writes an in-scope set when it shows one of these idioms.</para>
/// <list type="bullet">
/// <item>A set (<c>.DbSet</c> or <c>Set&lt;Entity&gt;()</c>) followed in the same statement by a write verb (Add, Remove, Update, Attach,
/// their range and async forms, ExecuteUpdate, ExecuteDelete).</item>
/// <item>A tracked read of a set (no AsNoTracking, no terminal aggregate) in a file that persists. A projection with <c>.Select(</c> is
/// not an aggregate: an identity or navigation projection still tracks, so it counts. Only a terminal aggregate, or a
/// <c>Select(p =&gt; new ...)</c> that never uses its parameter bare, is read as untracked (a navigation entity carried inside a new
/// shape is a residual limit).</item>
/// <item>A local that holds the set, a queryable of it, or an entity read from it, and a later write verb on the local, or the local
/// passed to a context-level verb.</item>
/// <item>A context-level verb (<c>_db.Add(e)</c>, AddRange, Update, UpdateRange, Remove, RemoveRange, Attach) or a state assignment
/// (<c>Entry(e).State = EntityState.Added|Modified|Deleted</c>, <c>IsModified = true</c>). The target is the in-scope entity types or
/// locals the statement names; a statement that names none is attributed to every in-scope entity type the file names.</item>
/// <item>A SQL string literal in a file that executes raw SQL, with a write statement whose target is an in-scope table, directly
/// (<c>UPDATE Integrations</c>, <c>[dbo].[Integrations]</c>) or through an alias (<c>UPDATE i ... FROM Integrations i</c>); or a literal
/// that is just a table name in such a file.</item>
/// </list>
///
/// <para>Rule two (file level): a file that names an in-scope entity type and carries any persist signal must be reviewed, whether or
/// not rule one resolved the set. This closes the idioms rule one cannot resolve.</para>
///
/// <para>Residual limits, stated so nobody reads the guard as a proof: a table name built at run time (concatenated, a variable,
/// <c>nameof</c>, a format argument) evades the SQL check; a write through a type the file never names (an entity handed in by another
/// class, a generic repository base) evades both rules in that file; an entity set aliased through a property or a method return is not
/// followed; a state assignment through <c>using static EntityState</c> is not seen; and a change recorded only by a database trigger is
/// out of reach. Each of these still has to pass code review; the guard narrows what review must catch.</para>
/// </summary>
internal static class PersistenceWriterScanner
{
    private const string Verbs = "Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach|AttachRange|ExecuteUpdate|ExecuteUpdateAsync|ExecuteDelete|ExecuteDeleteAsync";

    private static readonly Regex WriteVerb = new(@"\.\s*(" + Verbs + @")\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex ContextVerb = new(
        @"\b(?<recv>\w+)\s*\.\s*(?<verb>Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach|AttachRange)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex Materializer = new(
        @"\.\s*(FirstOrDefaultAsync|SingleOrDefaultAsync|FirstAsync|SingleAsync|FindAsync|ToListAsync|ToArrayAsync|AsAsyncEnumerable|FromSql\w*)\s*\(",
        RegexOptions.CultureInvariant);

    // Terminal aggregates only. A Select projection is not here: see the class remarks.
    private static readonly Regex Aggregate = new(
        @"\.\s*(CountAsync|AnyAsync|LongCountAsync|SumAsync|MaxAsync|MinAsync|AllAsync|AverageAsync)\s*\(",
        RegexOptions.CultureInvariant);

    // A Select whose lambda builds a new shape: Select(g => new { ... }), Select(x => new Dto(...)). EF tracks only entities it
    // materializes from a query root, so a new shape tracks nothing unless it carries an entity itself (the parameter used bare, or a
    // navigation of it). The parameter used bare is checked below; a navigation entity inside a new shape is a residual limit.
    private static readonly Regex ShapedSelect = new(
        @"\.\s*Select\s*\(\s*\(?\s*(?<p>\w+)\s*\)?\s*=>\s*new\b(?<body>[^;]*)",
        RegexOptions.CultureInvariant);

    /// <summary>A terminal aggregate, or a projection into a new shape that does not carry its row entity: the result is not a tracked entity.</summary>
    private static bool Untracked(string tail)
    {
        if (Aggregate.IsMatch(tail))
            return true;

        foreach (Match m in ShapedSelect.Matches(tail))
        {
            var parameter = Regex.Escape(m.Groups["p"].Value);
            if (!Regex.IsMatch(m.Groups["body"].Value, $@"\b{parameter}\b(?!\s*\.)", RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    private static readonly Regex Persist =new(@"(?<s>SaveChanges(Async)?)\s*\(|(?<s>ExecuteUpdate)|(?<s>ExecuteDelete)|(?<s>ExecuteSql)", RegexOptions.CultureInvariant);

    private static readonly Regex StateWrite = new(
        @"\bState\s*=\s*(?:Microsoft\.EntityFrameworkCore\.)?EntityState\s*\.\s*(?<state>Added|Modified|Deleted)\b|\bIsModified\s*=\s*true\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex DefaultContextName = new(@"^_?(db|dbContext|context|ctx)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DeclaredContext = new(
        @"\b\w*DbContext\??\s+(?<n>\w+)|(?<n>\w+)\s*=\s*(?:await\s+)?[\w.]*CreateDbContext(?:Async)?\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentPrefix = new(
        @"(?<alias>\w+)\s*=\s*(?:await\s+)?(?:[\w?]+\s*\.\s*)*[\w?]*\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RawSignal = new(
        @"CommandText|ExecuteSql|SqlCommand|FromSql|ExecuteNonQuery|CreateCommand",
        RegexOptions.CultureInvariant);

    private static readonly Regex SqlWrite = new(
        @"\b(?<verb>INSERT(?:\s+INTO)?|UPDATE|DELETE(?:\s+FROM)?|MERGE(?:\s+INTO)?|TRUNCATE\s+TABLE)\s+(?<target>(?:\[?\w+\]?\s*\.\s*)?\[?\w+\]?)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Migrations, the audit catalog and producers, and test-support projects never write the reviewed tables at run time.</summary>
    public static bool IsOutOfScope(string path) =>
        path.Contains("/Migrations/", StringComparison.Ordinal)
        || path.Contains("/Apperture.Core.Application/Audit/", StringComparison.Ordinal)
        || path.Contains(".Testing/", StringComparison.Ordinal)
        || path.Contains("TestSupport", StringComparison.Ordinal);

    public static Dictionary<string, FileScan> ScanAll(IEnumerable<SourceFile> files, WriterScanModel model)
    {
        var result = new Dictionary<string, FileScan>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var scan = Scan(file, model);
            if (scan.Sets.Count > 0 || scan.NamesEntityAndPersists)
                result[file.RelativePath] = scan;
        }

        return result;
    }

    private sealed record Alias(string Set, bool Queryable, bool NoTracking);

    public static FileScan Scan(SourceFile file, WriterScanModel model)
    {
        var scan = new FileScan(file.RelativePath);
        if (IsOutOfScope(file.RelativePath))
            return scan;

        var code = file.Code;
        var entityToSet = model.SetToEntity.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
        foreach (var entity in entityToSet.Keys)
        {
            if (file.Text.Contains(entity, StringComparison.Ordinal) && CSharpSource.ContainsWord(code, entity))
                scan.NamedEntities.Add(entity);
        }

        var namedSets = scan.NamedEntities.Select(e => entityToSet[e]).ToList();
        var eligible = file.Text.Contains("DbContext", StringComparison.Ordinal) || scan.NamedEntities.Count > 0;
        if (eligible)
            ScanEntityFramework(code, model, entityToSet, namedSets, scan);

        if (RawSignal.IsMatch(code))
            ScanRawSql(code, model, scan);

        return scan;
    }

    private static void ScanEntityFramework(
        string code, WriterScanModel model, Dictionary<string, string> entityToSet, List<string> namedSets, FileScan scan)
    {
        foreach (Match m in Persist.Matches(code))
            scan.PersistSignals.Add(m.Groups["s"].Value);
        var persists = scan.PersistSignals.Count > 0;

        var contextNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in DeclaredContext.Matches(code))
            contextNames.Add(m.Groups["n"].Value);

        var alternation = string.Join("|", model.InScopeSets.Where(model.SetToEntity.ContainsKey));
        var entities = string.Join("|", model.SetToEntity.Values.Select(Regex.Escape));
        var setAccess = new Regex($@"\.\s*(?<set>{alternation})\b|\bSet<(?<entity>{entities})>\s*\(\s*\)", RegexOptions.CultureInvariant);
        var aliases = new Dictionary<string, Alias>(StringComparer.Ordinal);

        void Write(string set, string idiom)
        {
            scan.Sets.Add(set);
            scan.Idioms.Add($"{set}: {idiom}");
        }

        foreach (Match match in setAccess.Matches(code))
        {
            var set = match.Groups["set"].Success ? match.Groups["set"].Value : entityToSet[match.Groups["entity"].Value];
            var tail = CSharpSource.TailStatement(code, match.Index);
            var noTracking = tail.Contains("AsNoTracking", StringComparison.Ordinal);
            var materializes = Materializer.IsMatch(tail);
            var aggregates = Untracked(tail);

            if (WriteVerb.IsMatch(tail))
            {
                Write(set, "set followed by a write verb");
                scan.PersistSignals.Add("set write verb");
            }
            else if (persists && !noTracking && materializes && !aggregates)
            {
                Write(set, "tracked read in a file that persists");
            }

            var prefix = code[CSharpSource.StatementStart(code, match.Index)..match.Index];
            var assignment = AssignmentPrefix.Match(prefix);
            if (assignment.Success)
                aliases[assignment.Groups["alias"].Value] = new Alias(set, Queryable: !materializes && !aggregates, noTracking);
        }

        foreach (var (name, alias) in aliases)
        {
            var use = new Regex($@"\b{Regex.Escape(name)}\s*\.\s*(?<verb>{Verbs})\s*\(", RegexOptions.CultureInvariant);
            if (alias.Queryable && use.IsMatch(code))
            {
                Write(alias.Set, $"write verb on the local '{name}' that holds the set");
                scan.PersistSignals.Add("local write verb");
            }

            if (alias.Queryable && persists && !alias.NoTracking)
            {
                var read = new Regex($@"\b{Regex.Escape(name)}\s*\.\s*[^;]*", RegexOptions.CultureInvariant);
                foreach (Match use2 in read.Matches(code))
                {
                    var tail = use2.Value;
                    if (Materializer.IsMatch(tail) && !tail.Contains("AsNoTracking", StringComparison.Ordinal) && !Untracked(tail))
                        Write(alias.Set, $"tracked read through the local '{name}'");
                }
            }
        }

        foreach (Match match in ContextVerb.Matches(code))
        {
            var receiver = match.Groups["recv"].Value;
            if (!DefaultContextName.IsMatch(receiver) && !contextNames.Contains(receiver))
                continue;

            scan.PersistSignals.Add($"context {match.Groups["verb"].Value}");
            var tail = CSharpSource.TailStatement(code, match.Index);
            foreach (var set in Targets(tail, entityToSet, aliases, namedSets))
                Write(set, $"context-level {match.Groups["verb"].Value}");
        }

        foreach (Match match in StateWrite.Matches(code))
        {
            scan.PersistSignals.Add("entry state assignment");
            var start = CSharpSource.StatementStart(code, match.Index);
            var statement = code[start..Math.Max(match.Index, start)] + CSharpSource.TailStatement(code, match.Index);
            foreach (var set in Targets(statement, entityToSet, aliases, namedSets))
                Write(set, "entry state set to Added, Modified or Deleted");
        }

        // A write verb on a set outside the reviewed list is still a persist signal for the file-level rule.
        var others = model.AllDbSets.Where(s => !model.SetToEntity.ContainsKey(s)).ToList();
        if (others.Count > 0)
        {
            var otherAccess = new Regex($@"\.\s*(?:{string.Join("|", others)})\b", RegexOptions.CultureInvariant);
            foreach (Match match in otherAccess.Matches(code))
            {
                if (WriteVerb.IsMatch(CSharpSource.TailStatement(code, match.Index)))
                    scan.PersistSignals.Add("write to another set");
            }
        }
    }

    /// <summary>
    /// The in-scope sets a context-level statement writes: the entity types it names and the locals it names that hold an in-scope
    /// set. A statement that names neither is attributed to every in-scope entity type the file names (a superset, never a miss).
    /// </summary>
    private static IEnumerable<string> Targets(
        string statement, Dictionary<string, string> entityToSet, Dictionary<string, Alias> aliases, List<string> namedSets)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (entity, set) in entityToSet)
        {
            if (CSharpSource.ContainsWord(statement, entity))
                found.Add(set);
        }

        foreach (var (name, alias) in aliases)
        {
            if (CSharpSource.ContainsWord(statement, name))
                found.Add(alias.Set);
        }

        return found.Count > 0 ? found : namedSets;
    }

    private static void ScanRawSql(string code, WriterScanModel model, FileScan scan)
    {
        var tables = model.InScopeSets;
        foreach (var (start, end) in CSharpSource.StringSpans(code))
        {
            var literal = code[start..end];

            foreach (Match write in SqlWrite.Matches(literal))
            {
                var target = Unbracket(write.Groups["target"].Value);
                var table = tables.FirstOrDefault(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
                if (table is null)
                {
                    // UPDATE i SET ... FROM Integrations i: the target is an alias of a table named in a FROM or JOIN.
                    table = tables.FirstOrDefault(t => Regex.IsMatch(
                        literal,
                        $@"\b(FROM|JOIN)\s+(?:\[?\w+\]?\s*\.\s*)?\[?{Regex.Escape(t)}\]?\s+(?:AS\s+)?\[?{Regex.Escape(target)}\]?\b",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
                }

                if (table is null)
                    continue;

                scan.PersistSignals.Add("raw SQL write");
                scan.Sets.Add(table);
                scan.Idioms.Add($"{table}: raw SQL {write.Groups["verb"].Value.ToUpperInvariant()} {target}");
            }

            // A literal that is just a table name, in a file that executes raw SQL: the statement is built around it.
            var bare = Unbracket(literal.Trim('"', '@', '$', ' '));
            var named = tables.FirstOrDefault(t => string.Equals(t, bare, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                scan.PersistSignals.Add("raw SQL table literal");
                scan.Sets.Add(named);
                scan.Idioms.Add($"{named}: raw-command file names the table as a string");
            }
        }
    }

    private static string Unbracket(string name)
    {
        var last = name.Split('.').Last().Trim();
        return last.Trim('[', ']', '"', ' ');
    }
}
