using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class NotificationDispatchProcessorTests
{
    private static AppertureDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Dry_run_does_not_call_email_channel()
    {
        await using var db = CreateDb();
        var (instanceId, outboxId) = await SeedPendingOutboxAsync(db);
        var channel = new Mock<IEmailNotificationChannel>();
        var sut = CreateProcessor(db, channel.Object, deliveryEnabled: false);

        await sut.ProcessBatchAsync();

        channel.Verify(
            x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var row = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        row.Status.Should().Be(NotificationOutboxStatuses.Pending);
    }

    [Fact]
    public async Task Live_send_calls_channel_when_delivery_enabled()
    {
        await using var db = CreateDb();
        var (_, outboxId) = await SeedPendingOutboxAsync(db);
        var channel = new Mock<IEmailNotificationChannel>();
        channel
            .Setup(x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(true, null, null));
        var sut = CreateProcessor(db, channel.Object, deliveryEnabled: true);

        await sut.ProcessBatchAsync();

        channel.Verify(
            x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var row = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        row.Status.Should().Be(NotificationOutboxStatuses.Succeeded);
        row.SentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Succeeded_row_is_not_resent()
    {
        await using var db = CreateDb();
        var (_, outboxId) = await SeedPendingOutboxAsync(db);
        var channel = new Mock<IEmailNotificationChannel>();
        channel
            .Setup(x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(true, null, null));
        var sut = CreateProcessor(db, channel.Object, deliveryEnabled: true);
        await sut.ProcessBatchAsync();

        var row = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        row.Status = NotificationOutboxStatuses.Succeeded;
        await db.SaveChangesAsync();

        await sut.ProcessBatchAsync();

        channel.Verify(
            x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Failure_increments_attempt_and_schedules_next_attempt()
    {
        await using var db = CreateDb();
        var (_, outboxId) = await SeedPendingOutboxAsync(db);
        var channel = new Mock<IEmailNotificationChannel>();
        channel
            .Setup(x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(false, null, "smtp_error"));
        var sut = CreateProcessor(db, channel.Object, deliveryEnabled: true, maxAttempts: 5);

        await sut.ProcessBatchAsync();

        var row = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        row.Status.Should().Be(NotificationOutboxStatuses.Pending);
        row.AttemptCount.Should().Be(1);
        row.NextAttemptAtUtc.Should().NotBeNull();
        row.LastError.Should().Be("smtp_error");
    }

    [Fact]
    public async Task Max_attempts_marks_terminal_failed()
    {
        await using var db = CreateDb();
        var (_, outboxId) = await SeedPendingOutboxAsync(db);
        var existing = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        existing.AttemptCount = 4;
        await db.SaveChangesAsync();

        var channel = new Mock<IEmailNotificationChannel>();
        channel
            .Setup(x => x.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(false, null, "smtp_error"));
        var sut = CreateProcessor(db, channel.Object, deliveryEnabled: true, maxAttempts: 5);

        await sut.ProcessBatchAsync();

        var row = await db.NotificationDeliveryOutbox.SingleAsync(x => x.Id == outboxId);
        row.Status.Should().Be(NotificationOutboxStatuses.Failed);
        row.AttemptCount.Should().Be(5);
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    public void Backoff_is_exponential_and_capped(int attemptNumber, int expectedSeconds)
    {
        var backoff = NotificationDispatchProcessor.ComputeBackoff(
            new NotificationDeliveryOptions
            {
                InitialBackoffSeconds = 60,
                MaxBackoffSeconds = 3600,
            },
            attemptNumber);
        backoff.TotalSeconds.Should().Be(expectedSeconds);
    }

    private static NotificationDispatchProcessor CreateProcessor(
        AppertureDbContext db,
        IEmailNotificationChannel channel,
        bool deliveryEnabled,
        int maxAttempts = 5) =>
        new(
            new SqlNotificationDeliveryOutboxStore(db),
            new SqlNotificationInstanceStore(db),
            channel,
            Options.Create(new NotificationDeliveryOptions
            {
                Enabled = deliveryEnabled,
                BatchSize = 10,
                MaxAttempts = maxAttempts,
                InitialBackoffSeconds = 60,
                MaxBackoffSeconds = 3600,
            }),
            TimeProvider.System,
            NullLogger<NotificationDispatchProcessor>.Instance);

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
