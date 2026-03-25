namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Stable identifiers for SQL seed data and test auth headers. Keep in sync with <see cref="ManagementHostGovernanceFixture"/>.
/// </summary>
internal static class ManagementGovernanceTestConstants
{
    internal const string ConnectionStringEnvironmentVariable = "APPERTURE_TEST_SQL";

    internal const string DefaultLocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=Apperture_ManagementGovernanceTests;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>24-char integration id seeded for governed integration HTTP tests.</summary>
    internal const string SeededIntegrationId = "intgovertestseed00000001";

    internal const string HelloWorldModuleId = "Sample.HelloWorld";

    // External ids (identity link / NameIdentifier claim)
    internal const string ExternalAuthNoPerms = "test-governance-auth-no-perms";
    internal const string ExternalConnectorCatalogReader = "test-governance-connector-catalog-reader";
    internal const string ExternalIntegrationReader = "test-governance-integration-reader";
    internal const string ExternalIntegrationScheduleOperator = "test-governance-integration-schedule-op";

    /// <summary>integration.read + workflow.execute (Sample.HelloWorld module) for procedure launch HTTP tests (Epic 56A).</summary>
    internal const string ExternalProcedureLauncher = "test-governance-procedure-launcher";

    // User ids (24 chars)
    internal const string UserAuthNoPerms = "usrgovernauthnoperm00001";
    internal const string UserConnectorReader = "usrgoverncnctdefread0001";
    internal const string UserIntegrationReader = "usrgovernintegread000001";
    internal const string UserIntegrationSchedule = "usrgovernintegsched00001";

    internal const string UserProcedureLauncher = "usrgovernproclaunch00001";

    // Role ids
    internal const string RoleNoEffectivePermissions = "rolgovernemptyperm000001";
    internal const string RoleConnectorCatalogReader = "rolgoverncnctdefread0001";
    internal const string RoleIntegrationReader = "rolgovernintegread000001";
    internal const string RoleIntegrationSchedule = "rolgovernintegsched00001";

    internal const string RoleProcedureLauncher = "rolgovernproclaunch00001";

    // Identity link entity ids (<= 64; fixed for idempotent seed)
    internal const string LinkAuthNoPerms = "idlkgrvnauthnoperm000001";
    internal const string LinkConnectorReader = "idlkgrvncndefread0000001";
    internal const string LinkIntegrationReader = "idlkgrvnintegread0000001";
    internal const string LinkIntegrationSchedule = "idlkgrvnintegschd0000001";

    internal const string LinkProcedureLauncher = "idlkgrvnproclaunch000001";
}
