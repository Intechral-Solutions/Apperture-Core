using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// SEC-2.1: acknowledging an attention incident changes how the integration alerts, so it needs <c>integration.update</c> on that
/// integration. Reading it (<c>integration.read</c>) is not enough, and the decision is made again once the integration row is
/// locked so an ownership move between the first check and the write is seen. These tests run the real controller route, the real
/// authorization helper and the real acknowledgement service over one EF Core InMemory database; the locks themselves are proven
/// against SQL Server in <c>AttentionIncidentAcknowledgementSqlServerTests</c>, and the whole route against the host in
/// <c>ManagementHostAttentionIncidentAcknowledgementHttpTests</c>.
/// </summary>
public sealed class AttentionIncidentAcknowledgementAuthorizationTests : IDisposable
{
    private const string Shared = "shared";
    private const string Elsewhere = "app-elsewhere";
    private const string NoteSentinel = "note-sentinel-do-not-record";

    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly AppertureDbContext _db;
    private readonly SqlIntegrationRepository _integrations;
    private readonly RecordingAuditedUnitOfWork _audit = new();

    public AttentionIncidentAcknowledgementAuthorizationTests()
    {
        _db = NewDb();
        _integrations = new SqlIntegrationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // ---- a read-only operator cannot acknowledge (A1.1) ----

    [Fact]
    public async Task A_read_only_operator_is_forbidden_and_nothing_is_written_or_recorded()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var policy = new Policy(allowRead: true, allowUpdate: false);
        var before = await IncidentAsync(incidentId);

        var result = await NewController(policy).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new AcknowledgeAttentionIncidentRequestBody(NoteSentinel), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        (await IncidentAsync(incidentId)).Should().BeEquivalentTo(before, "a refused acknowledgement changes no column");
        _audit.Operations.Should().BeEmpty("the refusal is answered before any unit opens");
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_operator_who_cannot_even_read_the_integration_is_forbidden_without_an_update_check()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var policy = new Policy(allowRead: false, allowUpdate: true);

        var result = await NewController(policy).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new AcknowledgeAttentionIncidentRequestBody(null), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        policy.UpdateChecks.Should().Be(0, "the read check refuses first");
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
    }

    // ---- an operator with integration.update can ----

