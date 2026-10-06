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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Connection lifecycle audit closure. Create, update, secret rotation, retire and verification each append exactly one
/// record, carry the owning ResourceContext of the loaded Connection, keep identity in structured JSON Details
/// (<c>Resource</c> stays null because the audit store would corrupt Details with it), and never carry a secret value,
/// a secret reference, a config value or remote verification text.
/// </summary>
public sealed class ConnectionLifecycleAuditTests : IAsyncDisposable
{
    private const string HostValue = "db.internal.example";
    private const string SecretValue = "hunter2-s3cret-value";
    private const string RemoteSummary = "remote said: token=abc123 for db.internal.example";
    private static readonly ConnectorDefinitionId Sql = new("Apperture.Platform.Sql");
    private static readonly string SecretRefGuid = Guid.NewGuid().ToString("D");

    private readonly FailingSaveInterceptor _failSaves = new();
    private readonly AppertureDbContext _db;
    private readonly RecordingAudit _audit = new();
    private readonly Mock<IAuthorizationEvaluationService> _auth = new();
    private readonly Mock<IConnectionVerificationService> _verification = new();
    private readonly Mock<ISecretStore> _secretStore = new();
    private readonly ConnectionManagementService _sut;
    private readonly SqlConnectionRepository _repository;

    public ConnectionLifecycleAuditTests()
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(_failSaves)
                .Options);
        foreach (var (id, kind, reserved) in new[]
                 {
                     (ResourceContextIds.SharedValue, ResourceContextKinds.Shared, true),
                     ("app-a", ResourceContextKinds.Application, false)
                 })
        {
            _db.ResourceContexts.Add(new ResourceContextEntity
            {
                Id = id, Name = id, Slug = id, Kind = kind, State = ResourceContextStates.Active,
                IsReserved = reserved, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch
            });
        }
        _db.SaveChanges();

        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:auditor"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
        _auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var definition = new ConnectorDefinition(Sql, "Platform SQL", null, new Dictionary<string, string> { ["Host"] = "string" })
        {
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("Host", "Server", false),
                new ConnectionFieldDescriptor("Database", "Database", false),
                new ConnectionFieldDescriptor("Password", "Password", true)
            ],
            Capabilities = ConnectorCapabilities.SupportsConnectionVerification
        };
        var registry = new Mock<IConnectorDefinitionRegistry>();
        registry.Setup(r => r.Get(Sql)).Returns(definition);

        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionVerificationResult(ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, RemoteSummary));

        _repository = new SqlConnectionRepository(_db);
        _sut = new ConnectionManagementService(
            principal.Object,
            _auth.Object,
            _repository,
            registry.Object,
            _verification.Object,
            new ConnectionSecretMaterialProvisioner(_secretStore.Object),
            new ResourceContextService(new SqlResourceContextRepository(_db), TimeProvider.System),
            _db,
            new SqlResourceOwnershipMutationGate(_db),
            _audit);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private ValueTask<Connection> CreateAsync(string? contextId = "app-a") =>
        _sut.CreateConnectionAsync(new CreateConnectionRequest(
            Sql,
            "Warehouse",
            new Dictionary<string, string> { ["Host"] = HostValue },
            new Dictionary<string, string> { ["Password"] = SecretRefGuid },
            ResourceContextId: contextId is null ? null : new ResourceContextId(contextId)));

    private static JsonElement Details(AuditRecord record) => JsonDocument.Parse(record.Details!).RootElement;

    // AU1 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AU1_create_appends_exactly_one_created_record()
    {
        var created = await CreateAsync();

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Created);
        record.Outcome.Should().Be("succeeded");
        record.ActorId.Value.Should().Be("user:auditor");
        Details(record).GetProperty("resourceId").GetString().Should().Be(created.Id.Value);
    }

    [Fact]
    public async Task AU1_a_name_and_config_update_appends_exactly_one_updated_record()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Warehouse renamed", new Dictionary<string, string> { ["Host"] = HostValue, ["Database"] = "Ops" }, null, null));

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Updated);
        var details = Details(record);
        details.GetProperty("nameChanged").GetBoolean().Should().BeTrue();
        details.GetProperty("configFieldsChanged").EnumerateArray().Select(e => e.GetString()).Should().Equal("Database");
        details.GetProperty("secretFieldsRotated").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AU1_secret_rotation_appends_exactly_one_secret_rotated_record_naming_only_the_field()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.SecretRotated);
        Details(record).GetProperty("secretFieldsRotated").EnumerateArray().Select(e => e.GetString()).Should().Equal("Password");
    }

    [Fact]
    public async Task AU1_retire_appends_exactly_one_retired_record()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.RetireConnectionAsync(created.Id);

        _audit.Records.Should().ContainSingle().Which.Action.Should().Be(ConnectionAuditActionNames.Retired);
    }

    [Fact]
    public async Task AU1_verification_appends_exactly_one_verified_record_with_the_status_only()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.VerifyConnectionAsync(created.Id);

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Verified);
        record.Outcome.Should().Be("succeeded");
        Details(record).GetProperty("verificationStatus").GetString().Should().Be("Success");
    }

    [Fact]
    public async Task A_failed_verification_is_recorded_with_a_failed_outcome_not_dropped()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionVerificationResult(ConnectionVerificationStatus.Failed, DateTimeOffset.UtcNow, RemoteSummary));

        await _sut.VerifyConnectionAsync(created.Id);

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be("failed");
        Details(record).GetProperty("verificationStatus").GetString().Should().Be("Failed");
    }

    // AU2 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AU2_context_comes_from_the_loaded_connection_and_follows_a_move()
    {
        var created = await CreateAsync("app-a");
        _audit.Records.Single().ResourceContextId.Should().Be("app-a");

        await _repository.SetResourceContextIdAsync(created.Id, ResourceContextIds.Shared);
        _audit.Records.Clear();
        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("Renamed", null, null, null));
        await _sut.VerifyConnectionAsync(created.Id);
        await _sut.RetireConnectionAsync(created.Id);

        _audit.Records.Should().HaveCount(3);
        _audit.Records.Should().OnlyContain(r => r.ResourceContextId == ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task AU2_a_connection_created_without_a_context_is_audited_as_shared()
    {
        await CreateAsync(contextId: null);

        _audit.Records.Single().ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
    }

    // AU3 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AU3_no_secret_value_secret_ref_config_value_or_remote_text_reaches_the_audit_record()
    {
        var created = await CreateAsync();
        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));
        await _sut.VerifyConnectionAsync(created.Id);
        await _sut.RetireConnectionAsync(created.Id);

        _audit.Records.Should().HaveCount(4);
        foreach (var record in _audit.Records)
        {
            var text = JsonSerializer.Serialize(record);
            text.Should().NotContain(SecretValue);
            text.Should().NotContain(SecretRefGuid);
            text.Should().NotContain(HostValue, "config values are never audited, only field keys");
            text.Should().NotContain("token=abc123");
            text.Should().NotContain("remote said");
        }
    }

    // AU4 / AU5 -------------------------------------------------------------------------------------

    [Fact]
    public async Task AU4_AU5_every_record_has_a_null_resource_and_valid_json_details_with_connection_identity()
    {
        var created = await CreateAsync();
        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("Renamed", null, null, null));
        await _sut.VerifyConnectionAsync(created.Id);
        await _sut.RetireConnectionAsync(created.Id);

        _audit.Records.Should().HaveCount(4);
        foreach (var record in _audit.Records)
        {
            record.Resource.Should().BeNull("a non-null Resource is string-appended into Details by the SQL audit store");
            var details = Details(record);
            details.GetProperty("resourceType").GetString().Should().Be("Connection");
            details.GetProperty("resourceId").GetString().Should().Be(created.Id.Value);
            details.GetProperty("connectorDefinitionId").GetString().Should().Be(Sql.Value);
            record.CorrelationId.Value.Should().NotBeNullOrEmpty();
        }
    }

    // AU7 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AU7_reading_a_connection_appends_nothing()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.GetConnectionAsync(created.Id);
        await _sut.ListConnectionsAsync();
        await _sut.ListConnectionsByConnectorAsync(Sql);

        _audit.Records.Should().BeEmpty();
    }

    // Failure semantics -----------------------------------------------------------------------------

    [Fact]
    public async Task A_denied_or_invalid_operation_appends_no_lifecycle_record()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();
        _auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await FluentActions.Awaiting(() => _sut.RetireConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("x", null, null, null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => _sut.VerifyConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        _audit.Records.Should().BeEmpty("nothing happened, so nothing is recorded as having happened");
    }

    // Secret-change classification --------------------------------------------------------------------

    [Fact]
    public async Task Blank_secret_values_are_skipped_by_the_provisioner_so_the_update_is_not_a_rotation()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Renamed", null, null, new Dictionary<string, string> { ["Password"] = "   " }));

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Updated);
        Details(record).GetProperty("secretFieldsRotated").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Pointing_a_field_at_a_different_secret_reference_is_a_rotation_without_recording_either_reference()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();
        var otherRef = Guid.NewGuid().ToString("D");

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, new Dictionary<string, string> { ["Password"] = otherRef }, null));

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.SecretRotated);
        Details(record).GetProperty("secretFieldsRotated").EnumerateArray().Select(e => e.GetString()).Should().Equal("Password");
        record.Details.Should().NotContain(otherRef).And.NotContain(SecretRefGuid);
    }

    [Fact]
    public async Task Detaching_a_secret_reference_is_recorded_as_a_secret_change()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, new Dictionary<string, string>(), null));

        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.SecretRotated);
        Details(record).GetProperty("secretFieldsRotated").EnumerateArray().Select(e => e.GetString()).Should().Equal("Password");
    }

    [Fact]
    public async Task Creating_with_secret_values_records_no_value_and_no_newly_minted_reference()
    {
        var minted = Guid.NewGuid();
        _secretStore.Setup(s => s.CreateAsync(It.IsAny<SecretCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretDescriptor(new SecretId(minted), "n", "p", SecretMaterialType.OpaqueString, "v1", SecretState.Active, DateTimeOffset.UtcNow));
        _secretStore.Setup(s => s.AddVersionAsync(It.IsAny<SecretId>(), It.IsAny<SecretMaterial>(), It.IsAny<CancellationToken>())).ReturnsAsync("v1");

        var created = await _sut.CreateConnectionAsync(new CreateConnectionRequest(
            Sql, "Warehouse",
            new Dictionary<string, string> { ["Host"] = HostValue },
            new Dictionary<string, string>(),
            SecretValues: new Dictionary<string, string> { ["Password"] = SecretValue }));

        created.SecretRefs["Password"].Should().Be(minted.ToString("D"), "precondition: a reference really was minted");
        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Created);
        record.Details.Should().NotContain(SecretValue).And.NotContain(minted.ToString("D")).And.NotContain(minted.ToString("N"));
    }

    [Fact]
    public async Task A_rotation_whose_update_then_fails_validation_is_still_recorded_as_failed()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        // An unknown config key is rejected while normalizing, before any secret write. A missing REQUIRED field is only
        // caught by ValidateConnectionMaterial, which runs after the secret material has been written.
        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, new Dictionary<string, string>(), null,
            new Dictionary<string, string> { ["Password"] = SecretValue }));

        await act.Should().ThrowAsync<InvalidOperationException>();
        var record = _audit.Records.Should().ContainSingle("the credential may already have rotated in the secret store").Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.SecretRotated);
        record.Outcome.Should().Be("failed");
        JsonSerializer.Serialize(record).Should().NotContain(SecretValue);
    }

    [Fact]
    public async Task A_save_that_fails_does_not_leave_the_update_tracked_for_the_shared_audit_context_to_commit()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();
        _failSaves.Fail = true;

        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Renamed", null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));

        await act.Should().ThrowAsync<InvalidOperationException>("the simulated save failure");
        _audit.Records.Should().ContainSingle().Which.Outcome.Should().Be("failed");

        // The audit store shares this context; the next save it performs must not carry the rejected update.
        _failSaves.Fail = false;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        (await _repository.GetByIdAsync(created.Id))!.Name.Should().Be("Warehouse");
    }

    [Fact]
    public async Task An_invalid_update_that_touches_no_secret_writes_no_record()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();

        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, new Dictionary<string, string> { ["Unknown"] = "x" }, null, null));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_verifier_that_throws_still_leaves_one_failed_record_and_no_exception_text()
    {
        var created = await CreateAsync();
        _audit.Records.Clear();
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(RemoteSummary));

        var act = async () => await _sut.VerifyConnectionAsync(created.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var record = _audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.Verified);
        record.Outcome.Should().Be("failed");
        JsonSerializer.Serialize(record).Should().NotContain("token=abc123");
    }

    [Fact]
    public async Task A_broken_audit_sink_on_a_failure_path_does_not_replace_the_original_exception()
    {
        var created = await CreateAsync();
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("verifier timed out"));
        _audit.ThrowOnAppend = true;

        var act = async () => await _sut.VerifyConnectionAsync(created.Id);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task A_denied_create_appends_no_record()
    {
        _auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = async () => await CreateAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _audit.Records.Should().BeEmpty();
    }

    // AU8 -------------------------------------------------------------------------------------------

    /// <summary>
    /// AU8, replaced by AUD-1.1 (audit hardening design gate §22, AUD-1.1 scope): the row schema is the gate's §10.1, §11.8,
    /// §13 and §14 columns. A Connection target is the generic <c>TargetType</c>/<c>TargetId</c> pair; there is still no
    /// resource-descriptor or connection-specific column.
    /// </summary>
    [Fact]
    public void AU8_the_audit_row_schema_is_the_aud1_schema_with_a_generic_target_and_no_connection_column()
    {
        var columns = typeof(AuditLogEntity).GetProperties().Select(p => p.Name).ToList();

        columns.Should().NotContain(c => c.Contains("Resource", StringComparison.Ordinal) && c != nameof(AuditLogEntity.ResourceContextId));
        columns.Should().NotContain(c => c.Contains("Connection", StringComparison.Ordinal));
        columns.Should().BeEquivalentTo(
        [
            "Id", "At", "Actor", "Action", "Details", "Outcome", "ModuleId", "ScheduleId", "RunId", "ResourceContextId", "CorrelationId",
            "EventId", "OperationEventId", "Producer", "DetailsFormat", "ClassificationState",
            "ActorKind", "ActorUserId", "ActorUsername", "ActorExternalProvider", "ActorExternalTenantId", "ActorExternalSubjectId",
            "ActorAttribution", "AttributionIssue", "AttributionIssueCertainty", "TargetType", "TargetId",
            "Category", "Severity", "Visibility", "Basis", "OutcomeCode", "OutcomeProvenance",
        ]);
    }

    [Fact]
    public void The_action_names_are_distinct_from_the_permission_actions()
    {
        new[]
        {
            ConnectionAuditActionNames.Created, ConnectionAuditActionNames.Updated, ConnectionAuditActionNames.SecretRotated,
            ConnectionAuditActionNames.Retired, ConnectionAuditActionNames.Verified
        }.Should().OnlyHaveUniqueItems().And.OnlyContain(a => a.StartsWith("connection.", StringComparison.Ordinal));
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Simulated save failure.") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public bool ThrowOnAppend { get; set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            if (ThrowOnAppend)
                throw new InvalidOperationException("Simulated audit sink failure.");
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
