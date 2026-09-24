using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real-SQL proof for Connection lifecycle audit. Unlike the in-memory unit tests, this drives the real
/// <see cref="ConnectionManagementService"/> over the real <see cref="SqlAuditService"/> against a throwaway migrated
/// SQL Server catalog, so it proves the JSON Details survive persistence (no Resource string-append), the
/// ResourceContext column is populated from the Connection, and the create-time audit record is atomic with the row.
/// The fixture fails hard when SQL Server is unreachable: a soft-skip is not evidence.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionLifecycleAuditSqlServerTests
{
    private const string HostValue = "sql-audit-proof.internal.example";
    private const string SecretValue = "audit-proof-secret-value";
    private static readonly ConnectorDefinitionId Sql = new("Apperture.Platform.Sql");
    private static readonly string SecretRefGuid = Guid.NewGuid().ToString("D");

    [Fact]
    public async Task Every_lifecycle_operation_persists_one_valid_json_record_with_the_connections_context()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_ConnAudit1_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);
        var context = await env.ResourceContexts.CreateApplicationContextAsync("ConnAudit-App");

        var created = await env.Connections.CreateConnectionAsync(NewRequest(context.Id));
        await env.Connections.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("Renamed", null, null, null));
        await env.Connections.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));
        await env.Connections.VerifyConnectionAsync(created.Id);
        await env.Connections.RetireConnectionAsync(created.Id);
        await env.Connections.GetConnectionAsync(created.Id);

        var page = await env.Audit.QueryAsync(new AuditQuery(FilterByResourceContextId: context.Id.Value, PageSize: 50));

        page.Items.Select(i => i.Action).Should().BeEquivalentTo(
        [
            ConnectionAuditActionNames.Created, ConnectionAuditActionNames.Updated, ConnectionAuditActionNames.SecretRotated,
            ConnectionAuditActionNames.Verified, ConnectionAuditActionNames.Retired
        ], "exactly one record per operation, and reading the connection wrote none");
        foreach (var item in page.Items)
        {
            item.ResourceContextId.Should().Be(context.Id.Value);
            using var details = JsonDocument.Parse(item.Details!);
            details.RootElement.GetProperty("resourceType").GetString().Should().Be("Connection");
            details.RootElement.GetProperty("resourceId").GetString().Should().Be(created.Id.Value);
            item.Details.Should().NotContain(SecretValue).And.NotContain(SecretRefGuid).And.NotContain(HostValue)
                .And.NotContain("Resource=", "a non-null Resource would have been string-appended to Details");
        }
    }

    [Fact]
    public async Task A_failing_audit_append_rolls_the_created_connection_back()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_ConnAudit2_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db, audit: new ThrowingAuditService());
        var context = await env.ResourceContexts.CreateApplicationContextAsync("ConnAudit-Atomic");

        var act = async () => await env.Connections.CreateConnectionAsync(NewRequest(context.Id));

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        (await db.Connections.CountAsync()).Should().Be(0, "the creation record and the connection commit or roll back together");
    }

    private static CreateConnectionRequest NewRequest(ResourceContextId contextId) =>
        new(
            Sql,
            "Audit proof",
            new Dictionary<string, string> { ["Host"] = HostValue },
            new Dictionary<string, string> { ["Password"] = SecretRefGuid },
            ResourceContextId: contextId);

    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string catalogPrefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(catalogPrefix);
        fixture.Should().NotBeNull(
            "Connection audit closure requires this suite to genuinely execute against SQL Server -- a soft-skipped green suite is not evidence. " +
            SqlServerTestConfiguration.DescribeActiveConfiguration());
        return fixture!;
    }

    private sealed class Env
    {
        public required IResourceContextService ResourceContexts { get; init; }
        public required ConnectionManagementService Connections { get; init; }
        public required IAuditService Audit { get; init; }

        public static Env Build(AppertureDbContext db, IAuditService? audit = null)
        {
            var resourceContexts = new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System);
            var realAudit = new SqlAuditService(db);
            var definition = new ConnectorDefinition(Sql, "Platform SQL", null, new Dictionary<string, string> { ["Host"] = "string" })
            {
                ConnectionFields =
                [
                    new ConnectionFieldDescriptor("Host", "Server", false),
                    new ConnectionFieldDescriptor("Password", "Password", true)
                ],
                Capabilities = ConnectorCapabilities.SupportsConnectionVerification
            };

            return new Env
            {
                ResourceContexts = resourceContexts,
                Audit = realAudit,
                Connections = new ConnectionManagementService(
                    new FixedPrincipalContext(),
                    new AlwaysAllowAuthorizationEvaluationService(),
                    new SqlConnectionRepository(db),
                    new SingleDefinitionRegistry(definition),
                    new SuccessfulVerificationService(),
                    new ConnectionSecretMaterialProvisioner(new RotatingSecretStore()),
                    resourceContexts,
                    db,
                    new SqlResourceOwnershipMutationGate(db),
                    audit ?? realAudit)
            };
        }
    }

    private sealed class SingleDefinitionRegistry(ConnectorDefinition definition) : IConnectorDefinitionRegistry
    {
        public void Register(ConnectorDefinition definition1) => throw new NotSupportedException();
        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => [definition];
        public ConnectorDefinition? Get(ConnectorDefinitionId id) => id == definition.Id ? definition : null;
    }

    private sealed class SuccessfulVerificationService : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ConnectionVerificationResult(
                ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, "remote banner sql-audit-proof.internal.example"));
    }

    /// <summary>Accepts version writes for an existing secret (rotation); creating a new secret is never needed here.</summary>
    private sealed class RotatingSecretStore : ISecretStore
    {
        public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default) => ValueTask.FromResult("v2");
        public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit sink failure.");
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("connection-audit-test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Connection Audit Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