    [Fact]
    public async Task An_operator_with_update_acknowledges_and_the_permission_is_checked_before_and_again_under_the_lock()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true);

        var result = await NewController(policy).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new AcknowledgeAttentionIncidentRequestBody("seen"), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var row = await IncidentAsync(incidentId);
        row.AcknowledgedAtUtc.Should().NotBeNull();
        row.AcknowledgementNote.Should().Be("seen");
        policy.UpdateChecks.Should().Be(2, "once before the unit and once more under the integration row lock");
        policy.UpdateChecksOn.Should().OnlyContain(id => id == integration.Id.Value);
        _audit.Actions.Should().Equal(AuditActionNames.AttentionIncidentAcknowledged);
    }

    [Fact]
    public async Task The_acknowledger_is_the_unit_actor_and_not_the_principal_the_controller_saw()
    {
        var (integration, incidentId) = await ArrangeAsync();
        _audit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_resolved_by_the_unit"));

        await NewController(new Policy(true, true)).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new AcknowledgeAttentionIncidentRequestBody(null), CancellationToken.None);

        (await IncidentAsync(incidentId)).AcknowledgedBy.Should().Be("usr_resolved_by_the_unit", "the controller's own actor id is operator-1");
    }

    [Fact]
    public async Task A_missing_request_body_acknowledges_without_a_note()
    {
        var (integration, incidentId) = await ArrangeAsync();

        var result = await NewController(new Policy(true, true)).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, body: null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        (await IncidentAsync(incidentId)).AcknowledgementNote.Should().BeNull();
    }

    [Fact]
    public async Task A_repeated_acknowledgement_is_a_no_content_no_op()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var controller = NewController(new Policy(true, true));
        await controller.AcknowledgeAttentionIncident(integration.Id.Value, incidentId, new("first"), CancellationToken.None);
        var first = await IncidentAsync(incidentId);

        var repeat = await controller.AcknowledgeAttentionIncident(integration.Id.Value, incidentId, new("second"), CancellationToken.None);

        repeat.Should().BeOfType<NoContentResult>();
        (await IncidentAsync(incidentId)).Should().BeEquivalentTo(first);
        _audit.Actions.Should().ContainSingle();
    }

    // ---- refusals ----

    [Fact]
    public async Task A_resolved_incident_is_a_409_with_the_not_active_code_and_nothing_is_written()
    {
        var (integration, incidentId) = await ArrangeAsync(resolved: true);

        var result = await NewController(new Policy(true, true)).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new(NoteSentinel), CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        CodeOf(conflict.Value).Should().Be("attention_incident_not_active");
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_note_over_1024_characters_is_a_400_and_nothing_is_written()
    {
        var (integration, incidentId) = await ArrangeAsync();

        var result = await NewController(new Policy(true, true)).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new(new string('n', 1025)), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_incident_and_another_integrations_incident_are_404()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var other = await NewIntegrationAsync();
        var controller = NewController(new Policy(true, true));

        (await controller.AcknowledgeAttentionIncident(integration.Id.Value, "attn_missing", new(null), CancellationToken.None))
            .Should().BeOfType<NotFoundResult>();
        (await controller.AcknowledgeAttentionIncident(other.Id.Value, incidentId, new(null), CancellationToken.None))
            .Should().BeOfType<NotFoundResult>();
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_integration_is_a_404_before_any_update_check()
    {
        var policy = new Policy(true, true);

        var result = await NewController(policy).AcknowledgeAttentionIncident("int-gone", "attn_x", new(null), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        policy.UpdateChecks.Should().Be(0);
    }

    // ---- an identity change between the first check and the write is seen (A1.2) ----

    [Fact]
    public async Task An_ownership_move_after_the_first_check_makes_the_acknowledgement_forbidden_and_stores_nothing()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true, contextSensitive: true, db: NewDb());
        // The first update check passes; the integration then moves to a context this operator may not update.
        policy.AfterFirstUpdateCheck = () => MoveAsync(integration, Elsewhere);

        var result = await NewController(policy).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new(NoteSentinel), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>("the check under the lock reads the integration as it now is");
        policy.UpdateChecks.Should().Be(2);
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
        _audit.Actions.Should().BeEmpty("an acknowledgement refused under the lock records nothing");
    }

    [Fact]
    public async Task An_ownership_move_to_a_context_the_operator_may_update_still_lets_the_acknowledgement_through()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true, contextSensitive: true, db: NewDb());
        policy.AllowedContexts.Add(Elsewhere);
        policy.AfterFirstUpdateCheck = () => MoveAsync(integration, Elsewhere);

        var result = await NewController(policy).AcknowledgeAttentionIncident(
            integration.Id.Value, incidentId, new(null), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().NotBeNull();
        _audit.Single(AuditActionNames.AttentionIncidentAcknowledged).Related.ResourceContextId
            .Should().Be(Elsewhere, "the row names the owner the unit locked, not the one the caller first read");
    }

    // ---- no secondary entry point ----

    [Fact]
    public void Only_the_authorized_route_calls_the_acknowledgement_service_and_only_the_service_writes_the_acknowledgement_columns()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var production = ProductionFiles(src).ToList();

        var users = production
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bIAttentionIncidentAcknowledgementService\b|\bnew\s+AttentionIncidentAcknowledgementService\b"))
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .ToList();
        users.Should().BeEquivalentTo(
            [
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionIncidentAcknowledgementService.cs",
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs",
                "Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs",
            ],
            "a new caller of the acknowledgement must be reviewed for its authorization");

        var controller = File.ReadAllText(Path.Combine(src, "Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs"));
        var route = RouteBody(controller, "AcknowledgeAttentionIncident");
        route.Should().Contain("RequireIntegrationManageAsync", "the route requires integration.update before it acknowledges");
        route.Should().Contain("ReauthorizeAcknowledgementAsync", "the route passes the re-run check to the locked write");
        RouteBody(controller, "ValueTask ReauthorizeAcknowledgementAsync").Should().Contain("RequireIntegrationManageAsync");
        route.Should().NotContain("RequireIntegrationReadAsync");

        // The three columns are assigned by the service alone: a write elsewhere would skip the route's authorization and the audit row.
        var assignment = new Regex(@"\b(AcknowledgedAtUtc|AcknowledgedBy|AcknowledgementNote)\s*=(?!=)", RegexOptions.CultureInvariant);
        var rawSql = new Regex(
            @"\b(UPDATE|INSERT\s+INTO)\s+\[?AttentionIncidents\b[^;]*?\bAcknowledg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        // The failed-item acknowledgements (OperationItemAttentionAcknowledgements) use similar names for another table: only files
        // that name an attention incident can write these columns.
        var writers = production
            .Where(f => File.ReadAllText(f).Contains("AttentionIncident", StringComparison.Ordinal))
            .Where(f => assignment.IsMatch(File.ReadAllText(f)) || rawSql.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .ToList();
        writers.Should().BeEquivalentTo(
            ["Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionIncidentAcknowledgementService.cs"],
            "only the acknowledgement service writes who acknowledged an incident (the one other writer, the start-time actor-id normalization "
            + "in HistoricalActorNormalization, rewrites stored actor ids to user ids through dynamic SQL and is a documented exclusion in the closure guard)");
    }

    [Fact]
    public void The_incident_tracker_has_no_acknowledgement_write()
    {
        typeof(IAttentionIncidentTracker).GetMethods().Select(m => m.Name).Should().NotContain("AcknowledgeAsync");
        typeof(SqlAttentionIncidentTracker).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Should().NotContain("AcknowledgeAsync");
    }

    [Fact]
    public void The_service_acknowledges_only_through_the_audited_unit_and_the_audit_row_never_names_a_note()
    {
        var text = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionIncidentAcknowledgementService.cs"));

        text.Should().Contain("IAuditedUnitOfWork").And.Contain("AuditActor.CurrentPrincipal")
            .And.Contain("transaction.Actor.RecordedValue", "the acknowledger is the unit's recorded actor");

        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AttentionIncidentAcknowledged);
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo(
            ["name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided"]);
        definition.Category.Should().Be(AuditCategories.Operations);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.TargetType.Should().Be(AuditTargetTypes.Integration);
        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
    }

    // ---- wiring ----

    private AppertureDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private IntegrationsController NewController(Policy policy)
    {
        var principal = new FixedPrincipalContext();
        var integrationService = new Mock<IIntegrationManagementService>();
        // The read check, as the real service makes it: refused for a caller who may not read the integration, otherwise the
        // integration as persisted now (or null when it is gone).
        integrationService
            .Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .Returns<IntegrationId, CancellationToken>(async (id, ct) =>
            {
                if (!policy.CanRead)
                    throw new UnauthorizedAccessException("Not authorized to view this integration.");
                return await _integrations.GetByIdAsync(id, ct);
            });

        return new IntegrationsController(
            integrationService.Object,
            Mock.Of<IIntegrationReadinessService>(),
            Mock.Of<IIntegrationConfigurationRegistry>(),
            Mock.Of<IIntegrationScheduleDiagnosticsService>(),
            new AttentionIncidentAcknowledgementService(_db, _integrations, _audit, TimeProvider.System),
            Mock.Of<IAttentionRoutingManagementService>(),
            principal,
            Mock.Of<IResourceContextService>(),
            Mock.Of<IResourceOwnershipMoveService>(),
            new IntegrationSettingsAuthorization(principal, policy, _integrations));
    }

    private async Task<DomainIntegration> NewIntegrationAsync()
    {
        var integration = new DomainIntegration(
            new IntegrationId("int-" + Guid.NewGuid().ToString("N")[..8]),
            "Sample Integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch)
        {
            ResourceContextId = new Apperture.Core.Domain.ResourceContexts.ResourceContextId(Shared),
        };
        await _integrations.AddAsync(integration);
        return integration;
    }

    private async Task<(DomainIntegration Integration, string IncidentId)> ArrangeAsync(bool resolved = false)
    {
        var integration = await NewIntegrationAsync();
        var incidentId = "attn_" + Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        _db.AttentionIncidents.Add(new AttentionIncidentEntity
        {
            Id = incidentId,
            IntegrationId = integration.Id.Value,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            Status = resolved ? AttentionIncidentStatuses.Resolved : AttentionIncidentStatuses.Active,
            StartedAtUtc = started,
            LastObservedAtUtc = started,
            ResolvedAtUtc = resolved ? started.AddHours(1) : null,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (integration, incidentId);
    }

    private async Task<AttentionIncidentEntity> IncidentAsync(string incidentId)
    {
        await using var db = NewDb();
        return await db.AttentionIncidents.AsNoTracking().SingleAsync(e => e.Id == incidentId);
    }

    private async Task MoveAsync(DomainIntegration integration, string contextId)
    {
        await using var db = NewDb();
        await new SqlIntegrationRepository(db).SetResourceContextIdAsync(
            integration.Id, new Apperture.Core.Domain.ResourceContexts.ResourceContextId(contextId));
    }

    private static string? CodeOf(object? body) => body?.GetType().GetProperty("code")?.GetValue(body) as string;

    private static string RouteBody(string source, string method)
    {
        var start = source.IndexOf(method + "(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{method} exists");
        var end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        return source[start..(end < 0 ? source.Length : end)];
    }

    private static IEnumerable<string> ProductionFiles(string src)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                && !f.Contains($"{sep}Migrations{sep}", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // ---- doubles ----

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("operator-1");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Operator";
        }
    }

    /// <summary>
    /// Grants integration.read and integration.update independently. With <c>contextSensitive</c> it also requires the integration
    /// to be owned by an allowed resource context, read from the database at the moment of each update check, which is how a
    /// policy that depends on ownership sees a move.
    /// </summary>
    private sealed class Policy(bool allowRead, bool allowUpdate, bool contextSensitive = false, AppertureDbContext? db = null)
        : IAuthorizationEvaluationService
    {
        private readonly List<string> _updateChecksOn = [];

        public bool CanRead => allowRead;

        public HashSet<string> AllowedContexts { get; } = [Shared];

        public Func<Task>? AfterFirstUpdateCheck { get; set; }

        public int UpdateChecks => _updateChecksOn.Count;

        public IReadOnlyList<string> UpdateChecksOn => _updateChecksOn;

        public async ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(context.Permission.Action, ActionConstants.IntegrationRead, StringComparison.Ordinal))
                return allowRead;

            if (!string.Equals(context.Permission.Action, ActionConstants.IntegrationUpdate, StringComparison.Ordinal))
                return false;

            var integrationId = context.ResourceIdentity?.ResourceId ?? string.Empty;
            _updateChecksOn.Add(integrationId);
            var first = _updateChecksOn.Count == 1;

            var allowed = allowUpdate;
            if (allowed && contextSensitive)
            {
                var owner = await db!.Integrations.AsNoTracking()
                    .Where(e => e.Id == integrationId)
                    .Select(e => e.ResourceContextId)
                    .SingleAsync(cancellationToken);
                allowed = AllowedContexts.Contains(owner);
            }

            if (first && AfterFirstUpdateCheck is not null)
                await AfterFirstUpdateCheck();
            return allowed;
        }
    }
}
