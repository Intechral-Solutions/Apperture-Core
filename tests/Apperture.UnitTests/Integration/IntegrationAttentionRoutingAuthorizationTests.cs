using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// An integration's attention-routing override is part of the integration, so changing or clearing it needs
/// <c>integration.update</c> on that integration. Reading it (<c>integration.read</c>) is not enough, and the decision is made again
/// once the integration row is locked so an identity change (an ownership move) between the first check and the write is seen.
/// These tests run the real controller routes, the real authorization helper and the real routing service over one EF Core
/// InMemory database; the lock itself is proven against SQL Server in <c>IntegrationRowLockSqlServerTests</c>.
/// </summary>
public sealed class IntegrationAttentionRoutingAuthorizationTests : IDisposable
{
    private const string Shared = "shared";
    private const string Elsewhere = "app-elsewhere";

    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly AppertureDbContext _db;
    private readonly SqlIntegrationRepository _integrations;
    private readonly Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork _audit = new();

    public IntegrationAttentionRoutingAuthorizationTests()
    {
        _db = NewDb();
        _integrations = new SqlIntegrationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // ---- a read-only operator cannot change routing ----

    [Fact]
    public async Task A_read_only_operator_cannot_create_an_override()
    {
        var integration = await NewIntegrationAsync();
        var policy = new Policy(allowRead: true, allowUpdate: false);
        var controller = NewController(policy);

        var result = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: false), null, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        (await RowsAsync(integration)).Should().BeEmpty("a refused write stores nothing");
    }

