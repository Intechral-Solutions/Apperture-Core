using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Inbox;

/// <summary>
/// Covers <see cref="SqlNotificationUserInboxStore.ListToastDeltaAsync"/>: composite-cursor
/// pagination, bootstrap-to-tip, and the dual LEFT JOIN (definition, per-user Toast preference)
/// used to compute effective Toast eligibility.
/// </summary>
public sealed class SqlNotificationUserInboxStoreToastDeltaTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static NotificationInstanceEntity NewInstance(
        string id,
        DateTimeOffset createdAtUtc,
        string? definitionId = "def-1",
        string? integrationId = null) => new()
    {
        Id = id,
        NotificationDefinitionId = definitionId ?? "def-missing",
        NotificationKind = NotificationKinds.Activation,
        Status = NotificationInstanceStatuses.Pending,
        IntegrationId = integrationId,
        InstanceDedupeKey = id,
        RenderedSubject = "Subject " + id,
        RenderedSummary = "Summary " + id,
        RenderedBodyText = "Body",
        TemplateVersionHash = "h",
        RenderContextJson = "{}",
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = createdAtUtc,
    };

    private static NotificationUserInboxItemEntity NewInbox(
        string id,
        string instanceId,
        string principalUserId,
        DateTimeOffset createdAtUtc,
        bool isRead = false,
        DateTimeOffset? readAtUtc = null,
        DateTimeOffset? firstViewedAtUtc = null,
        DateTimeOffset? lastViewedAtUtc = null) => new()
    {
        Id = id,
        NotificationInstanceId = instanceId,
        PrincipalUserId = principalUserId,
        SourceRecipientKind = NotificationRecipientKinds.Principal,
        SourceRecipientRef = principalUserId,
        IsRead = isRead,
        ReadAtUtc = readAtUtc,
        FirstViewedAtUtc = firstViewedAtUtc,
        LastViewedAtUtc = lastViewedAtUtc,
        CreatedAtUtc = createdAtUtc,
    };

    private static NotificationDefinitionEntity NewDefinition(
        string id,
        bool toastEnabledByDefault,
        string? defaultSeverity = "info") => new()
    {
        Id = id,
        DisplayName = "Definition " + id,
        SourceKind = "system",
        Origin = "builtin",
        Scope = "instance",
        Mutability = "immutable",
        IsEnabled = true,
        DefaultSeverity = defaultSeverity,
        RuleJson = "{}",
        TemplateJson = "{}",
        CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        ToastEnabledByDefault = toastEnabledByDefault,
    };

    private static NotificationUserPreferenceEntity NewPreference(
        string id,
        string userId,
        string definitionId,
        bool isEnabled,
        string channel = "toast") => new()
    {
        Id = id,
        UserId = userId,
        NotificationDefinitionId = definitionId,
        Channel = channel,
        IsEnabled = isEnabled,
        CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
    };

    [Fact]
    public async Task Composite_cursor_excludes_exact_row_and_ties_break_on_id()
    {
        await using var db = CreateDb();
        var t0 = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-06-01T11:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-06-01T12:00:00Z");

        db.NotificationDefinitions.Add(NewDefinition("def-1", toastEnabledByDefault: true));
        db.NotificationInstances.AddRange(
            NewInstance("ninst-a", t0),
            NewInstance("ninst-b1", t1),
            NewInstance("ninst-b2", t1),
            NewInstance("ninst-d", t2));
        db.NotificationUserInboxItems.AddRange(
            NewInbox("inbox-a", "ninst-a", "user-a", t0),
            NewInbox("inbox-b1", "ninst-b1", "user-a", t1),
            NewInbox("inbox-b2", "ninst-b2", "user-a", t1), // same CreatedAtUtc as b1, greater Id
            NewInbox("inbox-d", "ninst-d", "user-a", t2));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);

        // Cursor points exactly at inbox-b1 (t1, "inbox-b1").
        var rows = await store.ListToastDeltaAsync("user-a", t1, "inbox-b1", take: 10, CancellationToken.None);

        rows.Select(r => r.Id).Should().Equal("inbox-b2", "inbox-d");
    }

    [Fact]
    public async Task No_cursor_bootstrap_returns_tip_not_full_history()
    {
        await using var db = CreateDb();
        db.NotificationDefinitions.Add(NewDefinition("def-1", toastEnabledByDefault: true));

        var baseTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        for (var i = 0; i < 5; i++)
        {
            var t = baseTime.AddDays(i);
            db.NotificationInstances.Add(NewInstance($"ninst-{i}", t));
            db.NotificationUserInboxItems.Add(NewInbox($"inbox-{i}", $"ninst-{i}", "user-a", t));
        }
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);

        var rows = await store.ListToastDeltaAsync("user-a", afterCreatedAtUtc: null, afterId: null, take: 2, CancellationToken.None);

        // Most recent two (inbox-3, inbox-4), returned in ascending order.
        rows.Select(r => r.Id).Should().Equal("inbox-3", "inbox-4");
    }

    [Fact]
    public async Task Deleted_definition_yields_null_severity_and_ineligible_without_throwing()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        // Intentionally do NOT seed a NotificationDefinitions row for "def-deleted".
        db.NotificationInstances.Add(NewInstance("ninst-1", now, definitionId: "def-deleted"));
        db.NotificationUserInboxItems.Add(NewInbox("inbox-1", "ninst-1", "user-a", now));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);

        var act = async () => await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);
        var rows = await act.Should().NotThrowAsync();

        rows.Subject.Should().ContainSingle();
        rows.Subject[0].DefaultSeverity.Should().BeNull();
        rows.Subject[0].IsToastEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_preference_overrides_definition_default_both_directions()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");

        db.NotificationDefinitions.AddRange(
            NewDefinition("def-on-by-default", toastEnabledByDefault: true),
            NewDefinition("def-off-by-default", toastEnabledByDefault: false));
        db.NotificationUserPreferences.AddRange(
            NewPreference("pref-1", "user-a", "def-on-by-default", isEnabled: false),
            NewPreference("pref-2", "user-a", "def-off-by-default", isEnabled: true));
        db.NotificationInstances.AddRange(
            NewInstance("ninst-on", now, definitionId: "def-on-by-default"),
            NewInstance("ninst-off", now.AddMinutes(1), definitionId: "def-off-by-default"));
        db.NotificationUserInboxItems.AddRange(
            NewInbox("inbox-on", "ninst-on", "user-a", now),
            NewInbox("inbox-off", "ninst-off", "user-a", now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var rows = await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);

        rows.Single(r => r.Id == "inbox-on").IsToastEligible.Should().BeFalse();
        rows.Single(r => r.Id == "inbox-off").IsToastEligible.Should().BeTrue();
    }

    [Fact]
    public async Task No_preference_row_inherits_definition_default()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");

        db.NotificationDefinitions.AddRange(
            NewDefinition("def-true", toastEnabledByDefault: true),
            NewDefinition("def-false", toastEnabledByDefault: false));
        db.NotificationInstances.AddRange(
            NewInstance("ninst-true", now, definitionId: "def-true"),
            NewInstance("ninst-false", now.AddMinutes(1), definitionId: "def-false"));
        db.NotificationUserInboxItems.AddRange(
            NewInbox("inbox-true", "ninst-true", "user-a", now),
            NewInbox("inbox-false", "ninst-false", "user-a", now.AddMinutes(1)));
        await db.SaveChangesAsync();
        // Deliberately no NotificationUserPreferences rows at all.

        var store = new SqlNotificationUserInboxStore(db);
        var rows = await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);

        rows.Single(r => r.Id == "inbox-true").IsToastEligible.Should().BeTrue();
        rows.Single(r => r.Id == "inbox-false").IsToastEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Take_is_bounded_to_fifty_even_when_more_rows_match()
    {
        await using var db = CreateDb();
        db.NotificationDefinitions.Add(NewDefinition("def-1", toastEnabledByDefault: true));

        var baseTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        for (var i = 0; i < 60; i++)
        {
            var t = baseTime.AddMinutes(i);
            db.NotificationInstances.Add(NewInstance($"ninst-{i:D3}", t));
            db.NotificationUserInboxItems.Add(NewInbox($"inbox-{i:D3}", $"ninst-{i:D3}", "user-a", t));
        }
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);

        var rows = await store.ListToastDeltaAsync("user-a", null, null, take: 10_000, CancellationToken.None);

        rows.Count.Should().Be(50);
    }

    [Fact]
    public async Task Returns_both_toastable_and_non_toastable_rows_for_cursor_advancement()
    {
        await using var db = CreateDb();
        var t0 = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);
        var t3 = t0.AddMinutes(3);

        db.NotificationDefinitions.AddRange(
            NewDefinition("def-eligible", toastEnabledByDefault: true),
            NewDefinition("def-ineligible", toastEnabledByDefault: false));
        db.NotificationInstances.AddRange(
            NewInstance("ninst-1", t0, definitionId: "def-eligible"),
            NewInstance("ninst-2", t1, definitionId: "def-ineligible"),
            NewInstance("ninst-3", t2, definitionId: "def-eligible"),
            NewInstance("ninst-4", t3, definitionId: "def-ineligible"));
        db.NotificationUserInboxItems.AddRange(
            NewInbox("inbox-1", "ninst-1", "user-a", t0),
            NewInbox("inbox-2", "ninst-2", "user-a", t1),
            NewInbox("inbox-3", "ninst-3", "user-a", t2),
            NewInbox("inbox-4", "ninst-4", "user-a", t3));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        var rows = await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);

        rows.Should().HaveCount(4);
        rows.Select(r => r.Id).Should().Equal("inbox-1", "inbox-2", "inbox-3", "inbox-4");
        rows.Count(r => r.IsToastEligible).Should().Be(2);
        rows.Count(r => !r.IsToastEligible).Should().Be(2);
    }

    [Fact]
    public async Task Only_scans_requested_users_rows()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");

        db.NotificationDefinitions.Add(NewDefinition("def-1", toastEnabledByDefault: true));
        db.NotificationInstances.AddRange(
            NewInstance("ninst-a", now),
            NewInstance("ninst-b", now.AddMinutes(1)));
        db.NotificationUserInboxItems.AddRange(
            NewInbox("inbox-a", "ninst-a", "user-a", now),
            NewInbox("inbox-b", "ninst-b", "user-b", now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);

        var rowsForA = await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);
        rowsForA.Should().ContainSingle().Which.Id.Should().Be("inbox-a");

        var rowsForB = await store.ListToastDeltaAsync("user-b", null, null, take: 10, CancellationToken.None);
        rowsForB.Should().ContainSingle().Which.Id.Should().Be("inbox-b");
    }

    [Fact]
    public async Task Does_not_mutate_read_or_viewed_state()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var readAt = now.AddMinutes(-30);
        var firstViewedAt = now.AddMinutes(-20);
        var lastViewedAt = now.AddMinutes(-10);

        db.NotificationDefinitions.Add(NewDefinition("def-1", toastEnabledByDefault: true));
        db.NotificationInstances.Add(NewInstance("ninst-1", now));
        db.NotificationUserInboxItems.Add(NewInbox(
            "inbox-1",
            "ninst-1",
            "user-a",
            now,
            isRead: true,
            readAtUtc: readAt,
            firstViewedAtUtc: firstViewedAt,
            lastViewedAtUtc: lastViewedAt));
        await db.SaveChangesAsync();

        var store = new SqlNotificationUserInboxStore(db);
        await store.ListToastDeltaAsync("user-a", null, null, take: 10, CancellationToken.None);

        var row = await db.NotificationUserInboxItems.AsNoTracking().SingleAsync(x => x.Id == "inbox-1");
        row.IsRead.Should().BeTrue();
        row.ReadAtUtc.Should().Be(readAt);
        row.FirstViewedAtUtc.Should().Be(firstViewedAt);
        row.LastViewedAtUtc.Should().Be(lastViewedAt);
    }
}
