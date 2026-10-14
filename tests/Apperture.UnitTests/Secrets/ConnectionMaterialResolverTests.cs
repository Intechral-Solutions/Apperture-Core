using System.Security.Cryptography;
using System.Text.Json;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.2 (A2.1, A2.3, A2.4, A2.5): the connection-material boundary. Every secret field is resolved through
/// <c>ResolveOwnedAsync</c> against the owner the connection's own field defines; every refusal is the same typed exception with a
/// fixed message; one top-level operation writes one W3 row whatever it passed through; and the refusal never waits on, or is
/// changed by, the evidence.
/// </summary>
public sealed class ConnectionMaterialResolverTests
{
    private const string Foreign = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11";
    private const string Other = "9c1d4e7b-0a3f-4c52-8e6d-5b7a1f2c3d44";

    private static Connection ConnectionWith(params (string Key, string Value)[] secretRefs) =>
        new(
            new ConnectionId("conn-a"), WellKnownConnectorDefinitionIds.PlatformSql, "Warehouse",
            new Dictionary<string, string> { ["Server"] = "db.example" },
            secretRefs.ToDictionary(r => r.Key, r => r.Value),
            null, null);

    private static ResolvedSecret Resolved(string reference, string value) =>
        new(new SecretRef(reference), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: value));

    private sealed class Rig
    {
        /// <summary>Strict: the unchecked <c>ResolveAsync</c> has no setup, so calling it fails the test.</summary>
        public Mock<ISecretResolver> Secrets { get; } = new(MockBehavior.Strict);

        public Mock<IAuditEventWriter> Audit { get; } = new();
        public List<AuditEvent> Rows { get; } = [];
        public List<AuditActor> Actors { get; } = [];
        public List<string> Logs { get; } = [];

        public ConnectionMaterialResolver Sut { get; private set; } = null!;

        public static Rig New(bool auditWrites = true, Exception? auditThrows = null)
        {
            var rig = new Rig();
            rig.Audit.Setup(a => a.TryWriteAsync(It.IsAny<AuditEvent>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()))
                .Returns((AuditEvent e, AuditActor actor, CancellationToken _) =>
                {
                    rig.Rows.Add(e);
                    rig.Actors.Add(actor);
                    return auditThrows is not null ? Task.FromException<bool>(auditThrows) : Task.FromResult(auditWrites);
                });
            rig.Sut = new ConnectionMaterialResolver(rig.Secrets.Object, rig.Audit.Object, new CapturingLogger(rig.Logs));
            return rig;
        }

        public void Owned(string connectionId, string key, string reference, string value) =>
            Secrets.Setup(s => s.ResolveOwnedAsync(
                    It.Is<SecretRef>(r => r.Value == reference),
                    It.Is<SecretOwner>(o => o.Provider == "platform-connection" && o.Name == $"connection:{connectionId}:{key}"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Resolved(reference, value));

        public void Refuses(string reference) =>
            Secrets.Setup(s => s.ResolveOwnedAsync(It.Is<SecretRef>(r => r.Value == reference), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretUnavailableException());
    }

    private sealed class CapturingLogger(List<string> sink) : ILogger<ConnectionMaterialResolver>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
    }

    // ---- the owned path ----

    [Fact]
    public async Task An_owned_field_resolves_through_the_owned_call_with_the_owner_the_field_defines_and_merges_with_config()
    {
        var rig = Rig.New();
        rig.Owned("conn-a", "Password", Foreign, "s3cret");

        var material = await rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run);

        material.ConfigAndResolvedSecrets.Should().Equal(new Dictionary<string, string> { ["Server"] = "db.example", ["Password"] = "s3cret" });
        rig.Rows.Should().BeEmpty("nothing was refused, and a successful resolution is not audited");
        rig.Secrets.VerifyAll();
    }

    [Fact]
    public async Task A_connection_with_no_secret_fields_resolves_to_its_config_without_asking_the_secret_resolver()
    {
        var rig = Rig.New();

        var material = await rig.Sut.ResolveAsync(ConnectionWith(), ConnectionResolutionPath.Verify);

        material.ConfigAndResolvedSecrets.Should().Equal(new Dictionary<string, string> { ["Server"] = "db.example" });
        rig.Secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_material_type_selection_is_unchanged_json_is_used_when_there_is_no_opaque_string()
    {
        var rig = Rig.New();
        rig.Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(Foreign), new SecretMaterial(SecretMaterialType.Json, Json: "{\"k\":1}")));

        var material = await rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run);

        material.ConfigAndResolvedSecrets["Password"].Should().Be("{\"k\":1}");
    }

    // ---- refusals ----

    [Fact]
    public async Task Any_refusal_is_the_same_typed_exception_with_a_fixed_message_that_names_no_reference()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run).AsTask();

        var thrown = (await act.Should().ThrowAsync<ConnectionSecretUnavailableException>()).Which;
        thrown.Message.Should().Be(ConnectionSecretUnavailableException.FixedMessage);
        thrown.FieldKey.Should().Be("Password");
        thrown.ToString().Should().NotContain(Foreign);
        thrown.InnerException.Should().BeNull("the cause would say which condition failed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reference_is_refused_without_being_offered_to_the_secret_resolver(string reference)
    {
        var rig = Rig.New();

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", reference)), ConnectionResolutionPath.Verify).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        rig.Secrets.VerifyNoOtherCalls();
        rig.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task An_empty_field_key_is_refused_and_recorded_with_a_bounded_placeholder()
    {
        var rig = Rig.New();

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("", Foreign)), ConnectionResolutionPath.Browse).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        rig.Secrets.VerifyNoOtherCalls();
        rig.Rows.Should().ContainSingle().Which.Fields["fieldKey"].Should().Be("(blank)");
    }

    [Fact]
    public async Task A_very_long_legacy_key_is_recorded_truncated_to_the_schema_bound()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);
        var key = new string('k', AuditDetailSchema.MaxIdLength + 40);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith((key, Foreign)), ConnectionResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        rig.Rows.Should().ContainSingle().Which.Fields["fieldKey"].Should().Be(key[..AuditDetailSchema.MaxIdLength]);
    }

    [Fact]
    public async Task A_connection_with_several_poisoned_fields_aborts_at_the_first_in_ordinal_order_and_writes_one_row()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);
        rig.Refuses(Other);

        // "ApiKey" sorts before "Password"; the second field is never offered to the secret resolver.
        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Other), ("ApiKey", Foreign)), ConnectionResolutionPath.Run).AsTask();

        (await act.Should().ThrowAsync<ConnectionSecretUnavailableException>()).Which.FieldKey.Should().Be("ApiKey");
        rig.Secrets.Verify(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()), Times.Once);
        rig.Audit.Verify(a => a.TryWriteAsync(It.IsAny<AuditEvent>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_good_field_before_a_poisoned_one_is_not_returned_when_the_call_fails()
    {
        var rig = Rig.New();
        rig.Owned("conn-a", "ApiKey", Foreign, "good-secret");
        rig.Refuses(Other);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("ApiKey", Foreign), ("Password", Other)), ConnectionResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
    }

    // ---- the evidence ----

    [Theory]
    [InlineData(ConnectionResolutionPath.Run, "run", true)]
    [InlineData(ConnectionResolutionPath.Verify, "verify", false)]
    [InlineData(ConnectionResolutionPath.Browse, "browse", false)]
    [InlineData(ConnectionResolutionPath.SettingsOptions, "settings_options", false)]
    public async Task The_row_names_the_field_and_the_path_and_is_attributed_to_the_worker_on_a_run_and_the_operator_otherwise(
        ConnectionResolutionPath path, string expectedPath, bool worker)
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), path).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        var row = rig.Rows.Should().ContainSingle().Subject;
        row.Action.Should().Be("connection.secret.resolution_refused");
        row.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        row.Target!.Type.Should().Be(AuditTargetTypes.Connection);
        row.Target.Id.Should().Be("conn-a");
        row.Fields.Should().Equal(new Dictionary<string, object?> { ["fieldKey"] = "Password", ["path"] = expectedPath });
        rig.Actors.Should().ContainSingle().Which.Should().Be(worker ? AuditActor.Worker : AuditActor.CurrentPrincipal);
        JsonSerializer.Serialize(row.Fields).Should().NotContain(Foreign, "a refusal never records the reference");
    }

    [Fact]
    public async Task A_row_that_reports_failure_leaves_the_result_a_refusal()
    {
        var rig = Rig.New(auditWrites: false);
        rig.Refuses(Foreign);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        rig.Logs.Should().Contain(l => l.Contains("was not recorded"));
    }

    [Fact]
    public async Task A_writer_that_throws_leaves_the_result_a_refusal_and_loses_nothing_but_the_row()
    {
        var rig = Rig.New(auditThrows: new InvalidOperationException($"audit database unreachable near {Foreign}"));
        rig.Refuses(Foreign);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Verify).AsTask();

        var thrown = (await act.Should().ThrowAsync<ConnectionSecretUnavailableException>()).Which;
        thrown.Message.Should().Be(ConnectionSecretUnavailableException.FixedMessage);
        rig.Logs.Should().Contain(l => l.Contains("could not be recorded"));
        rig.Logs.Should().NotContain(l => l.Contains(Foreign), "the writer's message is not trusted to be free of identifiers, so it is not logged");
    }

    [Fact]
    public async Task Nothing_logged_by_a_refusal_contains_the_reference()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run).AsTask();
        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();

        rig.Logs.Should().NotBeEmpty("a refusal is worth an operator's attention");
        rig.Logs.Should().NotContain(l => l.Contains(Foreign));
    }

    // ---- what is not a refusal ----

    [Fact]
    public async Task A_failure_that_is_not_a_refusal_propagates_unchanged_and_writes_no_row()
    {
        var rig = Rig.New();
        var failure = new CryptographicException("protector failed");
        rig.Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run).AsTask();

        (await act.Should().ThrowAsync<CryptographicException>()).Which.Should().BeSameAs(failure);
        rig.Rows.Should().BeEmpty("an infrastructure failure is not an ownership refusal");
    }

    [Fact]
    public async Task The_callers_cancellation_propagates_and_writes_no_row()
    {
        var rig = Rig.New();
        using var cts = new CancellationTokenSource();
        rig.Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
            .Returns((SecretRef _, SecretOwner _, CancellationToken ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult<ResolvedSecret>(null!);
            });

        var act = () => rig.Sut.ResolveAsync(ConnectionWith(("Password", Foreign)), ConnectionResolutionPath.Run, cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.Rows.Should().BeEmpty();
    }

    [Fact]
    public void A_run_that_fails_on_a_refusal_records_only_the_fixed_text_as_its_failure_summary()
    {
        // The worker stores an exception's message as the run's failure summary, which operators read.
        var capture = Apperture.Persistence.SqlServer.RunFailureCapture.TryCapture(new ConnectionSecretUnavailableException("Password"));

        capture.FailureSummary.Should().Be(ConnectionSecretUnavailableException.FixedMessage);
        capture.FailureSummary.Should().NotContain("Password").And.NotContain(Foreign);
    }

    [Fact]
    public void The_typed_refusal_is_an_invalid_operation_so_existing_callers_that_mapped_a_secret_miss_keep_mapping_it()
    {
        new ConnectionSecretUnavailableException("k").Should().BeAssignableTo<InvalidOperationException>();
        new SecretUnavailableException().Should().BeAssignableTo<InvalidOperationException>();
        new SecretUnavailableException().Message.Should().Be(SecretUnavailableException.FixedMessage);
    }

    // ---- the slot resolver wraps the boundary and writes nothing of its own ----

    [Fact]
    public async Task The_default_resolver_reports_the_hosts_slot_path_and_adds_no_row_of_its_own()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);
        var registry = new InMemoryConnectorDefinitionRegistry();
        registry.Register(WellKnownConnectorDefinitions.CreatePlatformSql());
        var factories = new ConnectorClientFactoryRegistry();
        factories.Register(WellKnownConnectorDefinitionIds.PlatformSql, new ThrowingFactory());

        foreach (var (hostPath, expected) in new[] { (ConnectionResolutionPath.Run, "run"), (ConnectionResolutionPath.SettingsOptions, "settings_options") })
        {
            rig.Rows.Clear();
            var resolver = new DefaultConnectionResolver(rig.Sut, registry, factories, new ConnectionSlotResolution(hostPath));

            var act = () => resolver.ResolveAsync<object>(ConnectionWith(("Password", Foreign))).AsTask();

            await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
            rig.Rows.Should().ContainSingle("the default resolver wraps the boundary and records nothing itself")
                .Which.Fields["path"].Should().Be(expected);
        }
    }

    [Fact]
    public async Task The_default_resolver_without_a_host_registration_reports_a_run()
    {
        var rig = Rig.New();
        rig.Refuses(Foreign);
        var registry = new InMemoryConnectorDefinitionRegistry();
        registry.Register(WellKnownConnectorDefinitions.CreatePlatformSql());
        var factories = new ConnectorClientFactoryRegistry();
        factories.Register(WellKnownConnectorDefinitionIds.PlatformSql, new ThrowingFactory());

        var act = () => new DefaultConnectionResolver(rig.Sut, registry, factories).ResolveAsync<object>(ConnectionWith(("Password", Foreign))).AsTask();

        await act.Should().ThrowAsync<ConnectionSecretUnavailableException>();
        rig.Rows.Should().ContainSingle().Which.Fields["path"].Should().Be("run");
    }

    private sealed class ThrowingFactory : Apperture.Core.Application.Services.IConnectorClientFactory<object>
    {
        public object Create(Connection connection, ResolvedConnectionMaterial material) =>
            throw new InvalidOperationException("a poisoned connection must never reach a client factory");
    }
}
