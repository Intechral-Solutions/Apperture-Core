using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// <see cref="ResourceContextManagementService"/>: authorize via <c>resource_context.manage</c>, audit
/// on success, delegate every mutation to the (authorization-free) <see cref="IResourceContextService"/>;
/// delegate every read with no authorization check at all (CTX-3A §9.1, §9.2).
/// </summary>
public sealed class ResourceContextManagementServiceTests
{
    [Fact]
    public async Task CreateAsync_WithoutResourceContextManage_Throws()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task CreateAsync_WithResourceContextManage_CreatesAndAudits()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);

        created.Name.Should().Be("Canvas");
        harness.AuditRecords.Should().ContainSingle(r => r.Action == "resource_context.created" && r.ResourceContextId == created.Id.Value);
    }

    [Fact]
    public async Task ArchiveAsync_WithResourceContextManage_DelegatesToUnderlyingArchiveRule()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.SeedLiveIntegration(created.Id);

        var act = () => harness.Service.ArchiveAsync(created.Id, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active integration*");
    }

    [Fact]
    public async Task UpdateAsync_OnShared_Returns409StyleException()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

        var act = () => harness.Service.UpdateAsync(ResourceContextIds.Shared, name: "Renamed", slug: null, description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        // Reads are gated only by [Authorize] at the controller, never by resource_context.manage
        // (design gate §9.2) -- this must succeed even though the harness holds zero permissions.
        // The reserved Shared context always exists, so no create step (which WOULD require the
        // permission) is needed to exercise this.
        var found = await harness.Service.GetAsync(ResourceContextIds.Shared, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(ResourceContextIds.Shared);
    }

    [Fact]
    public async Task ArchiveThenUnarchive_WritesDistinctAuditActions()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);
        await harness.Service.UnarchiveAsync(created.Id, CancellationToken.None);

        harness.AuditRecords.Select(r => r.Action).Should().Contain(["resource_context.archived", "resource_context.unarchived"]);
    }

    [Fact]
    public async Task UpdateAsync_WithNoFieldsActuallyChanged_WritesNoAuditRecord()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);

        // Same name and description as already set -- nothing should actually change, so no audit entry.
        await harness.Service.UpdateAsync(created.Id, name: "Canvas", slug: null, description: "desc", CancellationToken.None);

        harness.AuditRecords.Should().NotContain(r => r.Action == "resource_context.updated");
    }

    [Fact]
    public async Task UpdateAsync_WhenRenameCommitsButSlugChangeThenFails_StillAuditsTheRename()
    {
        // Reviewer finding (Important #1): RenameAsync and ChangeSlugAsync each commit independently
        // (SqlResourceContextRepository.UpdateAsync ends in its own SaveChangesAsync), so a rename can
        // be durable before a later slug change throws. The finally block must still audit whatever
        // actually changed before the exception escapes.
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var canvas = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.Service.CreateAsync("Other", slug: "taken-slug", description: null, CancellationToken.None);

        var act = () => harness.Service.UpdateAsync(canvas.Id, name: "Renamed", slug: "taken-slug", description: null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in use*");

        // The rename committed before the slug change failed -- it must be visible and audited.
        var reloaded = await harness.Service.GetAsync(canvas.Id, CancellationToken.None);
        reloaded!.Name.Should().Be("Renamed");
        harness.AuditRecords.Should().ContainSingle(r =>
            r.Action == "resource_context.updated" &&
            r.ResourceContextId == canvas.Id.Value &&
            r.Details != null && r.Details.Contains("name") && !r.Details.Contains("slug"));
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
    public async Task ArchiveAsync_WhenAlreadyArchived_WritesNoAdditionalAuditRecord()
    {
        // Reviewer finding (Important #2): ResourceContextService.ArchiveAsync is a no-op (no state
        // change, no save) when already archived; the wrapper must not audit a no-op as if it happened.
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);

        // Second archive is a true no-op.
        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);

        harness.AuditRecords.Count(r => r.Action == "resource_context.archived" && r.ResourceContextId == created.Id.Value).Should().Be(1);
    }

    [Fact]
    public async Task UnarchiveAsync_WhenAlreadyActive_WritesNoAuditRecord()
    {
        // Reviewer finding (Important #2): ResourceContextService.UnarchiveAsync is a no-op when already
        // Active; a never-archived context must not accumulate a false "unarchived" audit trail.
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        await harness.Service.UnarchiveAsync(created.Id, CancellationToken.None);

        harness.AuditRecords.Should().NotContain(r => r.Action == "resource_context.unarchived");
    }

    [Fact]
    public async Task UnarchiveAsync_OnSharedContext_WritesNoAuditRecord()
    {
        // Reviewer's exact scenario: Shared is seeded Active and can never be archived, so calling
        // UnarchiveAsync on it must remain a true no-op with no audit record claiming otherwise.
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

        await harness.Service.UnarchiveAsync(ResourceContextIds.Shared, CancellationToken.None);

        harness.AuditRecords.Should().NotContain(r => r.Action == "resource_context.unarchived");
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
    public async Task ListAsync_RequiresNoPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();

        var list = await harness.Service.ListAsync(includeArchived: true, CancellationToken.None);

        list.Should().Contain(c => c.Id == ResourceContextIds.Shared);
    }
}
