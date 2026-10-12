using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// <see cref="ResourceContextManagementService"/>: authorize via <c>resource_context.manage</c>, run every mutation as an audited
/// unit that stages exactly one primary row (AUD-1.4 slice 5), delegate to the (authorization-free)
/// <see cref="IResourceContextService"/>; delegate every read with no authorization check at all (CTX-3A §9.1, §9.2). The
/// transactional guarantees are proven against SQL Server in <c>ResourceContextSqlServerTests</c>.
/// </summary>
public sealed class ResourceContextManagementServiceTests
{
    private static ManagementServiceTestHarness Manager() =>
        ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

    [Fact]
    public async Task CreateAsync_WithoutResourceContextManage_Throws()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.AuditUnit.Operations.Should().BeEmpty("a denied request never opens a unit");
    }

    [Fact]
    public async Task CreateAsync_WithResourceContextManage_CreatesAndStagesOneCreatedRow()
    {
        await using var harness = Manager();

        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);

        created.Name.Should().Be("Canvas");
        harness.AuditUnit.Actions.Should().Equal(AuditActionNames.ResourceContextCreated);
        var evt = harness.AuditUnit.Single(AuditActionNames.ResourceContextCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.ResourceContext, created.Id.Value));
        evt.Related.ResourceContextId.Should().Be(created.Id.Value);
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["name"] = "Canvas", ["slug"] = "canvas" });
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain("desc", "the description is never recorded");
    }

    [Fact]
    public async Task CreateAsync_WithATakenSlug_IsRefusedAndStagesNothing()
    {
        await using var harness = Manager();
        await harness.Service.CreateAsync("First", slug: "taken", description: null, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        var act = () => harness.Service.CreateAsync("Second", slug: "taken", description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in use*");
        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AfterALostAcknowledgement_ReturnsTheCommittedContextAndRunsOnce()
    {
        await using var harness = Manager();
        harness.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        created.Name.Should().Be("Canvas");
        harness.AuditUnit.Operations.Should().ContainSingle();
        (await harness.Service.GetAsync(created.Id, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WithAnUnknownOutcome_IsReportedAsUnknown()
    {
        await using var harness = Manager();
        harness.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task ArchiveAsync_WithResourceContextManage_DelegatesToUnderlyingArchiveRule()
    {
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.SeedLiveIntegration(created.Id);
        harness.AuditUnit.Operations.Clear();

        var act = () => harness.Service.ArchiveAsync(created.Id, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration not retired*");
        harness.AuditUnit.Actions.Should().BeEmpty("a refused archive records nothing");
    }

    [Fact]
    public async Task UpdateAsync_OnShared_IsRefusedAndStagesNothing()
    {
        await using var harness = Manager();

        var act = () => harness.Service.UpdateAsync(ResourceContextIds.Shared, name: "Renamed", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        // Reads are gated only by [Authorize] at the controller, never by resource_context.manage
        // (design gate §9.2) -- this must succeed even though the harness holds zero permissions.
        var found = await harness.Service.GetAsync(ResourceContextIds.Shared, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(ResourceContextIds.Shared);
    }

    [Fact]
    public async Task ArchiveThenUnarchive_StageDistinctRowsNamingTheContext()
    {
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        var archived = await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);
        var unarchived = await harness.Service.UnarchiveAsync(created.Id, CancellationToken.None);

        archived.State.Should().Be(ResourceContextStates.Archived);
        unarchived.State.Should().Be(ResourceContextStates.Active);
        harness.AuditUnit.Actions.Should().Equal(AuditActionNames.ResourceContextArchived, AuditActionNames.ResourceContextUnarchived);
        foreach (var evt in harness.AuditUnit.Staged)
        {
            evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.ResourceContext, created.Id.Value));
            evt.Related.ResourceContextId.Should().Be(created.Id.Value);
            evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["name"] = "Canvas" });
        }
    }

    [Fact]
    public async Task UpdateAsync_NamesTheFieldsThatChangedInDeclaredOrderAndNeverTheirValues()
    {
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        var updated = await harness.Service.UpdateAsync(created.Id, name: "Canvas Learning", slug: "canvas-learning", description: "a longer description", CancellationToken.None);

        updated.Slug.Should().Be("canvas-learning");
        var evt = harness.AuditUnit.Single(AuditActionNames.ResourceContextUpdated);
        evt.Fields["changed"].Should().BeEquivalentTo(new[] { "name", "slug", "description" }, o => o.WithStrictOrdering());
        evt.Fields["name"].Should().Be("Canvas Learning", "the name snapshot is the new name");
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain("longer description").And.NotContain("canvas-learning");
    }

    [Fact]
    public async Task UpdateAsync_WithNoFieldsActuallyChanged_DeclaresANoOpAndStagesNothing()
    {
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        // Same name and description as already set -- nothing should actually change, so no audit entry.
        await harness.Service.UpdateAsync(created.Id, name: "Canvas", slug: null, description: "desc", CancellationToken.None);

        harness.AuditUnit.Actions.Should().BeEmpty();
        harness.AuditUnit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_WithSlugThatNormalizesToTheStoredValue_StagesNothing()
    {
        // The slug branch compared the caller's RAW input against the NORMALIZED stored slug, so a raw value that only normalizes to
        // the same slug (for example "My Slug" over a stored "my-slug") slipped past a naive pre-call check. Actual before/after
        // state decides.
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: "my-slug", description: null, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        await harness.Service.UpdateAsync(created.Id, name: null, slug: "My Slug", description: null, CancellationToken.None);

        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenARenameIsFollowedByARefusedSlugChange_RecordsNothingAndFailsAsOneOperation()
    {
        // The rename and the re-slug used to commit independently, so a refused slug change left the rename durable and audited. They
        // are one unit now: the refusal fails the whole operation and no row is staged. (The rollback of the rename itself needs a
        // real transaction; it is proven against SQL Server.)
        await using var harness = Manager();
        var canvas = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.Service.CreateAsync("Other", slug: "taken-slug", description: null, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        var act = () => harness.Service.UpdateAsync(canvas.Id, name: "Renamed", slug: "taken-slug", description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in use*");
        harness.AuditUnit.Actions.Should().BeEmpty("a failed attempt commits no row");
    }

    [Fact]
    public async Task UpdateAsync_OfAMissingContext_IsANotFoundThatStagesNothing()
    {
        await using var harness = Manager();

        var act = () => harness.Service.UpdateAsync(new ResourceContextId("missing"), name: "x", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WithoutResourceContextManage_Throws()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.UpdateAsync(ResourceContextIds.Shared, name: "X", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ArchiveAsync_WithoutResourceContextManage_Throws()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.ArchiveAsync(ResourceContextIds.Shared, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task UnarchiveAsync_WithoutResourceContextManage_Throws()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.UnarchiveAsync(ResourceContextIds.Shared, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ArchiveAsync_WhenAlreadyArchived_DeclaresANoOpAndStagesNothingMore()
    {
        // ResourceContextService.ArchiveAsync is a no-op (no state change, no save) when already archived; it must not be recorded as
        // if it happened.
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);

        harness.AuditUnit.Actions.Should().BeEmpty();
        harness.AuditUnit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    [Fact]
    public async Task UnarchiveAsync_WhenAlreadyActive_DeclaresANoOpAndStagesNothing()
    {
        // A never-archived context must not accumulate a false "unarchived" audit trail.
        await using var harness = Manager();
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        harness.AuditUnit.Operations.Clear();

        await harness.Service.UnarchiveAsync(created.Id, CancellationToken.None);

        harness.AuditUnit.Actions.Should().BeEmpty();
        harness.AuditUnit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    [Fact]
    public async Task UnarchiveAsync_OnSharedContext_DeclaresANoOpAndStagesNothing()
    {
        // Shared is seeded Active and can never be archived, so calling UnarchiveAsync on it must remain a true no-op.
        await using var harness = Manager();

        await harness.Service.UnarchiveAsync(ResourceContextIds.Shared, CancellationToken.None);

        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_OfTheSharedContext_IsRefusedAndStagesNothing()
    {
        await using var harness = Manager();

        var act = () => harness.Service.ArchiveAsync(ResourceContextIds.Shared, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
        harness.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_mutation_after_the_authorization_check_refuses_to_run_without_the_audited_unit()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(
            ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, withoutAuditedUnit: true);

        await FluentActions.Awaiting(() => harness.Service.CreateAsync("Canvas", null, null, CancellationToken.None).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => harness.Service.UpdateAsync(ResourceContextIds.Shared, "x", null, null, CancellationToken.None).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => harness.Service.ArchiveAsync(ResourceContextIds.Shared, CancellationToken.None).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => harness.Service.UnarchiveAsync(ResourceContextIds.Shared, CancellationToken.None).AsTask()).Should().ThrowAsync<AuditInvariantException>();
    }

    [Fact]
    public async Task GetOwnershipSummaryAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var summary = await harness.Service.GetOwnershipSummaryAsync(ResourceContextIds.Shared, CancellationToken.None);

        summary.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOwnershipSummariesAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var summaries = await harness.Service.GetOwnershipSummariesAsync(CancellationToken.None);

        summaries.Should().NotBeNull();
    }

    [Fact]
    public async Task Archive_reads_ownership_under_the_ownership_gate()
    {
        // Documents the contract at the unit level: ArchiveAsync must not evaluate the ownership summary before acquiring the gate.
        // The observable proof is the Move_into_target_and_archive_target_are_serialized SQL Server test; this test pins the call
        // order so a refactor cannot silently drop the gate.
        await using var harness = ManagementServiceTestHarness.WithRecordingGate();
        var app = await harness.Service.CreateAsync("CampusNexus", slug: null, description: null, CancellationToken.None);

        await harness.Service.ArchiveAsync(app.Id, CancellationToken.None);

        harness.Gate!.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }

    [Fact]
    public async Task Update_and_unarchive_take_no_ownership_gate_since_they_make_nothing_live_and_move_nothing()
    {
        await using var harness = ManagementServiceTestHarness.WithRecordingGate();
        var app = await harness.Service.CreateAsync("CampusNexus", slug: null, description: null, CancellationToken.None);
        await harness.Service.ArchiveAsync(app.Id, CancellationToken.None);
        harness.Gate!.Reset();

        await harness.Service.UnarchiveAsync(app.Id, CancellationToken.None);
        await harness.Service.UpdateAsync(app.Id, name: "Renamed", slug: null, description: null, CancellationToken.None);

        harness.Gate.Acquisitions.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var list = await harness.Service.ListAsync(includeArchived: true, CancellationToken.None);

        list.Should().Contain(c => c.Id == ResourceContextIds.Shared);
    }
}
