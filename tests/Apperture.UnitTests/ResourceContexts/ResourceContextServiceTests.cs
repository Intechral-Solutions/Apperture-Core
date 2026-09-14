using System.Reflection;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Supported ResourceContext lifecycle: reserved-context guards, slug rules, archive rules, and the
/// deliberate absence of a hard delete.
/// </summary>
public sealed class ResourceContextServiceTests
{
    [Fact]
    public void SharedIdentity_IsTheReservedLiteral()
    {
        ResourceContextIds.SharedValue.Should().Be("shared");
        ResourceContextIds.Shared.Value.Should().Be("shared");
        ResourceContextIds.IsShared(ResourceContextIds.Shared).Should().BeTrue();
        ResourceContextIds.IsShared(new ResourceContextId("payments")).Should().BeFalse();
    }

    [Fact]
    public async Task Shared_IsSeededReservedActiveAndSlugged()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var shared = await service.GetAsync(ResourceContextIds.Shared);

        shared.Should().NotBeNull();
        shared!.Name.Should().Be("Shared");
        shared.Slug.Should().Be("shared");
        shared.Kind.Should().Be(ResourceContextKinds.Shared);
        shared.State.Should().Be(ResourceContextStates.Active);
        shared.IsReserved.Should().BeTrue();
        shared.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Shared_CannotBeRenamed()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.RenameAsync(ResourceContextIds.Shared, "Common");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*cannot be renamed*");
    }

    [Fact]
    public async Task Shared_CannotBeReSlugged()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.ChangeSlugAsync(ResourceContextIds.Shared, "common");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*cannot be re-slugged*");
    }

    [Fact]
    public async Task Shared_CannotBeArchived()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.ArchiveAsync(ResourceContextIds.Shared);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*cannot be archived*");
    }

    /// <summary>
    /// Historical provenance resolves a recorded ResourceContextId back to its row for display, so a row
    /// must never physically disappear. Archive is the only retirement path.
    /// </summary>
    [Fact]
    public void NoSupportedHardDeleteExists()
    {
        MemberNamesOf<IResourceContextService>().Should().NotContain(n => n.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        MemberNamesOf<IResourceContextRepository>().Should().NotContain(n => n.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        MemberNamesOf<IResourceContextService>().Should().Contain("ArchiveAsync");

        static IEnumerable<string> MemberNamesOf<T>() =>
            typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name);
    }

    [Fact]
    public async Task CreateApplicationContext_NormalizesSlugAndDefaultsItFromName()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var created = await service.CreateApplicationContextAsync("Student Records");

        created.Kind.Should().Be(ResourceContextKinds.Application);
        created.State.Should().Be(ResourceContextStates.Active);
        created.IsReserved.Should().BeFalse();
        created.Slug.Should().Be("student-records");
        created.Id.Value.Should().NotBeNullOrWhiteSpace();
        created.Id.Value.Should().NotBe(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task CreateApplicationContext_PersistsOnlyTheNormalizedSlug()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var created = await service.CreateApplicationContextAsync("Payments", slug: "  Payments Ops__X  ");

        created.Slug.Should().Be("payments-ops-x");
        (await service.GetBySlugAsync("Payments Ops__X"))!.Id.Value.Should().Be(created.Id.Value);
    }

    /// <summary>Names are not unique; slugs are.</summary>
    [Fact]
    public async Task CreateApplicationContext_AllowsDuplicateNamesButNotDuplicateSlugs()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        await service.CreateApplicationContextAsync("Payments", slug: "payments-a");
        var second = await service.CreateApplicationContextAsync("Payments", slug: "payments-b");
        second.Name.Should().Be("Payments");

        var act = async () => await service.CreateApplicationContextAsync("Payments Again", slug: "payments-a");
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*already in use*");
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("Shared")]
    [InlineData("system")]
    [InlineData("SYSTEM")]
    public async Task CreateApplicationContext_RejectsReservedSlugs(string slug)
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.CreateApplicationContextAsync("Anything", slug);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*is reserved*");
    }

    [Fact]
    public async Task CreateApplicationContext_RejectsAnUnusableSlug()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.CreateApplicationContextAsync("Payments", slug: "***");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*usable ResourceContext slug*");
    }

    [Fact]
    public async Task ApplicationContext_CanBeRenamedAndReSlugged()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");

        var renamed = await service.RenameAsync(created.Id, "Payments Operations");
        renamed.Name.Should().Be("Payments Operations");
        renamed.Slug.Should().Be("payments", because: "renaming does not silently re-slug");

        var reslugged = await service.ChangeSlugAsync(created.Id, "Payments Ops");
        reslugged.Slug.Should().Be("payments-ops");
    }

    [Fact]
    public async Task Archive_IsRejectedWhileTheContextOwnsLiveResources()
    {
        var (service, repository) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");
        repository.OwnershipSummaries[created.Id.Value] = new ResourceContextOwnershipSummary(
            LiveIntegrations: 2, RetiredIntegrations: 0,
            ActiveConnections: 1, RetiredConnections: 0,
            EnabledSchedules: 3, DisabledSchedules: 0);

        var act = async () => await service.ArchiveAsync(created.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*2 active integration*1 active connection*3 enabled schedule*");
    }

    /// <summary>
    /// The "nothing live" archive rule (CTX-3A §6.5, D2): retired Connections (never deletable) and
    /// disabled Schedules (never deletable) must not make archive unreachable.
    /// </summary>
    [Fact]
    public async Task Archive_SucceedsWhenOnlyRetiredOrDisabledResourcesRemain()
    {
        var (service, repository) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");
        repository.OwnershipSummaries[created.Id.Value] = new ResourceContextOwnershipSummary(
            LiveIntegrations: 0, RetiredIntegrations: 4,
            ActiveConnections: 0, RetiredConnections: 2,
            EnabledSchedules: 0, DisabledSchedules: 1);

        var archived = await service.ArchiveAsync(created.Id);

        archived.State.Should().Be(ResourceContextStates.Archived);
    }

    [Fact]
    public async Task ArchiveThenUnarchive_RoundTrips()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");

        var archived = await service.ArchiveAsync(created.Id);
        archived.State.Should().Be(ResourceContextStates.Archived);
        archived.IsActive.Should().BeFalse();

        (await service.ListAsync(includeArchived: false)).Should().NotContain(c => c.Id.Value == created.Id.Value);
        (await service.ListAsync(includeArchived: true)).Should().Contain(c => c.Id.Value == created.Id.Value);

        var unarchived = await service.UnarchiveAsync(created.Id);
        unarchived.State.Should().Be(ResourceContextStates.Active);
    }

    /// <summary>Archiving hides a context from creation; it never invalidates existing references.</summary>
    [Fact]
    public async Task ArchivedContext_StillResolvesById()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");
        await service.ArchiveAsync(created.Id);

        (await service.GetAsync(created.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveForNewOwnership_DefaultsToSharedWhenNoneSupplied()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var resolved = await service.ResolveForNewOwnershipAsync(null);

        resolved.Id.Value.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task ResolveForNewOwnership_AcceptsAnActiveApplicationContext()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");

        (await service.ResolveForNewOwnershipAsync(created.Id)).Id.Value.Should().Be(created.Id.Value);
    }

    [Fact]
    public async Task ResolveForNewOwnership_RejectsAnUnknownContext()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();

        var act = async () => await service.ResolveForNewOwnershipAsync(new ResourceContextId("does-not-exist"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*not found*");
    }

    [Fact]
    public async Task ResolveForNewOwnership_RejectsAnArchivedContext()
    {
        var (service, _) = ResourceContextTestFactory.CreateWithShared();
        var created = await service.CreateApplicationContextAsync("Payments");
        await service.ArchiveAsync(created.Id);

        var act = async () => await service.ResolveForNewOwnershipAsync(created.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*archived*");
    }
}
