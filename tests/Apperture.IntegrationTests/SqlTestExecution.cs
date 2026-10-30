using System.Collections.Concurrent;

namespace Apperture.IntegrationTests;

/// <summary>
/// The one place that decides what a SQL Server test does when it cannot run against SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// With <c>APPERTURE_REQUIRE_SQL_TESTS=1</c> in the process environment (the evidence mode: CI and any run that is offered as proof),
/// <see cref="NotExecuted"/> throws <see cref="SqlTestExecutionRequiredException"/>, so a test that did not run against SQL Server
/// fails and can never be counted as passing. The exception is deliberately not an <see cref="InvalidOperationException"/>: older
/// helpers catch that type to mean "endpoint unreachable", and must not be able to turn the failure back into a skip.
/// </para>
/// <para>
/// Without the variable (optional mode, for a developer running a subset of the suite on a machine that has no test SQL Server) a
/// test that cannot reach SQL Server returns early. xunit 2 has no dynamic skip, so it reports that test as <b>Passed</b>; it is
/// not evidence of anything. Each such return is announced here, on standard error as
/// <c>NOT EXECUTED (soft skip): ...</c>, and counted in <see cref="NotExecutedCount"/>. A run that is to be believed sets the
/// variable. Only a test that returns through <see cref="NotExecuted"/> may skip; <c>SqlSoftSkipGuardTests</c> fails the build of
/// any other silent exit.
/// </para>
/// <para>The variable is read from the process environment only, never from <c>.env</c>, like the protected-target opt-in.</para>
/// </remarks>
public static class SqlTestExecution
{
    /// <summary>Process-environment switch: when exactly <c>1</c>, a SQL test that cannot run fails instead of returning early.</summary>
    public const string RequireVariable = "APPERTURE_REQUIRE_SQL_TESTS";

    /// <summary>
    /// A well-formed connection string that names no real server, for a wiring test that builds the service provider and resolves
    /// services but never opens a connection. It stands in for the LocalDB string such tests used to carry; a test that needs
    /// SQL Server uses <see cref="ConnectionStringOrNotExecuted"/> or <c>SqlServerTestConfiguration.RequireConnectionString</c>.
    /// </summary>
    public const string NonConnectingConnectionString =
        "Server=sql-not-used.invalid,1433;Database=Apperture_WiringOnly;User Id=wiring;Password=not-a-credential;TrustServerCertificate=True;Connect Timeout=1;";

    private static readonly ConcurrentQueue<string> Ledger = new();

    /// <summary>True when the process environment sets <see cref="RequireVariable"/> to exactly <c>1</c>.</summary>
    public static bool Required => Environment.GetEnvironmentVariable(RequireVariable) == "1";

    /// <summary>How many tests have returned early in optional mode since the process started.</summary>
    public static int NotExecutedCount => Ledger.Count;

    /// <summary>The reasons given for each early return in optional mode, oldest first.</summary>
    public static IReadOnlyCollection<string> NotExecutedReasons => Ledger.ToArray();

    /// <summary>
    /// A reachable test SQL Server connection string. In optional mode, when there is none, announces the skip and returns
    /// <see langword="null"/> (the caller returns); in required mode throws. A refused target (<c>SqlTestTargetRefusedException</c>)
    /// propagates in both.
    /// </summary>
    public static string? ConnectionStringOrNotExecuted()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is not null)
            return connectionString;

        NotExecuted("no reachable SQL Server test endpoint is configured. " + SqlServerTestConfiguration.DescribeActiveConfiguration());
        return null;
    }

    /// <summary>
    /// Declares that the calling test is returning without having run against SQL Server. Throws in required mode; otherwise
    /// records the reason, writes it to standard error and returns, and the caller must return at once.
    /// </summary>
    public static void NotExecuted(string reason, Exception? cause = null)
    {
        if (Required)
        {
            var message = $"SQL Server proof is required ({RequireVariable}=1) but this test could not run against SQL Server: {reason}";
            throw cause is null
                ? new SqlTestExecutionRequiredException(message)
                : new SqlTestExecutionRequiredException(message, cause);
        }

        Ledger.Enqueue(reason);
        Console.Error.WriteLine("NOT EXECUTED (soft skip): " + reason);
    }
}

/// <summary>
/// A SQL test could not run against SQL Server while <c>APPERTURE_REQUIRE_SQL_TESTS=1</c>. Not an
/// <see cref="InvalidOperationException"/>, so the helpers that catch that type to mean "unreachable" cannot swallow it.
/// </summary>
public sealed class SqlTestExecutionRequiredException : Exception
{
    public SqlTestExecutionRequiredException(string message)
        : base(message)
    {
    }

    public SqlTestExecutionRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
