using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Connection lifecycle audit (AUD-1.4 slice 5). Create, update (including secret rotation) and retire are audited units: each
/// stages exactly one primary row, carries the owning ResourceContext of the connection as locked, names fields and never values,
/// and never carries a secret value, a secret reference, a config value or remote text. Verification probes first and then records
/// what the verifier answered in a unit of its own. The transactional guarantees are proven against SQL Server in
/// <c>ConnectionLifecycleSqlServerTests</c> and <c>ConnectionVerifyBrowseSqlServerTests</c>.
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
    private readonly RecordingAuditedUnitOfWork _unit = new();
    private readonly Mock<IAuthorizationEvaluationService> _auth = new();
    private readonly Mock<IConnectionVerificationService> _verification = new();
    private readonly Mock<ISecretStore> _secretStore = new();
    private readonly Mock<IPrincipalContext> _principal = new();
    private readonly Mock<IConnectorDefinitionRegistry> _registry = new();
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
        _principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
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
        _registry.Setup(r => r.Get(Sql)).Returns(definition);

        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionVerificationResult(ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, RemoteSummary));

        _repository = new SqlConnectionRepository(_db);
        _sut = NewService(_unit);
    }

    private ConnectionManagementService NewService(IAuditedUnitOfWork? unit) =>
        new(
            _principal.Object,
            _auth.Object,
            _repository,
            _registry.Object,
            _verification.Object,
            new ConnectionSecretMaterialProvisioner(_secretStore.Object),
            new ResourceContextService(new SqlResourceContextRepository(_db), TimeProvider.System),
            _db,
            new SqlResourceOwnershipMutationGate(_db),
            unit);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private ValueTask<Connection> CreateAsync(string? contextId = "app-a") =>
        _sut.CreateConnectionAsync(new CreateConnectionRequest(
            Sql,
            "Warehouse",
            new Dictionary<string, string> { ["Host"] = HostValue },
            new Dictionary<string, string> { ["Password"] = SecretRefGuid },
            ResourceContextId: contextId is null ? null : new ResourceContextId(contextId)));

    private string Everything() =>
        JsonSerializer.Serialize(_unit.Staged.Select(e => new { e.Action, e.Target, e.OutcomeCode, e.Fields, e.Related }));

    // Create ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_creation_stages_exactly_one_created_row_for_the_connection_in_its_context()
    {
        var created = await CreateAsync();

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionCreated);
        var evt = _unit.Single(AuditActionNames.ConnectionCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Connection, created.Id.Value));
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Related.ResourceContextId.Should().Be("app-a");
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Warehouse",
            ["connectorId"] = Sql.Value,
            ["fieldsStored"] = 0,
        });
    }

    [Fact]
    public async Task A_connection_created_without_a_context_is_recorded_in_shared()
    {
        await CreateAsync(contextId: null);

        _unit.Single(AuditActionNames.ConnectionCreated).Related.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task Creating_with_secret_values_stages_the_created_row_and_a_stored_row_per_field_but_no_value_or_reference()
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
        _unit.Actions.Should().Equal(AuditActionNames.ConnectionCreated, AuditActionNames.ConnectionSecretStored);
        _unit.Single(AuditActionNames.ConnectionCreated).Fields["fieldsStored"].Should().Be(1);
        _unit.Single(AuditActionNames.ConnectionSecretStored).Fields.Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["fieldKey"] = "Password", ["change"] = "created" });
        Everything().Should().NotContain(SecretValue).And.NotContain(minted.ToString("D")).And.NotContain(minted.ToString("N"));
    }

    [Fact]
    public async Task A_creation_whose_material_check_fails_stages_nothing_and_stores_nothing()
    {
        var act = () => _sut.CreateConnectionAsync(new CreateConnectionRequest(
            Sql, "Warehouse", new Dictionary<string, string>(), new Dictionary<string, string>())).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>("the required host is missing");
        _unit.Actions.Should().BeEmpty();
        (await _db.Connections.CountAsync()).Should().Be(0);
    }

    // Update and rotation ---------------------------------------------------------------------------

    [Fact]
    public async Task A_name_and_config_update_stages_one_updated_row_naming_fields_and_not_values()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        var updated = await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Warehouse renamed", new Dictionary<string, string> { ["Host"] = HostValue, ["Database"] = "Ops" }, null, null));

        updated.Name.Should().Be("Warehouse renamed");
        _unit.Actions.Should().Equal(AuditActionNames.ConnectionUpdated);
        var evt = _unit.Single(AuditActionNames.ConnectionUpdated);
        evt.Fields["nameChanged"].Should().Be(true);
        evt.Fields["configChanged"].Should().BeEquivalentTo(new[] { "Database" });
        evt.Fields.Should().NotContainKey("rotated");
        evt.Fields["name"].Should().Be("Warehouse renamed", "the name snapshot is the new name");
        Everything().Should().NotContain(HostValue).And.NotContain("Ops");
    }

    [Fact]
    public async Task A_secret_rotation_stages_one_rotated_row_counting_the_field_and_a_stored_row_naming_it()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored);
        _unit.Single(AuditActionNames.ConnectionSecretRotated).Fields.Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["connectorId"] = Sql.Value, ["fieldsRotated"] = 1 });
        _unit.Single(AuditActionNames.ConnectionSecretStored).Fields.Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["fieldKey"] = "Password", ["change"] = "replaced" },
            "the field already named a secret, so the material became a new version of it");
        Everything().Should().NotContain(SecretValue).And.NotContain(SecretRefGuid);
    }

    [Fact]
    public async Task Material_for_a_field_that_named_no_secret_is_stored_as_a_new_secret_and_recorded_as_created()
    {
        var created = await _sut.CreateConnectionAsync(new CreateConnectionRequest(
            Sql, "Warehouse", new Dictionary<string, string> { ["Host"] = HostValue }, new Dictionary<string, string>()));
        _unit.Operations.Clear();
        var minted = Guid.NewGuid();
        _secretStore.Setup(s => s.CreateAsync(It.IsAny<SecretCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretDescriptor(new SecretId(minted), "n", "p", SecretMaterialType.OpaqueString, "v1", SecretState.Active, DateTimeOffset.UtcNow));
        _secretStore.Setup(s => s.AddVersionAsync(It.IsAny<SecretId>(), It.IsAny<SecretMaterial>(), It.IsAny<CancellationToken>())).ReturnsAsync("v1");

        var updated = await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));

        updated.SecretRefs["Password"].Should().Be(minted.ToString("D"));
        _unit.Actions.Should().Equal(AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored);
        _unit.Single(AuditActionNames.ConnectionSecretStored).Fields["change"].Should().Be("created");
        Everything().Should().NotContain(minted.ToString("D")).And.NotContain(SecretValue);
    }

    [Fact]
    public async Task Blank_secret_values_are_skipped_so_the_update_is_not_a_rotation()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Renamed", null, null, new Dictionary<string, string> { ["Password"] = "   " }));

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionUpdated);
        _unit.Single(AuditActionNames.ConnectionUpdated).Fields.Should().NotContainKey("rotated");
    }

    [Fact]
    public async Task Pointing_a_field_at_a_different_secret_reference_is_a_rotation_without_recording_either_reference()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        var otherRef = Guid.NewGuid().ToString("D");

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, new Dictionary<string, string> { ["Password"] = otherRef }, null));

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionSecretRotated);
        _unit.Single(AuditActionNames.ConnectionSecretRotated).Fields["fieldsRotated"].Should().Be(1);
        Everything().Should().NotContain(otherRef).And.NotContain(SecretRefGuid);
    }

    [Fact]
    public async Task Detaching_a_secret_reference_is_recorded_as_a_secret_change()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, null, new Dictionary<string, string>(), null));

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionSecretRotated);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_declares_a_no_op_and_records_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        var same = await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Warehouse", new Dictionary<string, string> { ["Host"] = HostValue }, new Dictionary<string, string> { ["Password"] = SecretRefGuid }, null));
        var empty = await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(null, null, null, null));

        _unit.Actions.Should().BeEmpty();
        _unit.Operations.Should().HaveCount(2).And.OnlyContain(o => o.NoOp);
        same.Name.Should().Be("Warehouse");
        empty.Id.Should().Be(created.Id);
        _secretStore.Invocations.Should().BeEmpty("no material was supplied, so the store was never touched");
    }

    [Fact]
    public async Task An_update_authorizes_again_against_the_locked_connection_and_a_refusal_there_changes_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _secretStore.Invocations.Clear();
        _auth.SetupSequence(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)   // the early check
            .ReturnsAsync(false); // the check against the locked row (for example after an ownership move)

        var act = () => _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Renamed", null, null, new Dictionary<string, string> { ["Password"] = SecretValue })).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _unit.Actions.Should().BeEmpty();
        _secretStore.Invocations.Should().BeEmpty("the credential is not touched before the locked-row decision");
        _db.ChangeTracker.Clear();
        (await _repository.GetByIdAsync(created.Id))!.Name.Should().Be("Warehouse");
    }

    [Fact]
    public async Task A_rotation_that_then_fails_validation_stages_nothing_and_the_failure_is_the_validation_error()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        // Replacing the config with an empty one drops the required host: caught by the material check, after the material was
        // handed to the store. The store joins the unit's transaction, so nothing of it outlives the failed attempt.
        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, new Dictionary<string, string>(), null,
            new Dictionary<string, string> { ["Password"] = SecretValue }));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Host*");
        _unit.Actions.Should().BeEmpty("a failed attempt commits no row, and the rotation is not recorded as having happened");
    }

    [Fact]
    public async Task A_save_that_fails_does_not_leave_the_update_tracked_for_a_later_save_to_commit()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _failSaves.Fail = true;

        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            "Renamed", null, null, new Dictionary<string, string> { ["Password"] = SecretValue }));

        await act.Should().ThrowAsync<InvalidOperationException>("the simulated save failure");
        _unit.Actions.Should().BeEmpty();

        _failSaves.Fail = false;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        (await _repository.GetByIdAsync(created.Id))!.Name.Should().Be("Warehouse");
    }

    [Fact]
    public async Task An_invalid_update_that_touches_no_secret_stages_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        var act = async () => await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest(
            null, new Dictionary<string, string> { ["Unknown"] = "x" }, null, null));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _unit.Actions.Should().BeEmpty();
        _unit.Operations.Should().BeEmpty("a request invalid on its own never opens a unit");
    }

    // Retire ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_retirement_stages_one_retired_row_and_a_second_retirement_records_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        await _sut.RetireConnectionAsync(created.Id);
        await _sut.RetireConnectionAsync(created.Id);

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionRetired);
        _unit.Operations.Should().HaveCount(2);
        _unit.Operations[1].NoOp.Should().BeTrue("retiring a retired connection changes nothing");
        _db.ChangeTracker.Clear();
        (await _repository.GetByIdAsync(created.Id))!.State.Should().Be(ConnectionState.Retired);
    }

    // Context and authorization ---------------------------------------------------------------------

    [Fact]
    public async Task The_context_comes_from_the_locked_connection_and_follows_a_move()
    {
        var created = await CreateAsync("app-a");
        _unit.Single(AuditActionNames.ConnectionCreated).Related.ResourceContextId.Should().Be("app-a");

        await _repository.SetResourceContextIdAsync(created.Id, ResourceContextIds.Shared);
        _unit.Operations.Clear();
        await _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("Renamed", null, null, null));
        await _sut.RetireConnectionAsync(created.Id);

        _unit.Staged.Should().HaveCount(2).And.OnlyContain(e => e.Related.ResourceContextId == ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task A_denied_operation_stages_no_row_and_opens_no_unit()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await FluentActions.Awaiting(() => _sut.RetireConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => _sut.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("x", null, null, null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => _sut.VerifyConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => CreateAsync().AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        _unit.Operations.Should().BeEmpty("nothing happened, so nothing is recorded as having happened");
    }

    [Fact]
    public async Task A_missing_connection_is_a_not_found_that_records_nothing()
    {
        var missing = new ConnectionId("missing");

        await FluentActions.Awaiting(() => _sut.UpdateConnectionAsync(missing, new UpdateConnectionRequest("x", null, null, null)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        await FluentActions.Awaiting(() => _sut.RetireConnectionAsync(missing).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");

        _unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_converted_mutation_refuses_to_run_without_the_audited_unit()
    {
        var created = await CreateAsync();
        var noUnit = NewService(null);

        await FluentActions.Awaiting(() => noUnit.UpdateConnectionAsync(created.Id, new UpdateConnectionRequest("x", null, null, null)).AsTask())
            .Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => noUnit.RetireConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => noUnit.CreateConnectionAsync(new CreateConnectionRequest(
            Sql, "Other", new Dictionary<string, string> { ["Host"] = HostValue }, new Dictionary<string, string>())).AsTask())
            .Should().ThrowAsync<AuditInvariantException>();
        (await _db.Connections.CountAsync()).Should().Be(1, "nothing was changed unaudited");
    }

    // Ambiguous commits -----------------------------------------------------------------------------

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_connection_and_runs_the_attempt_once()
    {
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var created = await CreateAsync();

        created.Name.Should().Be("Warehouse");
        _unit.Operations.Should().ContainSingle();
        (await _db.Connections.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown()
    {
        var created = await CreateAsync();
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        await FluentActions.Awaiting(() => _sut.RetireConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    // Verification ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_successful_verification_stages_one_verified_row_with_the_status_only_and_stores_the_result()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        var verified = await _sut.VerifyConnectionAsync(created.Id);

        _unit.Actions.Should().Equal(AuditActionNames.ConnectionVerified);
        var evt = _unit.Single(AuditActionNames.ConnectionVerified);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Connection, created.Id.Value));
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Warehouse",
            ["connectorId"] = Sql.Value,
            ["verificationStatus"] = "success",
        });
        verified.LastVerificationStatus.Should().Be(ConnectionVerificationStatus.Success);
        verified.LastVerificationSummary.Should().Be(RemoteSummary, "the summary stays on the connection row");
        Everything().Should().NotContain("token=abc123").And.NotContain("remote said");
    }

    [Fact]
    public async Task A_failed_verification_is_recorded_as_failed_and_an_unavailable_one_as_unknown()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _verification.SetupSequence(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionVerificationResult(ConnectionVerificationStatus.Failed, DateTimeOffset.UtcNow, RemoteSummary))
            .ReturnsAsync(new ConnectionVerificationResult(ConnectionVerificationStatus.Unavailable, DateTimeOffset.UtcNow, "no verifier"));

        await _sut.VerifyConnectionAsync(created.Id);
        await _sut.VerifyConnectionAsync(created.Id);

        _unit.Staged.Select(e => (e.OutcomeCode, e.Fields["verificationStatus"])).Should().Equal(
            (AuditOutcomeCodes.Failed, (object)"failed"), (AuditOutcomeCodes.Unknown, "unavailable"));
    }

    [Fact]
    public async Task A_verifier_that_throws_is_recorded_as_a_failed_attempt_with_no_status_and_its_exception_is_rethrown_unchanged()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        var failure = new InvalidOperationException(RemoteSummary);
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var act = async () => await _sut.VerifyConnectionAsync(created.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        var evt = _unit.Staged.Should().ContainSingle().Subject;
        evt.Action.Should().Be(AuditActionNames.ConnectionVerified);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Failed);
        evt.Fields.Should().NotContainKey("verificationStatus", "the verifier produced no answer");
        Everything().Should().NotContain("token=abc123");
        _db.ChangeTracker.Clear();
        (await _repository.GetByIdAsync(created.Id))!.LastVerificationStatus.Should().Be(ConnectionVerificationStatus.NeverTested);
    }

    [Fact]
    public async Task A_request_cancelled_during_the_probe_still_leaves_its_record_and_the_cancellation_propagates()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        using var cts = new CancellationTokenSource();
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = async () => await _sut.VerifyConnectionAsync(created.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _unit.Staged.Should().ContainSingle().Which.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown, "a cancelled probe produced no answer, so the platform observed neither success nor failure");
    }

    [Fact]
    public async Task The_probe_runs_before_the_unit_and_the_record_is_written_from_the_row_as_locked()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        var probeSawOperations = -1;
        _verification.Setup(v => v.VerifyAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .Returns((Connection _, CancellationToken _) =>
            {
                probeSawOperations = _unit.Operations.Count;
                // The connection is renamed by someone else while the probe is running.
                _db.ChangeTracker.Clear();
                _repository.UpdateAsync((_repository.GetByIdAsync(created.Id).AsTask().Result)! with { Name = "Renamed meanwhile" }).AsTask().Wait();
                return ValueTask.FromResult(new ConnectionVerificationResult(ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, "ok"));
            });

        var verified = await _sut.VerifyConnectionAsync(created.Id);

        probeSawOperations.Should().Be(0, "no unit was open while the remote host was being probed");
        verified.Name.Should().Be("Renamed meanwhile", "only the verification columns are written, over the row as it is when locked");
        _unit.Single(AuditActionNames.ConnectionVerified).Fields["name"].Should().Be("Renamed meanwhile");
    }

    [Fact]
    public async Task A_denied_verification_never_probes_and_records_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _verification.Invocations.Clear();
        _auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await FluentActions.Awaiting(() => _sut.VerifyConnectionAsync(created.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        _verification.Invocations.Should().BeEmpty();
        _unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lost_acknowledgement_of_the_verification_record_returns_the_committed_connection()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var verified = await _sut.VerifyConnectionAsync(created.Id);

        verified.LastVerificationStatus.Should().Be(ConnectionVerificationStatus.Success);
        _unit.Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task Reading_a_connection_records_nothing()
    {
        var created = await CreateAsync();
        _unit.Operations.Clear();

        await _sut.GetConnectionAsync(created.Id);
        await _sut.ListConnectionsAsync();
        await _sut.ListConnectionsByConnectorAsync(Sql);

        _unit.Operations.Should().BeEmpty();
    }

    // Row schema ------------------------------------------------------------------------------------

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
            AuditActionNames.ConnectionCreated, AuditActionNames.ConnectionUpdated, AuditActionNames.ConnectionSecretRotated,
            AuditActionNames.ConnectionRetired, AuditActionNames.ConnectionVerified, AuditActionNames.ConnectionStructureBrowsed,
            AuditActionNames.ConnectionStructureBrowseFailed, AuditActionNames.ConnectionSecretStored,
        }.Should().OnlyHaveUniqueItems().And.OnlyContain(a => a.StartsWith("connection.", StringComparison.Ordinal));
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Simulated save failure.") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
