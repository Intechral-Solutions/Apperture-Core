using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class SqlNotificationInstanceStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static NotificationInstanceCreateCommand SampleCommand(string dedupeKey) =>
        new(
            "def-1",
            NotificationKinds.Activation,
            NotificationInstanceStatuses.Pending,
            "pevt-1",
            "inc-1",
            "int-1",
            null,
            null,
            null,
            dedupeKey,
            "Subject",
            "Summary",
            "Body",
            null,
            null,
            null,
            null,
            "hash",
            "{}",
            null,
            null);

    [Fact]
    public async Task TryCreate_is_idempotent_on_dedupe_key()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationInstanceStore(db);

        var first = await sut.TryCreateInstanceAsync(SampleCommand("dedupe-1"));
        var second = await sut.TryCreateInstanceAsync(SampleCommand("dedupe-1"));

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        second.Instance.Id.Should().Be(first.Instance.Id);
    }

    [Fact]
    public async Task GetLatestActivationOrReminder_returns_most_recent()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationInstanceStore(db);
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst_1",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            AttentionIncidentId = "inc-1",
            InstanceDedupeKey = "k1",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = older,
            UpdatedAtUtc = older,
        });
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst_2",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Reminder,
            Status = NotificationInstanceStatuses.Pending,
            AttentionIncidentId = "inc-1",
            InstanceDedupeKey = "k2",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = newer,
            UpdatedAtUtc = newer,
        });
        await db.SaveChangesAsync();

        var latest = await sut.GetLatestActivationOrReminderCreatedUtcAsync("inc-1", "def-1");
        latest.Should().Be(newer);
    }

    [Fact]
    public async Task ListAsync_FiltersByResourceContextId_WhenSupplied()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationInstanceStore(db);
        var now = DateTimeOffset.UtcNow;

        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst_a",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            IntegrationId = "int-1",
            AttentionIncidentId = "inc-1",
            ResourceContextId = "app-a",
            InstanceDedupeKey = "k-a",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst_b",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            IntegrationId = "int-1",
            AttentionIncidentId = "inc-1",
            ResourceContextId = "app-b",
            InstanceDedupeKey = "k-b",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var results = await sut.ListAsync(new NotificationInstanceListQuery(ResourceContextId: "app-a"));

        results.Should().ContainSingle(r => r.ResourceContextId == "app-a");
    }
}
