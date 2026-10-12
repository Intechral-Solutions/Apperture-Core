using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The connection services over a real catalog: the production service wired over the real SQL secret store, repository and
/// ownership gate, with an authorization that allows everything (authorization has its own suites) and seeding that writes no
/// audit row (the audit table is append-only). Shared by the connection SQL suites of AUD-1.4 slice 5.
/// </summary>
internal static class ConnectionSqlHarness
{
    internal const string HostSentinel = "sentinel-host.internal.example";
    internal const string OldSecret = "sentinel-old-connection-secret";
    internal const string NewSecret = "sentinel-new-connection-secret";
    internal static readonly ConnectorDefinitionId Sql = new("Apperture.Platform.Sql");

    internal static ConnectionManagementService Service(
        AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit, IConnectionVerificationService? verification = null) =>
        new(
            principal,
            new AllowAll(),
            new SqlConnectionRepository(db),
            new SingleDefinitionRegistry(),
            verification ?? new NoVerification(),
            new ConnectionSecretMaterialProvisioner(SecretStore(db)),
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            db,
            new SqlResourceOwnershipMutationGate(db),
            unit);

    internal static SqlSecretStore SecretStore(AppertureDbContext db) =>
        new(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());

    internal sealed record Seeded(string ConnectionId, string? SecretRef);

    /// <summary>
    /// Seeds a connection (and, with material, an active secret the connection names) straight through the repository and the
    /// store, so the case starts with no audit row: the audit table is append-only.
    /// </summary>
    internal static async Task<Seeded> SeedConnectionAsync(AuditSqlScratch scratch, IPrincipalContext principal, bool withMaterial)
    {
        _ = principal;
        await using var db = scratch.NewDb();
        var id = new ConnectionId(Guid.NewGuid().ToString("N")[..24]);
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (withMaterial)
        {
            var store = SecretStore(db);
            var descriptor = await store.CreateAsync(new SecretCreateRequest($"connection:{id.Value}:Password", "platform-connection", SecretMaterialType.OpaqueString));
            var version = await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: OldSecret));
            await store.ActivateVersionAsync(descriptor.SecretId, version);
            refs["Password"] = descriptor.SecretId.Value.ToString("D");
        }

        await new SqlConnectionRepository(db).AddAsync(new Connection(
            id,
            Sql,
            "Seeded connection",
            new Dictionary<string, string> { ["Host"] = HostSentinel },
            refs,
            ScopeKey: null,
            OwnerId: null));
        return new Seeded(id.Value, refs.GetValueOrDefault("Password"));
    }

    /// <summary>
    /// Seeds one more active secret in the real store under an explicit provider and name (no audit row), for the cases that need a
    /// secret that is, or only looks like, a connection's own.
    /// </summary>
    internal static async Task<string> SeedSecretAsync(AuditSqlScratch scratch, string provider, string name, string material)
    {
        await using var db = scratch.NewDb();
        var store = SecretStore(db);
        var descriptor = await store.CreateAsync(new SecretCreateRequest(name, provider, SecretMaterialType.OpaqueString));
        var version = await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: material));
        await store.ActivateVersionAsync(descriptor.SecretId, version);
        return descriptor.SecretId.Value.ToString("D");
    }

    /// <summary>Every row an operation can change, the secret store's rows included, as one comparable text.</summary>
    internal static async Task<string> SnapshotAsync(AuditSqlScratch scratch)
    {
        static string Text(IEnumerable<Dictionary<string, object?>> rows) =>
            string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));

        return Text(await scratch.RowsAsync("SELECT * FROM Connections ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT * FROM SecretDescriptors ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT SecretId, Version FROM SecretVersions ORDER BY SecretId, Version;"))
            + "#" + await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;");
    }

    internal static async Task<string?> ResolveAsync(AuditSqlScratch scratch, string secretRef)
    {
        await using var db = scratch.NewDb();
        try
        {
            var resolved = await new SqlSecretResolver(SecretStore(db), new PassThroughProtector()).ResolveAsync(new SecretRef(secretRef));
            return resolved.Material.OpaqueString;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static async Task<(string Admin, IPrincipalContext Principal)> AdminAsync(AuditSqlScratch scratch)
    {
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    internal static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string? connectionId = null) =>
        scratch.RowsAsync(connectionId is null
            ? "SELECT * FROM AuditLogs ORDER BY Id;"
            : $"SELECT * FROM AuditLogs WHERE TargetId = N'{connectionId}' ORDER BY Id;");

    internal static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        if (!details.RootElement.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    internal sealed class AllowAll : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    internal sealed class SingleDefinitionRegistry : IConnectorDefinitionRegistry
    {
        internal static readonly ConnectorDefinition Definition = new(Sql, "Platform SQL", null, new Dictionary<string, string> { ["Host"] = "string" })
        {
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("Host", "Server", false),
                new ConnectionFieldDescriptor("Database", "Database", false),
                new ConnectionFieldDescriptor("Password", "Password", true),
            ],
        };

        public void Register(ConnectorDefinition definition) => throw new NotSupportedException();

        public ConnectorDefinition? Get(ConnectorDefinitionId id) => id == Sql ? Definition : null;

        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => [Definition];
    }

    internal sealed class NoVerification : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }
}
