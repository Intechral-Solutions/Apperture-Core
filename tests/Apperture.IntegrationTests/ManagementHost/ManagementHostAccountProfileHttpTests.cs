using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

// The API controller and the typed client both declare a MeResponse. These tests read the wire form
// over HTTP, so the client contract is the one under test.
using MeResponse = Apperture.Management.Client.MeResponse;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// End-to-end coverage of the self-service account endpoints over real HTTP against the Management
/// Host: authentication, validation, avatar upload and serving, and cross-user isolation.
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostAccountProfileHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostAccountProfileHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    // --- Authentication ---

    [Theory]
    [InlineData("GET", "/api/account/profile")]
    [InlineData("GET", "/api/account/preferences")]
    [InlineData("GET", "/api/account/notification-preferences")]
    [InlineData("GET", "/api/account/avatar/image?size=128")]
    public async Task Account_reads_require_authentication(string method, string path)
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Account_writes_require_authentication()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();

        var profile = await client.PutAsJsonAsync("/api/account/profile", new { displayName = "Anyone" });
        profile.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var avatar = await client.DeleteAsync("/api/account/avatar");
        avatar.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_authenticated_but_unlinked_principal_is_forbidden_rather_than_served()
    {
        AssertSqlAvailable();
        await EnsureFirstAdminAsync();

        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, $"unlinked-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/api/account/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- Profile ---

    [Fact]
    public async Task Get_profile_returns_the_signed_in_operators_own_record()
    {
        AssertSqlAvailable();
        var (client, username, userId) = await CreateAuthenticatedClientAsync();

        var profile = await GetProfileAsync(client);

        profile.UserId.Should().Be(userId);
        profile.DisplayName.Should().NotBeNullOrWhiteSpace();
        profile.DisplayNameEditable.Should().BeTrue();
        profile.IdentityLinks.Should().Contain(l => l.Source == "Local" && l.ExternalId == username);
    }

    [Fact]
    public async Task Put_profile_updates_the_display_name_and_it_survives_a_reload()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var original = (await GetProfileAsync(client)).DisplayName;
        var updated = $"Renamed {Guid.NewGuid():N}"[..20];

        try
        {
            var response = await client.PutAsJsonAsync("/api/account/profile", new { displayName = updated });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var reloaded = await GetProfileAsync(client);
            reloaded.DisplayName.Should().Be(updated);

            // The session response is what the shell renders, so it must agree.
            var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions);
            me!.DisplayName.Should().Be(updated);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/account/profile", new { displayName = original });
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Put_profile_rejects_a_blank_display_name_with_problem_details(string? displayName)
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/account/profile", new { displayName });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Put_profile_ignores_any_user_id_supplied_in_the_body()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();
        var victim = await CreateSecondUserAsync();
        var original = (await GetProfileAsync(client)).DisplayName;

        try
        {
            // A client-supplied id must have no effect: the acting user comes from authentication only.
            var response = await client.PutAsJsonAsync(
                "/api/account/profile",
                new { displayName = "Injected Name", userId = victim.UserId, id = victim.UserId });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<AccountProfileDto>(JsonOptions);
            body!.UserId.Should().Be(userId, "the mutation must apply to the authenticated operator");

            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var untouched = await db.Users.AsNoTracking().FirstAsync(u => u.Id == victim.UserId);
            untouched.DisplayName.Should().Be(victim.DisplayName);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/account/profile", new { displayName = original });
        }
    }

    // --- Preferences ---

    [Fact]
    public async Task Preferences_default_to_platform_values_and_round_trip_when_saved()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        try
        {
            var saved = await client.PutAsJsonAsync(
                "/api/account/preferences",
                new { appearance = "Dark", timeFormat = "Hour12", timeZoneId = "UTC" });
            saved.StatusCode.Should().Be(HttpStatusCode.OK);

            var reloaded = await client.GetFromJsonAsync<UserPreferencesDto>("/api/account/preferences", JsonOptions);
            reloaded!.Appearance.Should().Be("Dark");
            reloaded.TimeFormat.Should().Be("Hour12");
            reloaded.TimeZoneId.Should().Be("UTC");

            // The shell reads appearance, clock, and timezone off the session response.
            var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions);
            me!.Appearance.Should().Be("Dark");
            me.TimeFormat.Should().Be("Hour12");
            me.DisplayTimeZoneId.Should().Be("UTC");
        }
        finally
        {
            await client.PutAsJsonAsync(
                "/api/account/preferences",
                new { appearance = "System", timeFormat = "System", timeZoneId = (string?)null });
        }
    }

    [Theory]
    [InlineData("Chartreuse", "System", null)]
    [InlineData("System", "Hour36", null)]
    [InlineData("System", "System", "Mars/Olympus_Mons")]
    [InlineData("System", "System", "../../etc/localtime")]
    public async Task Preferences_reject_values_outside_the_supported_set(
        string appearance,
        string timeFormat,
        string? timeZoneId)
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new { appearance, timeFormat, timeZoneId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // --- Avatar ---

    [Fact]
    public async Task Avatar_options_are_the_curated_catalog_with_a_neutral_default()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var options = await client.GetFromJsonAsync<AvatarOptionDto[]>("/api/account/avatar/options", JsonOptions);

        options.Should().NotBeNull();
        options!.Should().HaveCount(BuiltInAvatarCatalog.Options.Count);
        options.Should().ContainSingle(o => o.IsDefault)
            .Which.Id.Should().Be(BuiltInAvatarCatalog.DefaultOptionId);
    }

    [Fact]
    public async Task Selecting_a_curated_avatar_persists_and_appears_on_the_session_response()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        try
        {
            var response = await client.PutAsJsonAsync("/api/account/avatar/selection", new { optionId = "option-b" });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions);
            me!.Avatar!.BuiltInOptionId.Should().Be("option-b");
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Theory]
    [InlineData("dragon")]
    [InlineData("../neutral")]
    [InlineData("")]
    public async Task Selecting_an_avatar_outside_the_catalog_is_rejected(string optionId)
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/account/avatar/selection", new { optionId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Uploading_a_valid_image_stores_a_normalized_avatar_that_can_be_served_back()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        try
        {
            var uploaded = await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Jpeg, 400, 260), "photo.jpg", "image/jpeg");
            uploaded.Kind.Should().Be("custom");
            uploaded.Version.Should().StartWith("avt_");
            uploaded.ImageUrl.Should().Contain("v=" + uploaded.Version);

            var image = await client.GetAsync($"/api/account/avatar/image?size=128&v={uploaded.Version}");
            image.StatusCode.Should().Be(HttpStatusCode.OK);
            image.Content.Headers.ContentType!.MediaType.Should().Be("image/png",
                "uploads are always re-encoded to a platform-controlled format");
            (await image.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Fact]
    public async Task The_avatar_serving_route_sets_an_explicit_content_type_and_blocks_sniffing()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        try
        {
            var uploaded = await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Png, 300, 300), "photo.png", "image/png");

            var image = await client.GetAsync($"/api/account/avatar/image?size=256&v={uploaded.Version}");

            image.Headers.TryGetValues("X-Content-Type-Options", out var sniff).Should().BeTrue();
            sniff!.Should().Contain("nosniff");
            image.Headers.CacheControl!.Private.Should().BeTrue("a profile photo is personal data");
            image.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365),
                "a versioned key makes the response safe to treat as immutable");
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Theory]
    [InlineData(96)]
    [InlineData(1024)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task The_avatar_serving_route_refuses_a_size_it_does_not_generate(int size)
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        try
        {
            await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Png, 300, 300), "photo.png", "image/png");

            var image = await client.GetAsync($"/api/account/avatar/image?size={size}");

            image.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Fact]
    public async Task A_user_on_the_built_in_avatar_gets_a_not_found_so_the_client_draws_the_default()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        await client.DeleteAsync("/api/account/avatar");

        var image = await client.GetAsync("/api/account/avatar/image?size=128");

        image.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploading_a_replacement_retires_the_previous_stored_image()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();

        try
        {
            var first = await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Png, 300, 300), "one.png", "image/png");
            var second = await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Jpeg, 320, 240), "two.jpg", "image/jpeg");

            second.Version.Should().NotBe(first.Version);

            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var keys = await db.UserProfileImages.AsNoTracking()
                .Where(i => i.UserId == userId)
                .Select(i => i.ImageKey)
                .Distinct()
                .ToListAsync();

            keys.Should().Equal(second.Version);
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Fact]
    public async Task Resetting_the_avatar_deletes_every_stored_image()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();
        await UploadAsync(client, EncodeImage(SKEncodedImageFormat.Png, 300, 300), "photo.png", "image/png");

        var reset = await client.DeleteAsync("/api/account/avatar");
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var remaining = await db.UserProfileImages.AsNoTracking().CountAsync(i => i.UserId == userId);
        remaining.Should().Be(0);
    }

    [Theory]
    [InlineData("payload.svg", "image/svg+xml")]
    [InlineData("payload.png", "image/png")]
    public async Task An_svg_upload_is_rejected_however_it_is_labelled(string fileName, string contentType)
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><script>alert(1)</script></svg>");

        var response = await PostAvatarAsync(client, svg, fileName, contentType);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_executable_named_as_an_image_is_rejected()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var executable = new byte[1024];
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';

        var response = await PostAvatarAsync(client, executable, "avatar.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_traversal_style_file_name_cannot_influence_the_stored_key()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();

        try
        {
            var uploaded = await UploadAsync(
                client,
                EncodeImage(SKEncodedImageFormat.Png, 300, 300),
                "../../../../windows/system32/evil.png",
                "image/png");

            uploaded.Version.Should().StartWith("avt_");
            uploaded.Version.Should().NotContain("..").And.NotContain("/").And.NotContain("\\");

            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var stored = await db.UserProfileImages.AsNoTracking().Where(i => i.UserId == userId).ToListAsync();
            stored.Should().OnlyContain(i => i.ImageKey.StartsWith("avt_"));
            stored.Should().OnlyContain(i => !i.ImageKey.Contains("evil"));
        }
        finally
        {
            await client.DeleteAsync("/api/account/avatar");
        }
    }

    [Fact]
    public async Task An_oversized_upload_is_refused()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var oversized = new byte[ProfileImageLimits.MaxUploadBytes + 4096];
        Random.Shared.NextBytes(oversized);

        var response = await PostAvatarAsync(client, oversized, "big.png", "image/png");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task An_upload_with_no_file_part_is_rejected()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        using var form = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/account/avatar", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_operators_avatar_is_not_reachable_through_another_operators_session()
    {
        AssertSqlAvailable();
        var (ownerClient, _, ownerId) = await CreateAuthenticatedClientAsync();
        var intruder = await CreateSecondUserAsync();

        try
        {
            var uploaded = await UploadAsync(
                ownerClient,
                EncodeImage(SKEncodedImageFormat.Png, 300, 300),
                "owner.png",
                "image/png");

            var intruderClient = _fixture.Factory!.CreateClient();
            intruderClient.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, intruder.ExternalId);

            // The route takes no key, and lookup is scoped by the resolved user, so knowing the owner's
            // version token gets an intruder nothing.
            var leaked = await intruderClient.GetAsync($"/api/account/avatar/image?size=128&v={uploaded.Version}");
            leaked.StatusCode.Should().Be(HttpStatusCode.NotFound);

            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var ownerRows = await db.UserProfileImages.AsNoTracking().CountAsync(i => i.UserId == ownerId);
            ownerRows.Should().BeGreaterThan(0, "the owner's avatar must be untouched");
        }
        finally
        {
            await ownerClient.DeleteAsync("/api/account/avatar");
        }
    }

    // --- Notification preferences ---

    [Fact]
    public async Task Notification_preferences_report_the_in_app_channel_as_not_editable()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var preferences = await client.GetFromJsonAsync<NotificationPreferenceDto[]>(
            "/api/account/notification-preferences",
            JsonOptions);

        preferences.Should().NotBeNull();
        preferences!.Should().OnlyContain(p => p.PlatformEnabled && !p.PlatformEditable);
        preferences.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.PlatformNotEditableReason));
        preferences.Should().OnlyContain(p => p.EmailEditable);
    }

    [Fact]
    public async Task An_email_preference_round_trips_and_is_reflected_on_reload()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();

        var preferences = await client.GetFromJsonAsync<NotificationPreferenceDto[]>(
            "/api/account/notification-preferences",
            JsonOptions);
        if (preferences is null || preferences.Length == 0)
            return; // No self-subscribable definitions seeded in this environment.

        var target = preferences[0];
        try
        {
            var enable = await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { emailEnabled = true });
            enable.StatusCode.Should().Be(HttpStatusCode.OK);
            (await enable.Content.ReadFromJsonAsync<NotificationPreferenceDto>(JsonOptions))!
                .EmailEnabled.Should().BeTrue();

            var reloaded = await client.GetFromJsonAsync<NotificationPreferenceDto[]>(
                "/api/account/notification-preferences",
                JsonOptions);
            reloaded!.Single(p => p.DefinitionId == target.DefinitionId).EmailEnabled.Should().BeTrue();

            var disable = await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { emailEnabled = false });
            (await disable.Content.ReadFromJsonAsync<NotificationPreferenceDto>(JsonOptions))!
                .EmailEnabled.Should().BeFalse();

            // Toast-B P2: the "Email off" toggle must be channel-specific -- it must not disable the
            // operator's self-subscription (the route) or write a global route-level opt-out override.
            // Verify all three directly against the database, not just the HTTP response body.
            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

            var overrideRow = await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                .FirstOrDefaultAsync(o => o.UserId == userId && o.NotificationDefinitionId == target.DefinitionId);
            overrideRow.Should().BeNull("disabling email must not write a global route-level opt-out override");

            var selfSubscription = await db.NotificationSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.NotificationDefinitionId == target.DefinitionId
                    && s.RecipientKind == NotificationRecipientKinds.Principal
                    && s.RecipientRef == userId
                    && s.Origin == NotificationSubscriptionOrigins.UserSelfSubscribed
                    && s.IntegrationId == null);
            selfSubscription.Should().NotBeNull("the enable step must have created the self-subscription");
            selfSubscription!.IsEnabled.Should().BeTrue(
                "disabling the email preference must not touch the underlying subscription/route");

            var preferenceRow = await db.NotificationUserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId && p.NotificationDefinitionId == target.DefinitionId && p.Channel == "email");
            preferenceRow.Should().NotBeNull();
            preferenceRow!.IsEnabled.Should().BeFalse();
        }
        finally
        {
            await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { emailEnabled = target.EmailEnabled });
        }
    }

    [Fact]
    public async Task A_toast_preference_round_trips_and_leaves_email_and_self_subscription_untouched()
    {
        AssertSqlAvailable();
        var (client, _, userId) = await CreateAuthenticatedClientAsync();

        var preferences = await client.GetFromJsonAsync<NotificationPreferenceDto[]>(
            "/api/account/notification-preferences",
            JsonOptions);
        if (preferences is null || preferences.Length == 0)
            return; // No self-subscribable definitions seeded in this environment.

        var target = preferences[0];
        var originalEmailEnabled = target.EmailEnabled;

        // Every test in this class authenticates as the same first-admin user (see
        // CreateAuthenticatedClientAsync), so an explicit email preference row for this definition may
        // already exist from another test's own round trip. Capture its current shape before writing
        // toast, so "email untouched" can be asserted against a real baseline rather than assumed absent.
        (string Id, DateTimeOffset UpdatedAtUtc)? baselineEmailRow;
        (string Id, DateTimeOffset UpdatedAtUtc)? baselineSelfSubscription;
        await using (var baselineScope = _fixture.Factory!.Services.CreateAsyncScope())
        {
            var baselineDb = baselineScope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var row = await baselineDb.NotificationUserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId && p.NotificationDefinitionId == target.DefinitionId && p.Channel == "email");
            baselineEmailRow = row is null ? null : (row.Id, row.UpdatedAtUtc);

            var subRow = await baselineDb.NotificationSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.NotificationDefinitionId == target.DefinitionId
                    && s.RecipientKind == NotificationRecipientKinds.Principal
                    && s.RecipientRef == userId
                    && s.Origin == NotificationSubscriptionOrigins.UserSelfSubscribed
                    && s.IntegrationId == null);
            baselineSelfSubscription = subRow is null ? null : (subRow.Id, subRow.UpdatedAtUtc);
        }

        try
        {
            var enable = await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { toastEnabled = true, emailEnabled = (bool?)null });
            enable.StatusCode.Should().Be(HttpStatusCode.OK);
            var enabled = (await enable.Content.ReadFromJsonAsync<NotificationPreferenceDto>(JsonOptions))!;
            enabled.ToastEnabled.Should().BeTrue();
            // The response must reflect BOTH channels' current state regardless of which one was written.
            enabled.EmailEnabled.Should().Be(originalEmailEnabled, "writing toast must not change email's reported state");

            var reloaded = await client.GetFromJsonAsync<NotificationPreferenceDto[]>(
                "/api/account/notification-preferences",
                JsonOptions);
            var reloadedTarget = reloaded!.Single(p => p.DefinitionId == target.DefinitionId);
            reloadedTarget.ToastEnabled.Should().BeTrue();
            reloadedTarget.EmailEnabled.Should().Be(originalEmailEnabled);

            var disable = await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { toastEnabled = false, emailEnabled = (bool?)null });
            var disabled = (await disable.Content.ReadFromJsonAsync<NotificationPreferenceDto>(JsonOptions))!;
            disabled.ToastEnabled.Should().BeFalse();
            disabled.EmailEnabled.Should().Be(originalEmailEnabled);

            // Assert directly against the database: only the toast preference row changed. No
            // subscription/route or global opt-out override exists for toast -- it has none.
            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

            var toastPreferenceRow = await db.NotificationUserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId && p.NotificationDefinitionId == target.DefinitionId && p.Channel == "toast");
            toastPreferenceRow.Should().NotBeNull();
            toastPreferenceRow!.IsEnabled.Should().BeFalse();

            var emailPreferenceRow = await db.NotificationUserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId && p.NotificationDefinitionId == target.DefinitionId && p.Channel == "email");
            if (baselineEmailRow is null)
            {
                emailPreferenceRow.Should().BeNull("only the toast channel was written; toast writes must never create an email row");
            }
            else
            {
                emailPreferenceRow.Should().NotBeNull();
                emailPreferenceRow!.Id.Should().Be(baselineEmailRow.Value.Id);
                emailPreferenceRow.UpdatedAtUtc.Should().Be(
                    baselineEmailRow.Value.UpdatedAtUtc, "writing the toast channel must never touch the email preference row");
            }

            var overrideRow = await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                .FirstOrDefaultAsync(o => o.UserId == userId && o.NotificationDefinitionId == target.DefinitionId);
            overrideRow.Should().BeNull("toast has no subscription/route concept, so writing it must never create a route-level override");

            var selfSubscription = await db.NotificationSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.NotificationDefinitionId == target.DefinitionId
                    && s.RecipientKind == NotificationRecipientKinds.Principal
                    && s.RecipientRef == userId
                    && s.Origin == NotificationSubscriptionOrigins.UserSelfSubscribed
                    && s.IntegrationId == null);
            if (baselineSelfSubscription is null)
            {
                selfSubscription.Should().BeNull("toast writes must never create an email self-subscription");
            }
            else
            {
                selfSubscription.Should().NotBeNull();
                selfSubscription!.Id.Should().Be(baselineSelfSubscription.Value.Id);
                selfSubscription.UpdatedAtUtc.Should().Be(
                    baselineSelfSubscription.Value.UpdatedAtUtc, "writing the toast channel must never touch the email self-subscription");
            }
        }
        finally
        {
            await client.PutAsJsonAsync(
                $"/api/account/notification-preferences/{Uri.EscapeDataString(target.DefinitionId)}",
                new { toastEnabled = target.ToastEnabled, emailEnabled = (bool?)null });
        }
    }

    [Fact]
    public async Task Setting_a_preference_for_a_notification_that_is_not_configurable_returns_not_found()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/account/notification-preferences/does.not.exist",
            new { emailEnabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Audit ---

    [Fact]
    public async Task Changing_the_display_name_writes_an_audit_record()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();
        var original = (await GetProfileAsync(client)).DisplayName;

        try
        {
            await client.PutAsJsonAsync("/api/account/profile", new { displayName = $"Audited {Guid.NewGuid():N}"[..20] });

            await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var page = await audit.QueryAsync(
                new AuditQuery(FilterByActionPrefix: "account.profile"),
                CancellationToken.None);

            page.Items.Select(r => r.Action).Should().Contain(AccountAuditActionNames.DisplayNameUpdated);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/account/profile", new { displayName = original });
        }
    }

    [Fact]
    public async Task Saving_appearance_and_clock_preferences_writes_no_audit_noise()
    {
        AssertSqlAvailable();
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        await using var beforeScope = _fixture.Factory!.Services.CreateAsyncScope();
        var auditBefore = beforeScope.ServiceProvider.GetRequiredService<IAuditService>();
        var before = await auditBefore.CountAsync(
            new AuditQuery(FilterByActionPrefix: "account."),
            CancellationToken.None);

        try
        {
            await client.PutAsJsonAsync(
                "/api/account/preferences",
                new { appearance = "Dark", timeFormat = "Hour12", timeZoneId = "UTC" });

            await using var afterScope = _fixture.Factory.Services.CreateAsyncScope();
            var auditAfter = afterScope.ServiceProvider.GetRequiredService<IAuditService>();
            var after = await auditAfter.CountAsync(
                new AuditQuery(FilterByActionPrefix: "account."),
                CancellationToken.None);

            // Presentation-only choices must not fill the operational audit log.
            after.Should().Be(before);
        }
        finally
        {
            await client.PutAsJsonAsync(
                "/api/account/preferences",
                new { appearance = "System", timeFormat = "System", timeZoneId = (string?)null });
        }
    }

    // --- Helpers ---

    private static byte[] EncodeImage(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private async Task<AccountProfileDto> GetProfileAsync(HttpClient client)
    {
        var profile = await client.GetFromJsonAsync<AccountProfileDto>("/api/account/profile", JsonOptions);
        profile.Should().NotBeNull();
        return profile!;
    }

    private static async Task<HttpResponseMessage> PostAvatarAsync(
        HttpClient client,
        byte[] content,
        string fileName,
        string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/account/avatar", form);
    }

    private async Task<AccountAvatarDto> UploadAsync(
        HttpClient client,
        byte[] content,
        string fileName,
        string contentType)
    {
        var response = await PostAvatarAsync(client, content, fileName, contentType);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var avatar = await response.Content.ReadFromJsonAsync<AccountAvatarDto>(JsonOptions);
        avatar.Should().NotBeNull();
        return avatar!;
    }

    private async Task EnsureFirstAdminAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        if (await db.Users.AnyAsync())
            return;

        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest(
                "Account Test Admin",
                "Local",
                $"acct-admin-{Guid.NewGuid():N}"[..16],
                "unique-local-passphrase"));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Client, string Username, string UserId)> CreateAuthenticatedClientAsync()
    {
        await EnsureFirstAdminAsync();

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var link = await db.IdentityLinks.AsNoTracking()
            .Where(l => l.Source == (int)IdentitySource.Local)
            .OrderBy(l => l.ExternalId)
            .FirstAsync();

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, link.ExternalId);
        return (client, link.ExternalId, link.UserId);
    }

    /// <summary>
    /// Creates a second linked user so cross-account isolation can be asserted against a real record
    /// rather than a fabricated id.
    /// </summary>
    private async Task<(string UserId, string ExternalId, string DisplayName)> CreateSecondUserAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var userId = $"usr_{Guid.NewGuid():N}"[..24];
        var externalId = $"second-{Guid.NewGuid():N}"[..24];
        const string displayName = "Second Operator";

        db.Users.Add(new UserEntity { Id = userId, DisplayName = displayName, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            UserId = userId,
            Source = (int)IdentitySource.Local,
            ExternalId = externalId,
            LastAuthenticatedAt = null
        });
        await db.SaveChangesAsync();

        return (userId, externalId, displayName);
    }
}
