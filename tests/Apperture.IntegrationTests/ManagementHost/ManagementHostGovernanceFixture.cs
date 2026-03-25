using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Shared Management Host factory, SQL probe, and idempotent RBAC/integration seed for HTTP governance tests.
/// </summary>
public sealed class ManagementHostGovernanceFixture : IAsyncLifetime
{
    private const string ConnectorReaderPermissionsJson =
        """[{"action":"connector.definition.read","resource":{"resourceType":"ConnectorDefinition","scopeKey":"instance"}}]""";

    private const string IntegrationReaderPermissionsJson =
        """[{"action":"integration.read","resource":{"resourceType":"Integration","scopeKey":"instance"}}]""";

    private const string IntegrationSchedulePermissionsJson =
        """[{"action":"integration.read","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"schedule.manage","resource":{"resourceType":"Schedule","scopeKey":"instance"}}]""";

    private const string ProcedureLauncherPermissionsJson =
        """[{"action":"integration.read","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"workflow.execute","resource":{"resourceType":"WorkflowInstance","scopeKey":"module:Sample.HelloWorld"}},{"action":"operation.execute","resource":{"resourceType":"OperationDefinition","scopeKey":"module:Sample.HelloWorld"}}]""";

    private const string WorkflowOnlyProcedureLauncherPermissionsJson =
        """[{"action":"integration.read","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"workflow.execute","resource":{"resourceType":"WorkflowInstance","scopeKey":"module:Sample.HelloWorld"}}]""";

    private const string IntegrationAuthoringPermissionsJson =
        """[{"action":"integration.create","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"integration.read","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"integration.update","resource":{"resourceType":"Integration","scopeKey":"instance"}},{"action":"binding.assign","resource":{"resourceType":"Binding","scopeKey":"instance"}},{"action":"schedule.manage","resource":{"resourceType":"Schedule","scopeKey":"instance"}},{"action":"connection.create","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connection.read","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connection.update","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connector.definition.read","resource":{"resourceType":"ConnectorDefinition","scopeKey":"instance"}}]""";

    public string? ConnectionString { get; private set; }

    public bool SqlAvailable { get; private set; }

