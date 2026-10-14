using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Apperture.ConnectorResolution;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.ConnectionSecretOwnershipSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.2 (A2.2, A2.4, A2.5): all six places that turn a connection row into a secret, driven for real against SQL Server on
/// locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs: worker slot resolution, the integration settings options, and
/// test-connect for the SQL, Sftp and Campus Nexus verifiers, and structure browse. A poisoned reference of every class is refused
/// on every path; the foreign secret is never unprotected, nothing is sent to the connection's destination (a loopback listener
/// counts what arrives), the store is unchanged, and one W3 row is written per top-level operation. A connection that owns its secret
/// still reaches its destination on every path. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionSecretResolutionPathsSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string RefusedAction = "connection.secret.resolution_refused";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ConnectionSecretResolutionPathsSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>The six entry points of the design gate (§3.3, a to f).</summary>
    public enum Entry
    {
        WorkerSlot,
        SettingsOptions,
        VerifySql,
        VerifySftp,
        VerifyCampusNexus,
        Browse,
    }

    public static IEnumerable<object[]> Matrix() =>
        from entry in Enum.GetValues<Entry>()
        from kind in Refused
        from rcsi in new[] { false, true }
        select new object[] { entry, kind, rcsi };

    public static IEnumerable<object[]> Entries() =>
        from entry in Enum.GetValues<Entry>()
        from rcsi in new[] { false, true }
        select new object[] { entry, rcsi };

    private static string FieldFor(Entry entry) => entry is Entry.SettingsOptions or Entry.VerifyCampusNexus ? "ApiKey" : "Password";

    private static string PathFor(Entry entry) => entry switch
    {
        Entry.WorkerSlot => "run",
        Entry.SettingsOptions => "settings_options",
        Entry.Browse => "browse",
        _ => "verify",
    };

    // ---- a destination that counts what reaches it ----

    private sealed class Destination : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _accepted;

        public Destination()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        Interlocked.Increment(ref _accepted);
                    }
                }
                catch (Exception)
                {
                    // Stopped.
                }
            });
        }

        public int Port { get; }

        public int Accepted => Volatile.Read(ref _accepted);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }

    private sealed class CountingFactory : IConnectorClientFactory<object>
    {
        public int Created;

        public object Create(Connection connection, ResolvedConnectionMaterial material)
        {
            Interlocked.Increment(ref Created);
            return new object();
        }
    }

    // ---- writers that fail, to prove evidence never gates the refusal ----

    private sealed class ThrowingWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("audit store is down"));

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(new InvalidOperationException("audit store is down"));
    }

    private sealed class FalseWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>The real writer, pointed at a database that does not exist: its insert genuinely fails.</summary>
    private static IAuditEventWriter BrokenSqlWriter(AuditSqlScratch scratch, IPrincipalContext principal)
    {
        var connectionString = new SqlConnectionStringBuilder(scratch.ConnectionString)
        {
            InitialCatalog = "Apperture_DoesNotExist_" + Guid.NewGuid().ToString("N")[..8],
            ConnectTimeout = 2,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppertureDbContext>().UseSqlServer(connectionString).Options;
        return new SqlAuditEventWriter(options, TimeProvider.System, null, principal);
    }

    // ---- driving an entry point ----

    private sealed record Outcome(
        Exception? Thrown,
        ConnectionVerificationResult? Verification,
        SqlStructureResult<SqlSchemaInfo>? Browse,
        int Reached);

    private sealed record Case(string ConnectionId, string Field, string ModuleId, string? IntegrationId);

    /// <summary>Stores the connection (config pointing at the listener, the field holding <paramref name="reference"/>) and anything the entry point needs around it.</summary>
    private static async Task<Case> ArrangeAsync(AuditSqlScratch scratch, Entry entry, string connectionId, string reference, int port)
    {
        var field = FieldFor(entry);
        var moduleId = "mod-" + Guid.NewGuid().ToString("N")[..10];
        var (connector, config) = entry switch
        {
            Entry.VerifySftp => (WellKnownConnectorDefinitionIds.PlatformSftp, new Dictionary<string, string>
            {
                ["Host"] = "127.0.0.1", ["Port"] = port.ToString(), ["Username"] = "operator",
            }),
            Entry.SettingsOptions or Entry.VerifyCampusNexus => (WellKnownConnectorDefinitionIds.PlatformCampusNexus, new Dictionary<string, string>
            {
                ["BaseUrl"] = $"http://127.0.0.1:{port}/", ["AuthMode"] = "ApiKey",
            }),
            _ => (WellKnownConnectorDefinitionIds.PlatformSql, new Dictionary<string, string>
            {
                ["Server"] = $"127.0.0.1,{port}", ["Database"] = "Target", ["UserId"] = "reader", ["TrustServerCertificate"] = "true",
            }),
        };

        await using (var db = scratch.NewDb())
        {
            await new SqlConnectionRepository(db).AddAsync(new Connection(
                new ConnectionId(connectionId), connector, "Poisoned candidate", config,
                new Dictionary<string, string> { [field] = reference }, ScopeKey: null, OwnerId: null));
        }

        string? integrationId = null;
        if (entry is Entry.WorkerSlot or Entry.SettingsOptions)
        {
            if (entry == Entry.SettingsOptions)
            {
                integrationId = await OwnershipSqlSeed.SeedIntegrationAsync(scratch, ResourceContextIds.SharedValue);
                moduleId = "Sample.Mod";
            }

            await using var db = scratch.NewDb();
            await new SqlBindingRepository(db).AddAsync(new Binding(
                new BindingId("bnd-" + Guid.NewGuid().ToString("N")[..12]),
                integrationId is null ? null : new IntegrationId(integrationId),
                moduleId,
                entry == Entry.SettingsOptions ? CampusNexusCampusesOptionProvider.CampusNexusApiSlotName : "Db",
                "contract",
                new ConnectionId(connectionId),
                null,
                BindingState.Active));
        }

        return new Case(connectionId, field, moduleId, integrationId);
    }

    /// <summary>Runs the entry point once, over a boundary built on the real store and the given audit writer.</summary>
    private static async Task<Outcome> DriveAsync(
        AuditSqlScratch scratch, Entry entry, Case arranged, ConnectionSecretOwnershipSupport.Resolution rig,
        IAuditEventWriter writer, IPrincipalContext principal, Destination destination, CountingFactory slotFactory)
    {
        await using var db = scratch.NewDb();
        var connection = (await new SqlConnectionRepository(db).GetByIdAsync(new ConnectionId(arranged.ConnectionId)))!;
        var boundary = new ConnectionMaterialResolver(rig.Composite, writer);

        try
        {
            switch (entry)
            {
                case Entry.WorkerSlot:
                {
                    var registry = new InMemoryConnectorDefinitionRegistry();
                    registry.Register(WellKnownConnectorDefinitions.CreatePlatformSql());
                    var factories = new ConnectorClientFactoryRegistry();
                    factories.Register<object>(WellKnownConnectorDefinitionIds.PlatformSql, slotFactory);
                    var resolver = new SlotBoundResolver(
                        new SqlBindingRepository(db), new SqlConnectionRepository(db),
                        new DefaultConnectionResolver(boundary, registry, factories, new ConnectionSlotResolution(ConnectionResolutionPath.Run)));
                    await resolver.ResolveSlotAsync<object>(arranged.ModuleId, "Db");
                    return new Outcome(null, null, null, slotFactory.Created);
                }

                case Entry.SettingsOptions:
                {
                    var registry = new InMemoryConnectorDefinitionRegistry();
                    registry.Register(WellKnownConnectorDefinitions.CreatePlatformCampusNexusMinimal());
                    var factories = new ConnectorClientFactoryRegistry();
                    factories.Register<ICampusNexusApiClient>(WellKnownConnectorDefinitionIds.PlatformCampusNexus, new CampusNexusApiConnectorClientFactory());
                    var slots = new SlotBoundResolver(
                        new SqlBindingRepository(db), new SqlConnectionRepository(db),
                        new DefaultConnectionResolver(boundary, registry, factories, new ConnectionSlotResolution(ConnectionResolutionPath.SettingsOptions)));
                    var provider = new CampusNexusCampusesOptionProvider(new SqlIntegrationRepository(db), slots);
                    await provider.GetOptionsAsync(
                        null!,
                        new PlatformSettingOptionProviderContext
                        {
                            ContextKind = PlatformSettingOptionProviderContextKind.Integration,
                            SettingKey = "campus",
                            IntegrationId = arranged.IntegrationId,
                            Actor = (await principal.GetCurrentActorAsync())!,
                        });
                    return new Outcome(null, null, null, destination.Accepted);
                }

                case Entry.VerifySql:
                    return new Outcome(null, await new PlatformSqlConnectionVerifier(boundary, new PlatformSqlConnectorClientFactory()).VerifyAsync(connection), null, destination.Accepted);

                case Entry.VerifySftp:
                    return new Outcome(null, await new SftpConnectionVerifier(boundary, new SftpConnectorClientFactory()).VerifyAsync(connection), null, destination.Accepted);

                case Entry.VerifyCampusNexus:
                    return new Outcome(null, await new CampusNexusConnectionVerifier(boundary, new CampusNexusApiConnectorClientFactory()).VerifyAsync(connection), null, destination.Accepted);

                case Entry.Browse:
                {
                    var browse = await new PlatformSqlStructureBrowser(boundary, new PlatformSqlConnectorClientFactory(), new SqlStructureBrowserOptions { ConnectTimeoutSeconds = 5 })
                        .ListSchemasAsync(connection, CancellationToken.None);
                    return new Outcome(null, null, browse, destination.Accepted);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(entry), entry, null);
            }
        }
        catch (Exception ex)
        {
            return new Outcome(ex, null, null, entry == Entry.WorkerSlot ? slotFactory.Created : destination.Accepted);
        }
    }

    /// <summary>Whether the entry point reported a refusal in the way it reports one.</summary>
    private static void AssertRefused(Entry entry, Outcome outcome)
    {
        switch (entry)
        {
            case Entry.WorkerSlot:
            case Entry.SettingsOptions:
                outcome.Thrown.Should().BeOfType<ConnectionSecretUnavailableException>($"{entry} surfaces the typed refusal");
                outcome.Thrown!.Message.Should().Be(ConnectionSecretUnavailableException.FixedMessage);
                break;
            case Entry.Browse:
                outcome.Thrown.Should().BeNull();
                outcome.Browse!.Outcome.Should().Be(ConnectionToolOutcome.SecretsUnavailable);
                outcome.Browse.Message.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
                break;
            default:
                outcome.Thrown.Should().BeNull();
                outcome.Verification!.Status.Should().Be(ConnectionVerificationStatus.Failed);
                outcome.Verification.Summary.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
                break;
        }
    }

    private static async Task<List<Dictionary<string, object?>>> RefusalRowsAsync(AuditSqlScratch scratch, string connectionId) =>
        await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetId = N'{connectionId}' AND Action = N'{RefusedAction}' ORDER BY Id;");

    // ---- A2.2 / A2.3 / A2.4: every path, every class ----

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_poisoned_reference_is_refused_on_every_path_and_nothing_foreign_is_read_sent_or_changed(Entry entry, Kind kind, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, kind, FieldFor(entry));
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, entry, connectionId, reference, destination.Port);
        var before = await SnapshotAsync(scratch);
        await using var rig = NewResolution(scratch);
        var writer = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principal);

        var outcome = await DriveAsync(scratch, entry, arranged, rig, writer, principal, destination, new CountingFactory());

        AssertRefused(entry, outcome);
        outcome.Reached.Should().Be(0, "nothing is sent to a connection whose secret was refused");
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty("neither a foreign secret nor any other is decrypted for a refused connection");
        (await SnapshotAsync(scratch)).Should().Be(before, "the secret store and the connection are untouched by a refusal");
        if (ForeignWithMaterial.Contains(kind))
            rig.Log.Commands.Should().NotContain(c => c.Contains("SecretVersions"), "the foreign secret's version row is not read");

        var row = (await RefusalRowsAsync(scratch, connectionId)).Should().ContainSingle("one top-level operation writes one refusal row").Subject;
        row["Category"].Should().Be(AuditCategories.Secrets);
        row["Severity"].Should().Be(AuditSeverities.Elevated);
        row["Basis"].Should().Be(AuditBases.Attempt);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        row["TargetType"].Should().Be(AuditTargetTypes.Connection);
        row["Actor"].Should().Be(PathFor(entry) == "run" ? AuditActor.Worker.RecordedValue : admin);
        Detail(row, "fieldKey").Should().Be(arranged.Field);
        Detail(row, "path").Should().Be(PathFor(entry));

        // A2.3: nothing identifying in the evidence, whichever class it was.
        var details = (string)row["Details"]!;
        if (reference.Length > 0)
            details.Should().NotContain(reference);
        details.Should().NotContain("connection:").And.NotContain("platform-").And.NotContain(ForeignMaterial).And.NotContain(OwnedMaterial);
        JsonSerializer.Serialize(outcome.Verification).Should().NotContain(reference.Length > 0 ? reference : "\u0001");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task A_connection_that_owns_its_secret_still_reaches_its_destination_on_every_path_with_no_refusal_row(Entry entry, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned, FieldFor(entry));
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, entry, connectionId, reference, destination.Port);
        await using var rig = NewResolution(scratch);
        var writer = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principal);

        var outcome = await DriveAsync(scratch, entry, arranged, rig, writer, principal, destination, new CountingFactory());

        (outcome.Thrown is ConnectionSecretUnavailableException).Should().BeFalse("an owned secret is not refused");
        outcome.Verification?.Summary.Should().NotBe(ConnectionVerificationMessages.SecretResolutionFailed);
        outcome.Browse?.Outcome.Should().NotBe(ConnectionToolOutcome.SecretsUnavailable);
        outcome.Reached.Should().BeGreaterThan(0, "the owned secret resolves and the client goes to the destination, which is what the listener counts");
        rig.Protector.UnprotectedWithKeyId.Should().ContainSingle("exactly the connection's own secret was decrypted");
        (await RefusalRowsAsync(scratch, connectionId)).Should().BeEmpty("a resolution that succeeds is not audited");
    }

    // ---- A2.4: one row per top-level operation ----

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task A_connection_with_several_poisoned_fields_writes_one_row_naming_the_first_field(Entry entry, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var first = await SeedReferenceAsync(scratch, connectionId, Kind.OtherConnection, FieldFor(entry));
        var second = await SeedReferenceAsync(scratch, connectionId, Kind.OtherProvider, "Zeta");
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, entry, connectionId, first, destination.Port);
        await scratch.ExecuteAsync(
            $"UPDATE Connections SET SecretRefsJson = N'{{\"{arranged.Field}\":\"{first}\",\"Zeta\":\"{second}\"}}' WHERE Id = N'{connectionId}';");
        await using var rig = NewResolution(scratch);
        var writer = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principal);

        var outcome = await DriveAsync(scratch, entry, arranged, rig, writer, principal, destination, new CountingFactory());

        AssertRefused(entry, outcome);
        var row = (await RefusalRowsAsync(scratch, connectionId)).Should().ContainSingle("the slot resolver, the default resolver and a verifier wrap the boundary and write nothing").Subject;
        Detail(row, "fieldKey").Should().Be(arranged.Field.CompareTo("Zeta") < 0 ? arranged.Field : "Zeta", "the first refused field in ordinal order");
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty();
        outcome.Reached.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task Each_new_top_level_attempt_writes_its_own_row(Entry entry, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.OtherConnection, FieldFor(entry));
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, entry, connectionId, reference, destination.Port);
        await using var rig = NewResolution(scratch);
        var writer = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principal);

        // A step the worker retries is a new top-level operation each time (design gate §10.4, Q-1): no throttling is approved.
        for (var attempt = 0; attempt < 3; attempt++)
            AssertRefused(entry, await DriveAsync(scratch, entry, arranged, rig, writer, principal, destination, new CountingFactory()));

        (await RefusalRowsAsync(scratch, connectionId)).Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_worker_records_its_refusal_with_no_principal_context_at_all(bool rcsi)
    {
        // The Worker Host has no signed-in operator: its writer is built without a principal context, as its composition builds it.
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.OtherConnection, FieldFor(Entry.WorkerSlot));
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, Entry.WorkerSlot, connectionId, reference, destination.Port);
        await using var rig = NewResolution(scratch);
        var workerWriter = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principals: null);

        var outcome = await DriveAsync(scratch, Entry.WorkerSlot, arranged, rig, workerWriter, principal, destination, new CountingFactory());

        AssertRefused(Entry.WorkerSlot, outcome);
        var row = (await RefusalRowsAsync(scratch, connectionId)).Should().ContainSingle().Subject;
        row["Actor"].Should().Be(AuditActor.Worker.RecordedValue);
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    // ---- A2.4: evidence never gates the refusal ----

    public static IEnumerable<object[]> FailingWriters() =>
        from entry in Enum.GetValues<Entry>()
        from writer in new[] { "throws", "reports failure", "database unreachable" }
        select new object[] { entry, writer };

    [Theory]
    [MemberData(nameof(FailingWriters))]
    public async Task When_the_refusal_row_cannot_be_written_no_secret_is_delivered_and_the_result_is_still_a_refusal(Entry entry, string writerKind)
    {
        var scratch = _catalogs.Locking;
        var (_, principal) = await AdminAsync(scratch);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.OtherConnection, FieldFor(entry));
        using var destination = new Destination();
        var arranged = await ArrangeAsync(scratch, entry, connectionId, reference, destination.Port);
        await using var rig = NewResolution(scratch);
        IAuditEventWriter writer = writerKind switch
        {
            "throws" => new ThrowingWriter(),
            "reports failure" => new FalseWriter(),
            _ => BrokenSqlWriter(scratch, principal),
        };

        var outcome = await DriveAsync(scratch, entry, arranged, rig, writer, principal, destination, new CountingFactory());

        AssertRefused(entry, outcome);
        outcome.Reached.Should().Be(0, "an audit failure is not a reason to release the secret");
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty();
        (await RefusalRowsAsync(scratch, connectionId)).Should().BeEmpty("the write really failed, so there is no row");
    }
}
