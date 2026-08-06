using Apperture.ControlPlane.Security;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class SharedRecordReferenceQueryServiceTests
{
    [Fact]
    public async Task Lookup_returns_references_from_two_modules_for_same_customer_key()
    {
        var db = NewDb();
        SeedItem(db, "item-fi", "g-fi", "run-fi", "Sample.FileImport", "int-fi", "File Import", "C-100", "customer");
        SeedItem(db, "item-ap", "g-ap", "run-ap", "Sample.ApiPolling", "int-ap", "API Polling", "C-100", "customer");
        SeedItem(db, "item-other", "g-o", "run-o", "Sample.FileImport", "int-fi", "File Import", "C-200", "customer");
        SeedItem(db, "item-student", "g-s", "run-s", "Sample.FileImport", "int-fi", "File Import", "C-100", "student");
        await db.SaveChangesAsync();

        var svc = Create(db, allow: true);
        var page = await svc.GetReferencesAsync(new SharedRecordIdentity(SharedRecordTypes.Customer, "C-100"));

        page.TotalCount.Should().Be(2);
        page.Items.Select(i => i.ModuleId).Should().BeEquivalentTo(["Sample.FileImport", "Sample.ApiPolling"]);
        page.Items.Should().OnlyContain(i => i.BusinessKey == "C-100" && i.RecordType == "customer");
    }

    [Fact]
    public async Task Lookup_scopes_strictly_by_record_type()
    {
        var db = NewDb();
        SeedItem(db, "item-c", "g1", "r1", "Sample.FileImport", null, null, "C-100", "customer");
        SeedItem(db, "item-s", "g2", "r2", "Sample.FileImport", null, null, "C-100", "student");
        await db.SaveChangesAsync();

        var svc = Create(db, allow: true);
        var page = await svc.GetReferencesAsync(new SharedRecordIdentity("student", "C-100"));
        page.TotalCount.Should().Be(1);
        page.Items.Single().OperationItemId.Should().Be("item-s");
    }

    [Fact]
    public async Task Lookup_is_case_insensitive_for_customer_keys()
    {
        var db = NewDb();
        SeedItem(db, "item-1", "g1", "r1", "Sample.FileImport", null, null, "C-100", "customer");
        await db.SaveChangesAsync();

        var svc = Create(db, allow: true);
        var page = await svc.GetReferencesAsync(new SharedRecordIdentity("CUSTOMER", "c-100"));
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Lookup_denied_without_integration_read()
    {
        var db = NewDb();
        var svc = Create(db, allow: false);
        var act = async () => await svc.GetReferencesAsync(new SharedRecordIdentity(SharedRecordTypes.Customer, "C-100"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Lookup_orders_newest_first_and_pages()
    {
        var db = NewDb();
        var older = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var newer = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        SeedItem(db, "old", "g1", "r1", "Sample.FileImport", null, null, "C-100", "customer", older);
        SeedItem(db, "new", "g2", "r2", "Sample.ApiPolling", null, null, "C-100", "customer", newer);
        await db.SaveChangesAsync();

        var svc = Create(db, allow: true);
        var page = await svc.GetReferencesAsync(
            new SharedRecordIdentity(SharedRecordTypes.Customer, "C-100"),
            new SharedRecordReferenceQuery(Skip: 0, Take: 1));

        page.Items.Should().ContainSingle().Which.OperationItemId.Should().Be("new");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(2);
    }

    private static SharedRecordReferenceQueryService Create(AppertureDbContext db, bool allow) =>
        new(db, new StubPrincipal(), new StubAuth(allow), NullLogger<SharedRecordReferenceQueryService>.Instance);

    private static void SeedItem(
        AppertureDbContext db,
        string itemId,
        string groupId,
        string runId,
        string moduleId,
        string? integrationId,
        string? integrationLabel,
        string businessKey,
        string recordType,
        DateTimeOffset? completedAt = null)
    {
        var at = completedAt ?? DateTimeOffset.UtcNow;
        if (integrationId is not null && !db.Integrations.Local.Any(i => i.Id == integrationId)
            && !db.Integrations.Any(i => i.Id == integrationId))
        {
            db.Integrations.Add(new IntegrationEntity
            {
                Id = integrationId,
                DisplayName = integrationLabel ?? integrationId,
                ModuleId = moduleId,
                State = 1,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        if (!db.OperationGroups.Local.Any(g => g.Id == groupId) && !db.OperationGroups.Any(g => g.Id == groupId))
        {
            db.OperationGroups.Add(new OperationGroupEntity
            {
                Id = groupId,
                RunId = runId,
                IntegrationId = integrationId,
                ModuleId = moduleId,
                OperationKind = "op",
                Status = OperationGroupStatuses.Succeeded,
                StartedAtUtc = at,
                CompletedAtUtc = at,
                CreatedAtUtc = at,
                UpdatedAtUtc = at,
            });
        }

        db.OperationItems.Add(new OperationItemEntity
        {
            Id = itemId,
            OperationGroupId = groupId,
            RunId = runId,
            Sequence = 1,
            SubjectKey = businessKey,
            DisplayLabel = businessKey,
            Status = OperationItemStatuses.Succeeded,
            SharedRecordType = recordType.ToLowerInvariant(),
            SharedBusinessKey = businessKey,
            CompletedAtUtc = at,
            CreatedAtUtc = at,
            UpdatedAtUtc = at,
            MetadataJson = $"{{\"recordType\":\"{recordType.ToLowerInvariant()}\",\"businessKey\":\"{businessKey}\"}}",
        });
    }

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id { get; } = new("ops");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Ops";
    }

    private sealed class StubAuth(bool allow) : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allow);
    }
}