    public ManagementHostWebApplicationFactory? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ManagementGovernanceTestConstants.ConnectionStringEnvironmentVariable)
            ?? ManagementGovernanceTestConstants.DefaultLocalDbConnectionString;

        try
        {
            // Probe against master: Initial Catalog may not exist yet.
            var probeCs = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
            await using (var probe = new SqlConnection(probeCs))
            {
                await probe.OpenAsync().ConfigureAwait(false);
                // BootstrapValidationHostedService opens the full connection string before migrations; ensure the catalog exists.
                await EnsureTargetDatabaseExistsAsync(ConnectionString, probe).ConfigureAwait(false);
            }
        }
        catch
        {
            SqlAvailable = false;
            return;
        }

        SqlAvailable = true;
        Factory = new ManagementHostWebApplicationFactory(ConnectionString);

        using (var client = Factory.CreateClient())
        {
            _ = await client.GetAsync("/signin").ConfigureAwait(false);
        }

        await SeedAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public HttpClient CreateClient() =>
        Factory!.CreateClient();

    public HttpClient CreateAuthenticatedClient(string externalId)
    {
        var client = Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        return client;
    }

    private static async Task EnsureTargetDatabaseExistsAsync(string fullConnectionString, SqlConnection masterConnection)
    {
        var catalog = new SqlConnectionStringBuilder(fullConnectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(catalog))
            return;

        foreach (var c in catalog)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                throw new InvalidOperationException($"Refusing to auto-create database with unexpected name: {catalog}");
        }

        await using var cmd = masterConnection.CreateCommand();
        cmd.CommandText = $"""
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog}')
    CREATE DATABASE [{catalog}]
""";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task SeedAsync()
    {
        await using var scope = Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        if (!await db.Users.AnyAsync(u => u.Id == ManagementGovernanceTestConstants.UserAuthNoPerms).ConfigureAwait(false))
        {
        var now = DateTimeOffset.UtcNow;
        const int localSource = (int)IdentitySource.Local;

        db.Roles.AddRange(
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleNoEffectivePermissions,
                Name = "Governance test — empty permissions",
                Description = "Epic 55 harness",
                PermissionsJson = "[]",
                IsSystemRole = false
            },
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleConnectorCatalogReader,
                Name = "Governance test — connector catalog reader",
                Description = "Epic 55 harness",
                PermissionsJson = ConnectorReaderPermissionsJson,
                IsSystemRole = false
            },
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleIntegrationReader,
                Name = "Governance test — integration reader",
                Description = "Epic 55 harness",
                PermissionsJson = IntegrationReaderPermissionsJson,
                IsSystemRole = false
            },
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleIntegrationSchedule,
                Name = "Governance test — integration + schedules",
                Description = "Epic 55 harness",
                PermissionsJson = IntegrationSchedulePermissionsJson,
                IsSystemRole = false
            });

        db.Users.AddRange(
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserAuthNoPerms,
                DisplayName = "Governance test — auth no perms",
                IsBreakGlassEligible = false
            },
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserConnectorReader,
                DisplayName = "Governance test — connector reader",
                IsBreakGlassEligible = false
            },
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserIntegrationReader,
                DisplayName = "Governance test — integration reader",
                IsBreakGlassEligible = false
            },
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserIntegrationSchedule,
                DisplayName = "Governance test — integration schedule op",
                IsBreakGlassEligible = false
            });

        db.IdentityLinks.AddRange(
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkAuthNoPerms,
                UserId = ManagementGovernanceTestConstants.UserAuthNoPerms,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalAuthNoPerms
            },
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkConnectorReader,
                UserId = ManagementGovernanceTestConstants.UserConnectorReader,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalConnectorCatalogReader
            },
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkIntegrationReader,
                UserId = ManagementGovernanceTestConstants.UserIntegrationReader,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalIntegrationReader
            },
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkIntegrationSchedule,
                UserId = ManagementGovernanceTestConstants.UserIntegrationSchedule,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalIntegrationScheduleOperator
            });

        db.UserRoles.AddRange(
            new UserRoleEntity { UserId = ManagementGovernanceTestConstants.UserAuthNoPerms, RoleId = ManagementGovernanceTestConstants.RoleNoEffectivePermissions },
            new UserRoleEntity { UserId = ManagementGovernanceTestConstants.UserConnectorReader, RoleId = ManagementGovernanceTestConstants.RoleConnectorCatalogReader },
            new UserRoleEntity { UserId = ManagementGovernanceTestConstants.UserIntegrationReader, RoleId = ManagementGovernanceTestConstants.RoleIntegrationReader },
            new UserRoleEntity { UserId = ManagementGovernanceTestConstants.UserIntegrationSchedule, RoleId = ManagementGovernanceTestConstants.RoleIntegrationSchedule });

        db.Integrations.Add(
            new IntegrationEntity
            {
                Id = ManagementGovernanceTestConstants.SeededIntegrationId,
                DisplayName = "Governance test integration",
                ModuleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                ScopeKey = null,
                State = 0,
                SettingsJson = null,
                CreatedAt = now,
                UpdatedAt = now
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
        }

        await EnsureForeignModuleIntegrationForHarnessAsync(db).ConfigureAwait(false);
        await EnsureProcedureLauncherPrincipalAsync(db).ConfigureAwait(false);
        await EnsureWorkflowOnlyProcedureLauncherPrincipalAsync(db).ConfigureAwait(false);
        await EnsureIntegrationAuthoringPrincipalAsync(db).ConfigureAwait(false);
        await EnsureHelloWorldModuleEnabledForHarnessAsync(db).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="IProcedureRegistryService"/> only indexes procedures from modules passing <see cref="Apperture.ModuleSdk.Abstractions.IModuleActivationFilter"/>;
    /// without an enabled install row, governance procedure tests see an empty catalog.
    /// </summary>
    private static async Task EnsureHelloWorldModuleEnabledForHarnessAsync(AppertureDbContext db)
    {
        var envRaw = Environment.GetEnvironmentVariable("APERTURE_ENVIRONMENT_ID");
        var environmentId = string.IsNullOrWhiteSpace(envRaw) ? "local" : envRaw.Trim();

        if (await db.ModuleInstallationStates.AnyAsync(
                m => m.EnvironmentId == environmentId && m.ModuleId == ManagementGovernanceTestConstants.HelloWorldModuleId)
            .ConfigureAwait(false))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.ModuleInstallationStates.Add(
            new ModuleInstallationStateEntity
            {
                EnvironmentId = environmentId,
                ModuleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                Version = "1.0.0",
                Enabled = true,
                InstalledAt = now,
                UpdatedAt = now
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task EnsureForeignModuleIntegrationForHarnessAsync(AppertureDbContext db)
    {
        if (await db.Integrations.AnyAsync(i => i.Id == ManagementGovernanceTestConstants.SeededIntegrationForeignModuleId)
                .ConfigureAwait(false))
            return;

        var now = DateTimeOffset.UtcNow;
        db.Integrations.Add(
            new IntegrationEntity
            {
                Id = ManagementGovernanceTestConstants.SeededIntegrationForeignModuleId,
                DisplayName = "Governance test — foreign module integration",
                ModuleId = "Governance.Test.ForeignModule",
                ScopeKey = null,
                State = 0,
                SettingsJson = null,
                CreatedAt = now,
                UpdatedAt = now
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task EnsureProcedureLauncherPrincipalAsync(AppertureDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Id == ManagementGovernanceTestConstants.UserProcedureLauncher).ConfigureAwait(false))
        {
            var role = await db.Roles.SingleAsync(r => r.Id == ManagementGovernanceTestConstants.RoleProcedureLauncher)
                .ConfigureAwait(false);
            if (!string.Equals(role.PermissionsJson, ProcedureLauncherPermissionsJson, StringComparison.Ordinal))
            {
                role.PermissionsJson = ProcedureLauncherPermissionsJson;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return;
        }

        const int localSource = (int)IdentitySource.Local;

        db.Roles.Add(
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleProcedureLauncher,
                Name = "Governance test — procedure launcher",
                Description = "Epic 56A/56B harness",
                PermissionsJson = ProcedureLauncherPermissionsJson,
                IsSystemRole = false
            });

        db.Users.Add(
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserProcedureLauncher,
                DisplayName = "Governance test — procedure launcher",
                IsBreakGlassEligible = false
            });

        db.IdentityLinks.Add(
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkProcedureLauncher,
                UserId = ManagementGovernanceTestConstants.UserProcedureLauncher,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalProcedureLauncher
            });

        db.UserRoles.Add(
            new UserRoleEntity
            {
                UserId = ManagementGovernanceTestConstants.UserProcedureLauncher,
                RoleId = ManagementGovernanceTestConstants.RoleProcedureLauncher
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task EnsureWorkflowOnlyProcedureLauncherPrincipalAsync(AppertureDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Id == ManagementGovernanceTestConstants.UserWorkflowOnlyProcedureLauncher).ConfigureAwait(false))
            return;

        const int localSource = (int)IdentitySource.Local;

        db.Roles.Add(
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleWorkflowOnlyProcedureLauncher,
                Name = "Governance test — workflow-only procedure launcher",
                Description = "Epic 56B harness",
                PermissionsJson = WorkflowOnlyProcedureLauncherPermissionsJson,
                IsSystemRole = false
            });

        db.Users.Add(
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserWorkflowOnlyProcedureLauncher,
                DisplayName = "Governance test — workflow-only procedure launcher",
                IsBreakGlassEligible = false
            });

        db.IdentityLinks.Add(
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkWorkflowOnlyProcedureLauncher,
                UserId = ManagementGovernanceTestConstants.UserWorkflowOnlyProcedureLauncher,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalWorkflowOnlyProcedureLauncher
            });

        db.UserRoles.Add(
            new UserRoleEntity
            {
                UserId = ManagementGovernanceTestConstants.UserWorkflowOnlyProcedureLauncher,
                RoleId = ManagementGovernanceTestConstants.RoleWorkflowOnlyProcedureLauncher
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task EnsureIntegrationAuthoringPrincipalAsync(AppertureDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Id == ManagementGovernanceTestConstants.UserIntegrationAuthoring).ConfigureAwait(false))
        {
            var role = await db.Roles.SingleAsync(r => r.Id == ManagementGovernanceTestConstants.RoleIntegrationAuthoring)
                .ConfigureAwait(false);
            if (!string.Equals(role.PermissionsJson, IntegrationAuthoringPermissionsJson, StringComparison.Ordinal))
            {
                role.PermissionsJson = IntegrationAuthoringPermissionsJson;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return;
        }

        const int localSource = (int)IdentitySource.Local;

        db.Roles.Add(
            new RoleEntity
            {
                Id = ManagementGovernanceTestConstants.RoleIntegrationAuthoring,
                Name = "Governance test — integration authoring",
                Description = "Epic 57 harness",
                PermissionsJson = IntegrationAuthoringPermissionsJson,
                IsSystemRole = false
            });

        db.Users.Add(
            new UserEntity
            {
                Id = ManagementGovernanceTestConstants.UserIntegrationAuthoring,
                DisplayName = "Governance test — integration authoring",
                IsBreakGlassEligible = false
            });

        db.IdentityLinks.Add(
            new IdentityLinkEntity
            {
                Id = ManagementGovernanceTestConstants.LinkIntegrationAuthoring,
                UserId = ManagementGovernanceTestConstants.UserIntegrationAuthoring,
                Source = localSource,
                ExternalId = ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator
            });

        db.UserRoles.Add(
            new UserRoleEntity
            {
                UserId = ManagementGovernanceTestConstants.UserIntegrationAuthoring,
                RoleId = ManagementGovernanceTestConstants.RoleIntegrationAuthoring
            });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
