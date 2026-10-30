using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Apperture.SqlTestTargets;

/// <summary>
/// Thrown when test infrastructure is pointed at a SQL target that is not a disposable test target. Deliberately not an
/// <see cref="InvalidOperationException"/>: older SQL tests catch that type to soft-skip an unreachable server, and a refusal must
/// never be reported as a skipped (passing) test.
/// </summary>
public sealed class SqlTestTargetRefusedException : Exception
{
    public SqlTestTargetRefusedException(string message) : base(message)
    {
    }
}

/// <summary>
/// The one place that decides whether test infrastructure may create, migrate, seed or drop a catalog on a SQL server. It applies
/// to test and developer tooling only; product runtime database behaviour is untouched. The source file is shared by the test
/// projects that own SQL test targets (a linked file, so there is a single rule set).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A catalog being created or dropped must be named <c>Apperture_</c> plus a suffix of ASCII letters, digits and underscore.
/// The bare development catalog <c>Apperture</c> is never a test catalog, in any spelling.</item>
/// <item>A governance catalog must also end in <c>Tests</c>.</item>
/// <item>Host port 14333 (the development SQL server) is refused whatever the host name, protocol prefix or keyword alias.</item>
/// <item>A target the guard cannot place is refused: an unparseable string, an empty or malformed address, a port that is not a
/// number in range, a named instance without a port, or a non-TCP protocol.</item>
/// <item>Only the protected-port, named-instance and non-TCP refusals can be overridden, by
/// <see cref="ProtectedTargetOptInVariable"/>=1 in the process environment (never read from <c>.env</c>). The opt-in never relaxes
/// the catalog rules.</item>
/// </list>
/// Messages name the data source and the catalog and never a user, password or other credential.
/// </remarks>
public static class SqlTestTargetGuard
{
    public const string ProtectedTargetOptInVariable = "APPERTURE_ALLOW_PROTECTED_SQL_TEST_TARGET";
    public const int DevelopmentSqlPort = 14333;
    public const string DisposableCatalogPrefix = "Apperture_";
    public const string GovernanceCatalogSuffix = "Tests";

    private const string DevelopmentCatalog = "Apperture";
    private const int MaxCatalogLength = 128;

    private static readonly string[] TcpPrefixes = ["tcp:", "admin:"];
    private static readonly string[] NonTcpPrefixes = ["np:", "lpc:", "via:"];

