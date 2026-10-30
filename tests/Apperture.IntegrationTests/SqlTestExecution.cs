namespace Apperture.IntegrationTests;

/// <summary>
/// What a SQL Server test does when it cannot run against SQL Server: it fails. There is no skip, no optional mode and no switch
/// that turns the failure off, so a test can never report Passed solely because SQL Server was unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Every helper that cannot reach or prepare SQL Server throws <see cref="SqlTestUnavailableException"/> (built by
/// <see cref="Unavailable"/>): <see cref="SqlServerTestConfiguration.RequireConnectionString"/>,
/// <see cref="SqlServerTestConfiguration.EnsureTestDatabaseExists"/>, <see cref="SqlServerScratchDatabase.CreateAsync"/>,
/// <c>AuditSqlScratch.CreateAsync</c> and the Management Host fixtures. The exception is deliberately not an
/// <see cref="InvalidOperationException"/>: older helpers catch that type to mean "unreachable", and must not be able to turn the
/// failure back into a skip. A target refused by <c>SqlTestTargetGuard</c> is a different exception and likewise propagates.
/// </para>
/// <para>
/// <c>APPERTURE_REQUIRE_SQL_TESTS</c> is retired: strict behaviour is the only behaviour, the variable is no longer read, and
/// setting it to any value changes nothing. It can be deleted from any script or workflow that still sets it.
/// </para>
/// <para>
/// Two independent defences keep this true. <c>SqlSoftSkipGuardTests</c> refuses, in source, the silent exits it can recognise.
/// <c>scripts/dev/Test-SqlExecutionHonesty.ps1</c> runs the whole assembly against an unreachable server and refuses any Passed
/// test that is not on the reviewed non-SQL allow-list, which catches false passes the source scan cannot see.
/// </para>
/// </remarks>
public static class SqlTestExecution
{
    /// <summary>
    /// A well-formed connection string that names no real server, for a wiring test that builds the service provider and resolves
    /// services but never opens a connection. A test that needs SQL Server uses
    /// <c>SqlServerTestConfiguration.RequireConnectionString</c>.
    /// </summary>
    public const string NonConnectingConnectionString =
        "Server=sql-not-used.invalid,1433;Database=Apperture_WiringOnly;User Id=wiring;Password=not-a-credential;TrustServerCertificate=True;Connect Timeout=1;";

    /// <summary>The failure for a SQL test that cannot run against SQL Server. Always <c>throw</c> the result.</summary>
    public static SqlTestUnavailableException Unavailable(string reason, Exception? cause = null) =>
        cause is null
            ? new SqlTestUnavailableException($"This SQL Server test cannot run against SQL Server, so it fails: {reason}")
            : new SqlTestUnavailableException($"This SQL Server test cannot run against SQL Server, so it fails: {reason}", cause);
}

/// <summary>
/// A SQL test could not run against SQL Server. Not an <see cref="InvalidOperationException"/>, so the helpers that catch that
/// type to mean "unreachable" cannot swallow it.
/// </summary>
public sealed class SqlTestUnavailableException : Exception
{
    public SqlTestUnavailableException(string message)
        : base(message)
    {
    }

    public SqlTestUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
