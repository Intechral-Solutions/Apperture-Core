using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// End-to-end coverage of <c>GET api/me/notifications/toast-delta</c> (Toast-B P4) over real HTTP
/// against the Management Host: authentication, composite-cursor pagination, toast-eligibility
/// filtering, deleted-definition handling, cross-user isolation, and read-state immutability.
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostNotificationToastDeltaHttpTests
{
    private const string Route = "/api/me/notifications/toast-delta";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostNotificationToastDeltaHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    // --- 1. Authentication ---

    [Fact]
    public async Task Toast_delta_requires_authentication()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- 2. Fresh user, no cursor ---

    [Fact]
    public async Task Fresh_user_with_inbox_items_returns_the_tip_respects_the_bound_and_includes_unread_count()
    {
        AssertSqlAvailable();
        var (client, userId) = await CreateUserClientAsync();
        var defId = await SeedDefinitionAsync(toastEnabledByDefault: true);

        // Seed 55 toast-eligible items so the 50-row bound is actually exercised.
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-120);
        for (var i = 0; i < 55; i++)
            await SeedInboxItemAsync(userId, defId, baseTime.AddSeconds(i));

        var result = await GetToastDeltaAsync(client, null, null);

        result.Items.Should().HaveCount(50, "the store bounds the scan to 50 rows regardless of how many are eligible");
        result.UnreadCount.Should().Be(55, "all seeded items are unread and CountUnreadAsync counts the whole inbox, not just the scanned window");
        result.NextAfterCreatedAtUtc.Should().NotBeNull();
        result.NextAfterId.Should().NotBeNullOrEmpty();
    }

    // --- 3. Cursor round trip ---

    [Fact]
    public async Task Cursor_round_trip_returns_no_duplicates_and_does_not_regress_when_nothing_new_landed()
    {
        AssertSqlAvailable();
        var (client, userId) = await CreateUserClientAsync();
        var defId = await SeedDefinitionAsync(toastEnabledByDefault: true);

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-60);
        await SeedInboxItemAsync(userId, defId, t0);
        await SeedInboxItemAsync(userId, defId, t0.AddSeconds(1));
        await SeedInboxItemAsync(userId, defId, t0.AddSeconds(2));

        var first = await GetToastDeltaAsync(client, null, null);
        first.Items.Should().HaveCount(3);
        first.NextAfterCreatedAtUtc.Should().NotBeNull();
        first.NextAfterId.Should().NotBeNullOrEmpty();

        var second = await GetToastDeltaAsync(client, first.NextAfterCreatedAtUtc, first.NextAfterId);

        second.Items.Should().BeEmpty("nothing new has landed since the first poll");
        second.NextAfterCreatedAtUtc.Should().Be(first.NextAfterCreatedAtUtc, "the cursor must not regress when no rows are scanned");
        second.NextAfterId.Should().Be(first.NextAfterId);

        // No duplicates across the two polls' item sets.
        var firstIds = first.Items.Select(i => i.Id).ToHashSet();
        var secondIds = second.Items.Select(i => i.Id).ToHashSet();
        firstIds.Intersect(secondIds).Should().BeEmpty();
    }

    // --- 4. Non-toast-eligible item excluded but cursor still advances past it ---

    [Fact]
    public async Task Non_toast_eligible_item_is_excluded_from_items_but_the_cursor_advances_past_it()
    {
        AssertSqlAvailable();
        var (client, userId) = await CreateUserClientAsync();
        var ineligibleDefId = await SeedDefinitionAsync(toastEnabledByDefault: false);

        var t1 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var ineligibleItemId = await SeedInboxItemAsync(userId, ineligibleDefId, t1);

        var firstPoll = await GetToastDeltaAsync(client, null, null);

        firstPoll.Items.Should().NotContain(i => i.Id == ineligibleItemId, "the item's definition has ToastEnabledByDefault=false and no override");
        firstPoll.NextAfterId.Should().Be(ineligibleItemId, "the cursor must advance past a scanned row even when it is not toast-eligible");
        firstPoll.NextAfterCreatedAtUtc.Should().Be(t1);

        // Seed a second, later, toast-eligible item and poll again with the cursor from the first poll.
        var eligibleDefId = await SeedDefinitionAsync(toastEnabledByDefault: true);
        var t2 = t1.AddSeconds(5);
        var eligibleItemId = await SeedInboxItemAsync(userId, eligibleDefId, t2);

        var secondPoll = await GetToastDeltaAsync(client, firstPoll.NextAfterCreatedAtUtc, firstPoll.NextAfterId);

        secondPoll.Items.Should().ContainSingle(i => i.Id == eligibleItemId,
            "the cursor was not 'stuck' behind the skipped, non-eligible row");
        secondPoll.NextAfterCreatedAtUtc.Should().BeOnOrAfter(firstPoll.NextAfterCreatedAtUtc!.Value);
    }

    // --- 5. Deleted definition ---

    [Fact]
    public async Task Deleted_definition_does_not_crash_the_endpoint_and_produces_no_toast()
    {
        AssertSqlAvailable();
        var (client, userId) = await CreateUserClientAsync();

        // Intentionally do not seed a NotificationDefinitions row for this id: simulates a definition
        // that has since been deleted after the instance/inbox item were created.
        var deletedDefId = $"def-deleted-{Guid.NewGuid():N}"[..24];
        var t = DateTimeOffset.UtcNow.AddMinutes(-15);
        var orphanItemId = await SeedInboxItemAsync(userId, deletedDefId, t);

        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "a missing definition must not crash the endpoint");

        var result = await response.Content.ReadFromJsonAsync<NotificationToastDeltaResponseDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.Items.Should().NotContain(i => i.Id == orphanItemId,
            "a row whose definition no longer exists is never toast-eligible (store: DefaultSeverity is null => IsToastEligible is false)");
        result.NextAfterId.Should().Be(orphanItemId, "the cursor still advances past the orphaned row");
    }

    // --- 6. No target-user parameter; cross-user isolation ---

    [Fact]
    public void ToastDelta_action_signature_accepts_no_parameter_that_could_identify_a_different_user()
    {
        // Code-review-style contract assertion: the only query parameters the action accepts are the
        // cursor pair, plus the standard cancellation token. There is no userId/targetUserId/principalId
        // route, query, or body parameter anywhere on this action -- the acting user comes exclusively
        // from ResolveUserIdAsync() reading the authenticated principal.
        var method = typeof(MeNotificationsController).GetMethod(nameof(MeNotificationsController.ToastDelta));

        method.Should().NotBeNull();
        method!.GetParameters().Select(p => p.Name).Should().BeEquivalentTo(
            new[] { "afterCreatedAtUtc", "afterId", "cancellationToken" },
            "the endpoint must have no way to specify a different user's id");
    }

    [Fact]
    public async Task One_users_toast_delta_never_includes_another_users_inbox_items()
    {
        AssertSqlAvailable();
        var (clientA, userIdA) = await CreateUserClientAsync();
        var (clientB, userIdB) = await CreateUserClientAsync();
        var defId = await SeedDefinitionAsync(toastEnabledByDefault: true);

        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var itemAId = await SeedInboxItemAsync(userIdA, defId, now);
        var itemBId = await SeedInboxItemAsync(userIdB, defId, now.AddSeconds(1));

        var resultA = await GetToastDeltaAsync(clientA, null, null);
        resultA.Items.Should().ContainSingle(i => i.Id == itemAId);
        resultA.Items.Should().NotContain(i => i.Id == itemBId, "user A must never see user B's inbox items");

        var resultB = await GetToastDeltaAsync(clientB, null, null);
        resultB.Items.Should().ContainSingle(i => i.Id == itemBId);
        resultB.Items.Should().NotContain(i => i.Id == itemAId, "user B must never see user A's inbox items");
    }

    // --- 7. Read-state untouched ---

    [Fact]
    public async Task Calling_the_endpoint_does_not_mutate_read_or_viewed_state()
    {
        AssertSqlAvailable();
        var (client, userId) = await CreateUserClientAsync();
        var defId = await SeedDefinitionAsync(toastEnabledByDefault: true);

        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var readAt = now.AddMinutes(-30);
        var firstViewedAt = now.AddMinutes(-20);
        var lastViewedAt = now.AddMinutes(-10);
        var itemId = await SeedInboxItemAsync(
            userId,
            defId,
            now,
            isRead: true,
            readAtUtc: readAt,
            firstViewedAtUtc: firstViewedAt,
            lastViewedAtUtc: lastViewedAt);

        await using (var beforeScope = _fixture.Factory!.Services.CreateAsyncScope())
        {
            var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var before = await beforeDb.NotificationUserInboxItems.AsNoTracking().SingleAsync(x => x.Id == itemId);
            before.IsRead.Should().BeTrue();
            before.ReadAtUtc.Should().Be(readAt);
            before.FirstViewedAtUtc.Should().Be(firstViewedAt);
            before.LastViewedAtUtc.Should().Be(lastViewedAt);
        }

        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var afterScope = _fixture.Factory!.Services.CreateAsyncScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var after = await afterDb.NotificationUserInboxItems.AsNoTracking().SingleAsync(x => x.Id == itemId);
        after.IsRead.Should().BeTrue();
        after.ReadAtUtc.Should().Be(readAt);
        after.FirstViewedAtUtc.Should().Be(firstViewedAt);
        after.LastViewedAtUtc.Should().Be(lastViewedAt);
    }

    // --- Helpers ---

    private async Task<NotificationToastDeltaResponseDto> GetToastDeltaAsync(
        HttpClient client,
        DateTimeOffset? afterCreatedAtUtc,
        string? afterId)
    {
        var query = new List<string>();
        if (afterCreatedAtUtc is not null)
            query.Add($"afterCreatedAtUtc={Uri.EscapeDataString(afterCreatedAtUtc.Value.ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(afterId))
            query.Add($"afterId={Uri.EscapeDataString(afterId)}");
        var path = query.Count > 0 ? $"{Route}?{string.Join("&", query)}" : Route;

        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<NotificationToastDeltaResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        return dto!;
    }

    /// <summary>
    /// Creates a brand-new, isolated linked user (bypassing the shared "first admin" bootstrap) so
    /// each test's seeded inbox data cannot leak into or be polluted by another test sharing this
    /// collection's fixture.
    /// </summary>
    private async Task<(HttpClient Client, string UserId)> CreateUserClientAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var userId = $"usr_{Guid.NewGuid():N}"[..24];
        var externalId = $"toast-delta-{Guid.NewGuid():N}"[..24];

        db.Users.Add(new UserEntity { Id = userId, DisplayName = "Toast Delta Test User", IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            UserId = userId,
            Source = (int)IdentitySource.Local,
            ExternalId = externalId,
            LastAuthenticatedAt = null,
        });
        await db.SaveChangesAsync();

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        return (client, userId);
    }

    private async Task<string> SeedDefinitionAsync(bool toastEnabledByDefault, string? defaultSeverity = "info")
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var id = $"def-toastdelta-{Guid.NewGuid():N}"[..32];
        var now = DateTimeOffset.UtcNow;
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = id,
            DisplayName = "Toast Delta Test Definition",
            SourceKind = "system",
            Origin = "builtin",
            Scope = "instance",
            Mutability = "immutable",
            IsEnabled = true,
            DefaultSeverity = defaultSeverity,
            RuleJson = "{}",
            TemplateJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ToastEnabledByDefault = toastEnabledByDefault,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<string> SeedInboxItemAsync(
        string userId,
        string definitionId,
        DateTimeOffset createdAtUtc,
        bool isRead = false,
        DateTimeOffset? readAtUtc = null,
        DateTimeOffset? firstViewedAtUtc = null,
        DateTimeOffset? lastViewedAtUtc = null)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var instanceId = $"ninst-toastdelta-{Guid.NewGuid():N}"[..32];
        var inboxId = $"inbox-toastdelta-{Guid.NewGuid():N}"[..32];

        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = instanceId,
            NotificationDefinitionId = definitionId,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            InstanceDedupeKey = instanceId,
            RenderedSubject = "Subject " + instanceId,
            RenderedSummary = "Summary " + instanceId,
            RenderedBodyText = "Body",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        });
        db.NotificationUserInboxItems.Add(new NotificationUserInboxItemEntity
        {
            Id = inboxId,
            NotificationInstanceId = instanceId,
            PrincipalUserId = userId,
            SourceRecipientKind = NotificationRecipientKinds.Principal,
            SourceRecipientRef = userId,
            IsRead = isRead,
            ReadAtUtc = readAtUtc,
            FirstViewedAtUtc = firstViewedAtUtc,
            LastViewedAtUtc = lastViewedAtUtc,
            CreatedAtUtc = createdAtUtc,
        });
        await db.SaveChangesAsync();
        return inboxId;
    }
}