    /// <summary>True only when the variable is exactly <c>1</c>.</summary>
    public static bool ProtectedTargetOptInEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(ProtectedTargetOptInVariable), "1", StringComparison.Ordinal);

    /// <summary>Refuses a server that is not a disposable test server (the connection's data source and failover partner).</summary>
    public static void EnsureServerAllowed(string connectionString, string operation, bool? protectedTargetOptIn = null)
    {
        var optIn = protectedTargetOptIn ?? ProtectedTargetOptInEnabled;
        if (!TryParse(connectionString, out var builder))
            throw Refused(operation, Describe(connectionString), new Refusal("the connection string is unparseable", false));

        var refusal = ServerRefusal(builder, optIn);
        if (refusal is not null)
            throw Refused(operation, Describe(builder), refusal.Value);
    }

    /// <summary>Refuses a catalog name that is not a disposable test catalog.</summary>
    public static void EnsureDisposableCatalog(string catalog, string operation, bool requireTestsSuffix = false)
    {
        var refusal = CatalogRefusal(catalog, requireTestsSuffix);
        if (refusal is not null)
            throw Refused(operation, "database=" + Sanitize(catalog), refusal.Value);
    }

    /// <summary>
    /// Checks the server and the catalog the connection string names (<c>Initial Catalog</c>). Call this before a catalog is
    /// created or migrated.
    /// </summary>
    public static void EnsureDisposableTarget(
        string connectionString, string operation, bool requireTestsSuffix = false, bool? protectedTargetOptIn = null)
    {
        EnsureServerAllowed(connectionString, operation, protectedTargetOptIn);

        var builder = new SqlConnectionStringBuilder(connectionString);
        var refusal = CatalogRefusal(builder.InitialCatalog, requireTestsSuffix);
        if (refusal is not null)
            throw Refused(operation, Describe(builder), refusal.Value);
    }

    /// <summary>
    /// Checks the server a connection string names and a separately named catalog. Call this before a catalog is dropped or
    /// altered: the base connection string may legitimately default to the development catalog (CI does), so only the catalog
    /// actually being operated on is judged.
    /// </summary>
    public static void EnsureDisposableCatalogOnServer(
        string serverConnectionString, string catalog, string operation, bool requireTestsSuffix = false, bool? protectedTargetOptIn = null)
    {
        EnsureServerAllowed(serverConnectionString, operation, protectedTargetOptIn);

        var refusal = CatalogRefusal(catalog, requireTestsSuffix);
        if (refusal is not null)
        {
            var server = new SqlConnectionStringBuilder(serverConnectionString).DataSource;
            throw Refused(operation, $"data source={Sanitize(server)}; database={Sanitize(catalog)}", refusal.Value);
        }
    }

    /// <summary>The data source and catalog of a connection string, for diagnostics. Never a user, password or other keyword.</summary>
    public static string Describe(string connectionString) =>
        TryParse(connectionString, out var builder) ? Describe(builder) : "(unparseable connection string)";

    private static string Describe(SqlConnectionStringBuilder builder) =>
        $"data source={Sanitize(builder.DataSource)}; database={Sanitize(builder.InitialCatalog)}";

    private readonly record struct Refusal(string Reason, bool OptInApplies);

    private static bool TryParse(string connectionString, out SqlConnectionStringBuilder builder)
    {
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
            return true;
        }
        catch (ArgumentException)
        {
            builder = new SqlConnectionStringBuilder();
            return false;
        }
    }

    private static SqlTestTargetRefusedException Refused(string operation, string description, Refusal refusal)
    {
        var message =
            $"Refusing to {operation}. SQL target [{description}] is not allowed: {refusal.Reason}. " +
            "Test infrastructure creates, migrates and drops only disposable catalogs (names starting 'Apperture_') on a disposable " +
            "test SQL server; the development database and the development SQL server are never test targets. " +
            "See tests/README.md, 'Test SQL target safety'.";
        if (refusal.OptInApplies)
        {
            message += $" For an exceptional, approved use set {ProtectedTargetOptInVariable}=1 in the process environment " +
                       "(it is not read from .env).";
        }

        return new SqlTestTargetRefusedException(message);
    }

    // ---- catalog ---------------------------------------------------------------------------------------------------------------

    private static Refusal? CatalogRefusal(string? catalog, bool requireTestsSuffix)
    {
        if (string.IsNullOrWhiteSpace(catalog))
            return new Refusal("no catalog name was given, so the target database cannot be identified", false);

        // Whatever quoting or padding is put around it, the development database is never a test catalog.
        if (catalog.Trim().Trim('[', ']', '"', '\'', ' ').Equals(DevelopmentCatalog, StringComparison.OrdinalIgnoreCase))
            return new Refusal($"the development database '{DevelopmentCatalog}' is never a disposable test catalog", false);

        if (catalog.Length > MaxCatalogLength)
            return new Refusal($"the catalog name is longer than the SQL Server limit of {MaxCatalogLength} characters", false);

        if (!catalog.StartsWith(DisposableCatalogPrefix, StringComparison.OrdinalIgnoreCase) || catalog.Length == DisposableCatalogPrefix.Length)
        {
            return new Refusal(
                $"the catalog is not a disposable test catalog: its name must start with '{DisposableCatalogPrefix}' followed by a suffix",
                false);
        }

        foreach (var c in catalog)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                return new Refusal("the catalog name contains characters other than ASCII letters, digits and underscore", false);
            }
        }

        if (requireTestsSuffix && !catalog.EndsWith(GovernanceCatalogSuffix, StringComparison.OrdinalIgnoreCase))
            return new Refusal($"a governance test catalog name must end with '{GovernanceCatalogSuffix}'", false);

        return null;
    }

    // ---- server ----------------------------------------------------------------------------------------------------------------

    private static Refusal? ServerRefusal(SqlConnectionStringBuilder builder, bool optIn)
    {
        var primary = DataSourceRefusal("server", builder.DataSource, optIn);
        if (primary is not null)
            return primary;

        return string.IsNullOrWhiteSpace(builder.FailoverPartner)
            ? null
            : DataSourceRefusal("failover partner", builder.FailoverPartner, optIn);
    }

    private static Refusal? DataSourceRefusal(string label, string? raw, bool optIn)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
            return new Refusal($"the {label} is empty, so the target cannot be identified", false);

        var rest = text;
        foreach (var prefix in NonTcpPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return optIn
                    ? null
                    : new Refusal($"the {label} uses the '{prefix}' protocol, so its port cannot be checked", true);
            }
        }

        foreach (var prefix in TcpPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = text[prefix.Length..].Trim();
                break;
            }
        }

        string hostPart;
        string? portText = null;
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']');
            if (close < 0)
                return Malformed(label);

            hostPart = rest[..(close + 1)];
            var tail = rest[(close + 1)..].Trim();
            if (tail.Length > 0)
            {
                if (tail[0] != ',')
                    return Malformed(label);

                portText = tail[1..].Trim();
            }
        }
        else
        {
            var comma = rest.LastIndexOf(',');
            if (comma >= 0)
            {
                hostPart = rest[..comma].Trim();
                portText = rest[(comma + 1)..].Trim();
            }
            else
            {
                hostPart = rest;
            }
        }

        if (hostPart.Length == 0 || hostPart.Contains(','))
            return Malformed(label);

        // LocalDB is a per-user local instance, never a shared server and never the development SQL server.
        if (portText is null
            && (hostPart.Equals("(localdb)", StringComparison.OrdinalIgnoreCase)
                || hostPart.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string? instance = null;
        var slash = hostPart.IndexOf('\\');
        if (slash >= 0)
        {
            instance = hostPart[(slash + 1)..];
            if (slash == 0)
                return Malformed(label);
        }

        int? port = null;
        if (portText is not null)
        {
            if (portText.Length == 0 || portText.Length > 10 || !portText.All(c => c is >= '0' and <= '9')
                || !long.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed is < 1 or > 65535)
            {
                return Malformed(label);
            }

            port = (int)parsed;
        }

        if (port == DevelopmentSqlPort)
        {
            return optIn
                ? null
                : new Refusal($"the {label} port {DevelopmentSqlPort} is reserved for the development SQL server", true);
        }

        if (port is null && !string.IsNullOrEmpty(instance))
        {
            return optIn
                ? null
                : new Refusal(
                    $"the {label} names an instance without a port, so SQL Browser decides the port and it cannot be checked", true);
        }

        return null;
    }

    private static Refusal Malformed(string label) =>
        new($"the {label} is not a recognised address form, so the target cannot be placed", false);

    /// <summary>Replaces control characters so a hostile value cannot forge lines in a log or an assertion message.</summary>
    private static string Sanitize(string? value) =>
        value is null ? string.Empty : string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
        });
}
