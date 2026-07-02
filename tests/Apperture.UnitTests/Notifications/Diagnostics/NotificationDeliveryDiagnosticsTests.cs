using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class NotificationDeliveryDiagnosticsTests
{
    private static AppertureDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Health_summary_returns_instance_and_outbox_counts()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "n1",
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            InstanceDedupeKey = "d1",
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
            Id = "n2",
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Resolved,
            Status = NotificationInstanceStatuses.Suppressed,
            SuppressionReason = AttentionNotificationSuppressionReasons.PolicyDisabled,
            InstanceDedupeKey = "d2",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = "o1",
            NotificationInstanceId = "n1",
            RecipientEmail = "a@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Failed,
            AttemptCount = 3,
            LastError = "smtp",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationDeliveryDiagnosticsStore(db, TimeProvider.System);
        var summary = await store.GetHealthSummaryAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            50);

        summary.InstanceCountsByStatus.Should().Contain(c => c.Status == NotificationInstanceStatuses.Pending && c.Count == 1);
        summary.InstanceCountsByStatus.Should().Contain(c => c.Status == NotificationInstanceStatuses.Suppressed && c.Count == 1);
        summary.OutboxCountsByStatus.Should().Contain(c => c.Status == NotificationOutboxStatuses.Failed && c.Count == 1);
        summary.RecentSuppressedInstanceCount.Should().Be(1);
        summary.RecentFailedDeliveryCount.Should().Be(1);
    }

    [Fact]
    public async Task GetByIntegrationAsync_returns_outbox_rows_ordered_by_updated_at()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        const string integrationId = "int-notifications";
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "n1",
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            IntegrationId = integrationId,
            InstanceDedupeKey = "d1",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = "o1",
            NotificationInstanceId = "n1",
            RecipientEmail = "a@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Failed,
            AttemptCount = 1,
            LastError = "smtp",
            CreatedAtUtc = now,
            UpdatedAtUtc = now.AddMinutes(-5),
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = "o2",
            NotificationInstanceId = "n1",
            RecipientEmail = "b@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Pending,
            AttemptCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationDeliveryDiagnosticsStore(db, TimeProvider.System);
        var act = () => store.GetByIntegrationAsync(integrationId, null, 10).AsTask();

        await act.Should().NotThrowAsync();
        var diagnostics = await store.GetByIntegrationAsync(integrationId, null, 10);
        diagnostics.RecentOutboxRows.Should().HaveCount(2);
        diagnostics.RecentOutboxRows[0].Id.Should().Be("o2");
        diagnostics.RecentOutboxRows[1].Id.Should().Be("o1");
    }

    [Fact]
    public async Task Parity_query_marks_missing_platform_recipient()
    {
        var epic69 = new List<Epic69NotificationDiagnosticRow>
        {
            new(
                "l1",
                "inc-1",
                "int-1",
                AttentionNotificationKinds.Activated,
                "email",
                "old@test.com",
                "Succeeded",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "integration_override"),
        };

        var instances = new List<NotificationInstanceDiagnosticRow>
        {
            new(
                "n1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                NotificationKinds.Activation,
                NotificationInstanceStatuses.Pending,
                "int-1",
                "inc-1",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
        };

        var outbox = new List<NotificationOutboxDiagnosticRow>
        {
            new(
                "o1",
                "n1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                NotificationKinds.Activation,
                "int-1",
                "inc-1",
                "new@test.com",
                null,
                null,
                "subscription",
                NotificationChannels.Email,
                NotificationOutboxStatuses.Pending,
                0,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
        };

        var parity = ExpectedRunMissingParityComparer.Build(
            "int-1",
            "inc-1",
            epic69,
            instances,
            outbox,
            DateTimeOffset.UtcNow);

        var activation = parity.ByKind.Single(k => k.NotificationKind == NotificationKinds.Activation);
        activation.DifferenceKind.Should().NotBe(ParityDifferenceKinds.Aligned);
        activation.RecipientComparisons.Should().Contain(
            c => c.RecipientEmail == "old@test.com" && c.DifferenceKind == ParityDifferenceKinds.MissingInPlatform);
        activation.RecipientComparisons.Should().Contain(
            c => c.RecipientEmail == "new@test.com" && c.DifferenceKind == ParityDifferenceKinds.ExtraInPlatform);
    }

    [Fact]
    public async Task Duplicate_claim_only_one_processor_sends()
    {
        await using var db = CreateDb();
        var (_, outboxId) = await SeedPendingOutboxAsync(db);
        var store = new SqlNotificationDeliveryOutboxStore(db);

        var claim1 = await store.TryMarkDeliveringAsync(NotificationDeliveryOutboxId.From(outboxId), DateTimeOffset.UtcNow);
        var claim2 = await store.TryMarkDeliveringAsync(NotificationDeliveryOutboxId.From(outboxId), DateTimeOffset.UtcNow);

        claim1.Should().BeTrue();
        claim2.Should().BeFalse();
        (await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId)).Status
            .Should().Be(NotificationOutboxStatuses.Delivering);
    }

    private static async Task<(string instanceId, string outboxId)> SeedPendingOutboxAsync(AppertureDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var instanceId = NotificationInstanceId.New().Value;
        var outboxId = NotificationDeliveryOutboxId.New().Value;
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = instanceId,
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            TemplateVersionHash = "hash",
            RenderContextJson = "{}",
            InstanceDedupeKey = "dedupe",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = outboxId,
            NotificationInstanceId = instanceId,
            RecipientEmail = "user@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Pending,
            AttemptCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
        return (instanceId, outboxId);
    }
}