    [Fact]
    public async Task A_read_only_operator_cannot_change_an_existing_override()
    {
        var integration = await NewIntegrationAsync();
        await SeedOverrideAsync(integration, enabled: true);
        var controller = NewController(new Policy(allowRead: true, allowUpdate: false));

        var result = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: false), null, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        var row = (await RowsAsync(integration)).Should().ContainSingle().Subject;
        row.IsEnabled.Should().BeTrue("the persisted override is exactly as it was");
        row.EscalationAfterTicks.Should().Be(TimeSpan.FromMinutes(30).Ticks);
    }

    [Fact]
    public async Task A_read_only_operator_cannot_clear_an_override()
    {
        var integration = await NewIntegrationAsync();
        await SeedOverrideAsync(integration, enabled: true);
        var controller = NewController(new Policy(allowRead: true, allowUpdate: false));

        var result = await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        (await RowsAsync(integration)).Should().ContainSingle("the override is still there");
    }

    [Fact]
    public async Task An_operator_who_cannot_even_read_the_integration_is_forbidden_on_both_routes_without_an_update_check()
    {
        var integration = await NewIntegrationAsync();
        await SeedOverrideAsync(integration, enabled: true);
        var policy = new Policy(allowRead: false, allowUpdate: true);
        var controller = NewController(policy);

        (await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: false), null, CancellationToken.None))
            .Result.Should().BeOfType<ForbidResult>();
        (await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None)).Should().BeOfType<ForbidResult>();

        policy.UpdateChecks.Should().Be(0, "the read check refuses first");
        (await RowsAsync(integration)).Should().ContainSingle().Which.IsEnabled.Should().BeTrue();
    }

    // ---- an operator with integration.update can ----

    [Fact]
    public async Task An_operator_with_update_can_create_change_and_clear_an_override()
    {
        var integration = await NewIntegrationAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true);
        var controller = NewController(policy);

        var created = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: true), null, CancellationToken.None);
        created.Result.Should().BeOfType<OkObjectResult>();
        (await RowsAsync(integration)).Should().ContainSingle().Which.IsEnabled.Should().BeTrue();

        var changed = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: false), null, CancellationToken.None);
        changed.Result.Should().BeOfType<OkObjectResult>();
        (await RowsAsync(integration)).Should().ContainSingle().Which.IsEnabled.Should().BeFalse();

        var cleared = await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None);
        cleared.Should().BeOfType<NoContentResult>();
        (await RowsAsync(integration)).Should().BeEmpty();
        _audit.Actions.Should().Equal(
            Apperture.Core.Application.Audit.AuditActionNames.AttentionRoutingOverrideCreated,
            Apperture.Core.Application.Audit.AuditActionNames.AttentionRoutingOverrideUpdated,
            Apperture.Core.Application.Audit.AuditActionNames.AttentionRoutingOverrideCleared);
    }

    [Fact]
    public async Task Clearing_when_no_override_exists_is_still_a_no_content_no_op_for_an_operator_with_update()
    {
        var integration = await NewIntegrationAsync();
        var controller = NewController(new Policy(allowRead: true, allowUpdate: true));

        var result = await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        (await RowsAsync(integration)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_integration_is_a_404_for_both_routes_before_any_update_check()
    {
        var policy = new Policy(allowRead: true, allowUpdate: true);
        var controller = NewController(policy);

        (await controller.PutAttentionRouting("int-gone", Body(true), null, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
        (await controller.DeleteAttentionRouting("int-gone", null, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        policy.UpdateChecks.Should().Be(0);
    }

    [Fact]
    public async Task The_permission_evaluated_is_integration_update_on_that_integration_before_and_again_under_the_lock()
    {
        var integration = await NewIntegrationAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true);
        var controller = NewController(policy);

        await controller.PutAttentionRouting(integration.Id.Value, Body(true), null, CancellationToken.None);

        policy.UpdateChecks.Should().Be(2, "once before the write and once more under the integration row lock");
        policy.UpdateChecksOn.Should().OnlyContain(id => id == integration.Id.Value);

        policy.Reset();
        await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None);

        policy.UpdateChecks.Should().Be(2, "the clear is re-authorized under the lock too, whether or not an override exists");
        policy.UpdateChecksOn.Should().OnlyContain(id => id == integration.Id.Value);
    }

    // ---- an identity change between the first check and the write is seen ----

    [Fact]
    public async Task An_ownership_move_after_the_first_check_makes_the_put_forbidden_and_stores_nothing()
    {
        var integration = await NewIntegrationAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true, contextSensitive: true, db: NewDb());
        // The first update check passes; the integration then moves to a context this operator may not update.
        policy.AfterFirstUpdateCheck = () => MoveAsync(integration, Elsewhere);
        var controller = NewController(policy);

        var result = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: true), null, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>("the check under the lock reads the integration as it now is");
        policy.UpdateChecks.Should().Be(2);
        (await RowsAsync(integration)).Should().BeEmpty();
        _audit.Actions.Should().BeEmpty("a write refused under the lock records nothing");
    }

    [Fact]
    public async Task An_ownership_move_after_the_first_check_makes_the_clear_forbidden_and_keeps_the_override()
    {
        var integration = await NewIntegrationAsync();
        await SeedOverrideAsync(integration, enabled: true);
        var policy = new Policy(allowRead: true, allowUpdate: true, contextSensitive: true, db: NewDb());
        policy.AfterFirstUpdateCheck = () => MoveAsync(integration, Elsewhere);
        var controller = NewController(policy);

        var result = await controller.DeleteAttentionRouting(integration.Id.Value, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        policy.UpdateChecks.Should().Be(2);
        (await RowsAsync(integration)).Should().ContainSingle("the move happened before the lock; the refused clear removed nothing");
        _audit.Actions.Should().ContainSingle()
            .Which.Should().Be(
                Apperture.Core.Application.Audit.AuditActionNames.AttentionRoutingOverrideCreated,
                "only the seed was recorded; the refused clear recorded nothing");
    }

    [Fact]
    public async Task An_ownership_move_to_a_context_the_operator_may_update_still_lets_the_write_through()
    {
        var integration = await NewIntegrationAsync();
        var policy = new Policy(allowRead: true, allowUpdate: true, contextSensitive: true, db: NewDb());
        policy.AllowedContexts.Add(Elsewhere);
        policy.AfterFirstUpdateCheck = () => MoveAsync(integration, Elsewhere);
        var controller = NewController(policy);

        var result = await controller.PutAttentionRouting(integration.Id.Value, Body(enabled: true), null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        (await RowsAsync(integration)).Should().ContainSingle();
    }

    // ---- no secondary entry point ----

    [Fact]
    public void The_two_integration_level_routing_writers_are_called_only_by_the_authorized_routes()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var callers = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.\s*(UpsertIntegrationRoutingAsync|ClearIntegrationRoutingAsync)\s*\("))
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .ToList();

        callers.Should().BeEquivalentTo(
            ["Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs"],
            "a new caller of an integration-level routing write must be reviewed for its authorization");

        var controller = File.ReadAllText(Path.Combine(
            src, "Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs"));
        foreach (var route in new[] { "PutAttentionRouting", "DeleteAttentionRouting" })
        {
            var body = RouteBody(controller, route);
            body.Should().Contain("RequireIntegrationManageAsync", $"{route} requires integration.update before it writes");
            body.Should().Contain("ReauthorizeRoutingWriteAsync", $"{route} passes the re-run check to the locked write");
        }

        // Nothing else writes the overrides table: a direct write elsewhere would skip the service and the routes' authorization.
        var mutation = new Regex(
            @"IntegrationAttentionRoutings\s*\.\s*(Add|AddAsync|AddRange|Remove|RemoveRange|Update|ExecuteDelete|ExecuteDeleteAsync|ExecuteUpdate|ExecuteUpdateAsync)\b"
            + @"|\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+\[?IntegrationAttentionRoutings\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var writers = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => mutation.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .ToList();
        writers.Should().BeEquivalentTo(
            ["Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs"],
            "only the routing service writes integration routing overrides");

        // The helper both routes pass is the update permission, not the read check alone.
        RouteBody(controller, "ValueTask ReauthorizeRoutingWriteAsync").Should().Contain("RequireIntegrationManageAsync");
    }

    [Fact]
    public void The_helper_that_requires_update_evaluates_the_update_permission()
    {
        var text = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationSettingsAuthorization.cs"));

        Regex.Match(text, @"RequireIntegrationManageAsync[\s\S]*?return actor;").Value
            .Should().Contain("ActionConstants.IntegrationUpdate");
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
            Mock.Of<IAttentionIncidentAcknowledgementService>(),
            new AttentionRoutingManagementService(
                _db, _integrations, new SqlAttentionRecipientResolver(_db, new OperationalTeamRecipientExpander(_db)), _audit),
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

    private async Task SeedOverrideAsync(DomainIntegration integration, bool enabled) =>
        await new AttentionRoutingManagementService(_db, _integrations, null!, _audit).UpsertIntegrationRoutingAsync(
            integration.Id,
            AttentionConditionKeys.ExpectedRunMissing,
            new AttentionRoutingUpsertCommand(enabled, true, false, false, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), null, null));

    private async Task<List<IntegrationAttentionRoutingEntity>> RowsAsync(DomainIntegration integration)
    {
        await using var db = NewDb();
        return await db.IntegrationAttentionRoutings.AsNoTracking().Where(r => r.IntegrationId == integration.Id.Value).ToListAsync();
    }

    private async Task MoveAsync(DomainIntegration integration, string contextId)
    {
        await using var db = NewDb();
        await new SqlIntegrationRepository(db).SetResourceContextIdAsync(
            integration.Id, new Apperture.Core.Domain.ResourceContexts.ResourceContextId(contextId));
    }

    private static UpsertAttentionRoutingRequest Body(bool enabled) =>
        new(enabled, true, false, false, TimeSpan.FromMinutes(30).Ticks, TimeSpan.FromMinutes(10).Ticks, null, null);

    private static string RouteBody(string source, string method)
    {
        var start = source.IndexOf(method + "(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{method} exists");
        var end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        return source[start..(end < 0 ? source.Length : end)];
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

        public void Reset() => _updateChecksOn.Clear();

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
