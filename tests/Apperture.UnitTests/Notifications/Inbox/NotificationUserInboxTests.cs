using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Inbox;

public sealed class NotificationUserInboxTests
{
    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public void ExtractPrincipalRecipients_includes_principal_and_team_member_with_userId()
    {
        var json = """
            {
              "deliverable": [
                { "email": "a@x.com", "userId": "user-a", "recipientKind": "principal", "source": "directSubscription" },
                { "email": "b@x.com", "userId": "user-b", "recipientKind": "operational_team", "subscriptionId": "sub-1", "source": "operationalTeamMembership" }
              ],
              "skipped": [
                { "email": "ext@x.com", "recipientKind": "operational_team", "reason": "missingEmail" }
              ]
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);
        recipients.Should().HaveCount(2);
        recipients.Select(r => r.UserId).Should().BeEquivalentTo(["user-a", "user-b"]);
    }

    [Fact]
    public void ExtractPrincipalRecipients_includes_managed_contact_with_linked_user()
    {
        var json = """
            {
              "deliverable": [
                { "email": "c@x.com", "userId": "user-c", "managedContactId": "mc-1", "recipientKind": "managed_contact", "source": "directSubscription" }
              ],
              "skipped": []
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);
        recipients.Should().ContainSingle().Which.UserId.Should().Be("user-c");
    }

    [Fact]
    public async Task MaterializeForInstance_creates_one_row_per_user_deduped()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst-1",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            InstanceDedupeKey = "dedupe",
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            RecipientSnapshotJson = """
                {
                  "deliverable": [
                    { "email": "a@x.com", "userId": "user-a", "recipientKind": "principal", "source": "directSubscription" },
                    { "email": "a@x.com", "userId": "user-a", "recipientKind": "operational_team", "subscriptionId": "sub-1", "source": "operationalTeamMembership" }
                  ],
                  "skipped": []
                }
                """,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        await sut.MaterializeForInstanceAsync(
            "ninst-1",
            db.NotificationInstances.Single().RecipientSnapshotJson,
            [],
            CancellationToken.None);

        var rows = await db.NotificationUserInboxItems.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].PrincipalUserId.Should().Be("user-a");
        rows[0].NotificationInstanceId.Should().Be("ninst-1");
        rows[0].IsRead.Should().BeFalse();
        rows[0].ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Unread_count_and_mark_read_are_scoped_to_user()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        db.NotificationUserInboxItems.AddRange(
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-1",
                NotificationInstanceId = "ninst-1",
                PrincipalUserId = "user-a",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-a",
                IsRead = false,
                CreatedAtUtc = now,
            },
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-2",
                NotificationInstanceId = "ninst-2",
                PrincipalUserId = "user-b",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-b",
                IsRead = false,
                CreatedAtUtc = now,
            });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        (await sut.GetUnreadCountAsync("user-a", CancellationToken.None)).Should().Be(1);
        (await sut.MarkReadAsync("user-a", "inbox-1", CancellationToken.None)).Should().BeTrue();
        (await sut.GetUnreadCountAsync("user-a", CancellationToken.None)).Should().Be(0);
        (await sut.GetUnreadCountAsync("user-b", CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Mark_all_read_only_affects_current_user()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        db.NotificationUserInboxItems.AddRange(
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-1",
                NotificationInstanceId = "ninst-1",
                PrincipalUserId = "user-a",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-a",
                IsRead = false,
                CreatedAtUtc = now,
            },
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-2",
                NotificationInstanceId = "ninst-2",
                PrincipalUserId = "user-a",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-a",
                IsRead = false,
                CreatedAtUtc = now,
            },
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-3",
                NotificationInstanceId = "ninst-3",
                PrincipalUserId = "user-b",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-b",
                IsRead = false,
                CreatedAtUtc = now,
            });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        var marked = await sut.MarkAllReadAsync("user-a", CancellationToken.None);
        marked.Should().Be(2);

        (await db.NotificationUserInboxItems.Where(x => x.PrincipalUserId == "user-a").ToListAsync())
            .Should().OnlyContain(x => x.ReadAtUtc != null);
        (await db.NotificationUserInboxItems.SingleAsync(x => x.PrincipalUserId == "user-b")).ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task List_filters_read_and_unread()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        SeedInstance(db, "ninst-1", now);
        SeedInstance(db, "ninst-2", now);
        db.NotificationUserInboxItems.AddRange(
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-1",
                NotificationInstanceId = "ninst-1",
                PrincipalUserId = "user-a",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-a",
                IsRead = false,
                CreatedAtUtc = now,
            },
            new NotificationUserInboxItemEntity
            {
                Id = "inbox-2",
                NotificationInstanceId = "ninst-2",
                PrincipalUserId = "user-a",
                SourceRecipientKind = NotificationRecipientKinds.Principal,
                SourceRecipientRef = "user-a",
                IsRead = true,
                ReadAtUtc = now,
                CreatedAtUtc = now,
            });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        var unread = await sut.ListForUserAsync("user-a", new PersonalNotificationListQuery(PersonalNotificationReadFilter.Unread), CancellationToken.None);
        unread.Should().ContainSingle().Which.NotificationInstanceId.Should().Be("ninst-1");

        var read = await sut.ListForUserAsync("user-a", new PersonalNotificationListQuery(PersonalNotificationReadFilter.Read), CancellationToken.None);
        read.Should().ContainSingle().Which.NotificationInstanceId.Should().Be("ninst-2");
    }

    [Fact]
    public async Task RecordMenuViewed_sets_last_viewed_without_marking_read()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        SeedInstance(db, "ninst-1", now);
        db.NotificationUserInboxItems.Add(new NotificationUserInboxItemEntity
        {
            Id = "inbox-1",
            NotificationInstanceId = "ninst-1",
            PrincipalUserId = "user-a",
            SourceRecipientKind = NotificationRecipientKinds.Principal,
            SourceRecipientRef = "user-a",
            IsRead = false,
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        await sut.RecordMenuViewedAsync("user-a", take: 10, CancellationToken.None);

        var row = await db.NotificationUserInboxItems.SingleAsync();
        row.ReadAtUtc.Should().BeNull();
        row.LastViewedAtUtc.Should().Be(now);
        row.FirstViewedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task ListForUser_includes_rendered_action_url_from_instance()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-05-18T12:00:00Z");
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst-action",
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            InstanceDedupeKey = "dedupe",
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            RenderedActionUrl = "/integrations/int-1/schedule-diagnostics",
            RenderedActionText = "Review schedule diagnostics",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationUserInboxItems.Add(new NotificationUserInboxItemEntity
        {
            Id = "inbox-action",
            NotificationInstanceId = "ninst-action",
            PrincipalUserId = "user-a",
            SourceRecipientKind = NotificationRecipientKinds.Principal,
            SourceRecipientRef = "user-a",
            IsRead = false,
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var clock = new FixedUtcTimeProvider(now);
        var sut = new NotificationUserInboxService(store, db, clock, NullLogger<NotificationUserInboxService>.Instance);

        var items = await sut.ListForUserAsync("user-a", new PersonalNotificationListQuery(), CancellationToken.None);
        items.Should().ContainSingle();
        items[0].RenderedActionUrl.Should().Be("/integrations/int-1/schedule-diagnostics");
        items[0].RenderedActionText.Should().Be("Review schedule diagnostics");
    }

    private static void SeedInstance(AppertureDbContext db, string id, DateTimeOffset now)
    {
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = id,
            NotificationDefinitionId = "def-1",
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            InstanceDedupeKey = id,
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }
}
