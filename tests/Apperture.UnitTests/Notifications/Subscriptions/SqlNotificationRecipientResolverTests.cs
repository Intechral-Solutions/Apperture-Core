using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

// Note: NotificationRecipientSkipReasons.UserDisabled is a reserved skip reason. As of this
// writing, SqlNotificationRecipientResolver.ExpandPrincipalAsync has no concept of a disabled
// UserEntity (UserEntity carries no such flag), so that reason is never produced today. Do not
// assume principal "disabled" handling is covered by these characterization tests.
public sealed class SqlNotificationRecipientResolverTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static SqlNotificationRecipientResolver CreateSut(AppertureDbContext db) =>
        new(
            db,
            new SqlNotificationSubscriptionStore(db),
            new SqlNotificationDefinitionStore(db),
            new OperationalTeamRecipientExpander(db),
            new SqlNotificationUserPreferenceStore(db));

    private static async Task SeedBuiltInDefinitionAsync(AppertureDbContext db)
    {
        var builtin = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = builtin.Id.Value,
            DisplayName = builtin.DisplayName,
            Description = builtin.Description,
            SourceKind = builtin.SourceKind,
            Origin = builtin.Origin,
            Scope = builtin.Scope,
            Mutability = builtin.Mutability,
            PrimaryEventType = builtin.PrimaryEventType,
            IsEnabled = builtin.IsEnabled,
            DefaultSeverity = builtin.DefaultSeverity,
            RuleJson = builtin.RuleJson,
            TemplateJson = builtin.TemplateJson,
            CreatedAtUtc = builtin.CreatedAtUtc,
            UpdatedAtUtc = builtin.UpdatedAtUtc,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Principal_subscription_resolves_deliverable_email()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "owner@test.com" && r.UserId == "user-1");
    }

    [Fact]
    public async Task User_self_subscribed_principal_resolves_deliverable_email()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            "int-1");

        preview.Deliverable.Should().ContainSingle(r => r.Email == "owner@test.com");
    }

    [Fact]
    public async Task Global_email_opt_out_skips_user_with_subscription()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, null, "user-1"));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            "int-1");

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.UserOptOut);
    }

    [Fact]
    public async Task Team_member_opt_out_is_skipped_direct_principal_survives()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-1";
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Member", EmailAddress = "member@test.com" });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Ops",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "Principal",
            PrincipalUserId = "user-1",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(
                "user-1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                teamId,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "member@test.com");
        preview.Skipped.Should().Contain(s =>
            s.Reason == NotificationRecipientSkipReasons.UserOptOut
            && s.Source == NotificationRecipientSources.OperationalTeamMembership);
    }

    [Fact]
    public async Task Disabled_managed_contact_is_skipped()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.ManagedContacts.Add(new ManagedContactEntity
        {
            Id = "mc-1",
            DisplayName = "Vendor",
            EmailAddress = "vendor@test.com",
            Source = "Custom",
            IsEnabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.ManagedContact,
                "mc-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.ContactDisabled);
    }

    [Fact]
    public async Task Implicit_attention_routing_when_no_explicit_team_subscription()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-routing";
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Routing team",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "ExternalEmail",
            EmailAddress = "routing@test.com",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryOperationalTeamId = teamId,
        });
        db.IntegrationAttentionRoutings.Add(new IntegrationAttentionRoutingEntity
        {
            Id = "ir1",
            IntegrationId = "int-1",
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryOperationalTeamId = teamId,
        });
        await db.SaveChangesAsync();

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            "int-1");

        preview.Deliverable.Should().ContainSingle(r =>
            r.Email == "routing@test.com"
            && r.Source == NotificationRecipientSources.AttentionRoutingIntegrationOverride);
    }

    [Fact]
    public async Task Dedupes_by_email_across_sources()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-dup";
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Dup", EmailAddress = "dup@test.com" });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Dup team",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "Principal",
            PrincipalUserId = "user-1",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().HaveCount(1);
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.DuplicateEmail);
    }

    [Fact]
    public async Task Principal_with_missing_email_is_skipped_with_missing_email_reason()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "No Email", EmailAddress = null });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.MissingEmail);
    }

    [Fact]
    public async Task Team_disabled_is_skipped_with_team_disabled_reason()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-disabled";
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Disabled team",
            IsEnabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.TeamDisabled);
    }

    [Fact]
    public async Task Principal_with_missing_email_is_still_InApp_eligible()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "No Email", EmailAddress = null });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.MissingEmail);
        preview.InApp.Should().ContainSingle(r => r.UserId == "user-1");
    }

    [Fact]
    public async Task Linked_managed_contact_InApp_entry_carries_ManagedContactId()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.ManagedContacts.Add(new ManagedContactEntity
        {
            Id = "mc-linked",
            DisplayName = "Linked Vendor",
            EmailAddress = "linked-vendor@test.com",
            Source = "Custom",
            IsEnabled = true,
            LinkedUserId = "user-linked",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.ManagedContact,
                "mc-linked",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r =>
            r.Email == "linked-vendor@test.com" && r.ManagedContactId == "mc-linked");
        preview.InApp.Should().ContainSingle(r =>
            r.UserId == "user-linked"
            && r.ManagedContactId == "mc-linked"
            && r.RecipientKind == NotificationRecipientKinds.ManagedContact);
    }

    [Fact]
    public async Task Two_users_sharing_email_both_InApp_eligible_but_only_one_email_deliverable()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "First", EmailAddress = "shared@test.com" });
        db.Users.Add(new UserEntity { Id = "user-2", DisplayName = "Second", EmailAddress = "shared@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-2",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().HaveCount(1);
        preview.InApp.Should().HaveCount(2);
        preview.InApp.Select(r => r.UserId).Should().BeEquivalentTo(["user-1", "user-2"]);
    }

    [Fact]
    public async Task Global_opt_out_suppresses_InApp_too()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, null, "user-1"));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            "int-1");

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.UserOptOut);
        preview.InApp.Should().BeEmpty();
    }

    [Fact]
    public async Task Team_scoped_opt_out_keeps_InApp_route_scoped()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-1";
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Member", EmailAddress = "member@test.com" });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Ops",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "Principal",
            PrincipalUserId = "user-1",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(
                "user-1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                teamId,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "member@test.com");
        preview.Skipped.Should().Contain(s =>
            s.Reason == NotificationRecipientSkipReasons.UserOptOut
            && s.Source == NotificationRecipientSources.OperationalTeamMembership);
        preview.InApp.Should().Contain(r => r.UserId == "user-1");
    }

    [Fact]
    public async Task Disabled_managed_contact_is_not_InApp_eligible()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Linked", EmailAddress = "linked@test.com" });
        db.ManagedContacts.Add(new ManagedContactEntity
        {
            Id = "mc-1",
            DisplayName = "Vendor",
            EmailAddress = "vendor@test.com",
            Source = "Custom",
            IsEnabled = false,
            LinkedUserId = "user-1",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.ManagedContact,
                "mc-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.ContactDisabled);
        preview.InApp.Should().BeEmpty();
    }

    [Fact]
    public async Task Team_empty_is_skipped_with_team_empty_reason()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-empty";
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Empty team",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.TeamEmpty);
    }

    [Fact]
    public async Task Explicit_email_preference_false_skips_with_EmailOptedOut_reason()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preferenceStore = new SqlNotificationUserPreferenceStore(db);
        await preferenceStore.SetAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationChannels.Email,
            false,
            "user-1");

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().BeEmpty();
        preview.Skipped.Should().Contain(s => s.Reason == NotificationRecipientSkipReasons.EmailOptedOut);
        preview.InApp.Should().ContainSingle(r => r.UserId == "user-1");
    }

    [Fact]
    public async Task Explicit_email_preference_true_does_not_skip()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preferenceStore = new SqlNotificationUserPreferenceStore(db);
        await preferenceStore.SetAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationChannels.Email,
            true,
            "user-1");

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "owner@test.com" && r.UserId == "user-1");
        preview.Skipped.Should().NotContain(s => s.Reason == NotificationRecipientSkipReasons.EmailOptedOut);
    }

    [Fact]
    public async Task No_email_preference_row_behaves_as_before()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "owner@test.com" && r.UserId == "user-1");
        preview.Skipped.Should().NotContain(s => s.Reason == NotificationRecipientSkipReasons.EmailOptedOut);
        preview.InApp.Should().ContainSingle(r => r.UserId == "user-1");
    }

    [Fact]
    public async Task Admin_global_opt_out_still_suppresses_everything_regardless_of_email_preference()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, null, "user-1"));

        var preferenceStore = new SqlNotificationUserPreferenceStore(db);
        await preferenceStore.SetAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationChannels.Email,
            true,
            "user-1");

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            "int-1");

        preview.Deliverable.Should().BeEmpty();
        preview.InApp.Should().BeEmpty();
        preview.Skipped.Should().ContainSingle(s => s.UserId == "user-1")
            .Which.Reason.Should().Be(NotificationRecipientSkipReasons.UserOptOut);
    }

    [Fact]
    public async Task Team_scoped_opt_out_still_route_scoped_after_this_change()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-1";
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Member", EmailAddress = "member@test.com" });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Ops",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "Principal",
            PrincipalUserId = "user-1",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlNotificationSubscriptionStore(db);
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.OperationalTeam,
                teamId,
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(
                "user-1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                teamId,
                null));

        // Explicit email preference is set to true, proving the email-preference check is
        // completely uninvolved in team-scoped opt-out routing -- the direct-subscription path
        // still delivers, and the team-scoped skip is still reported as UserOptOut, not
        // EmailOptedOut, exactly as before this change.
        var preferenceStore = new SqlNotificationUserPreferenceStore(db);
        await preferenceStore.SetAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationChannels.Email,
            true,
            "user-1");

        var preview = await CreateSut(db).ResolvePreviewAsync(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null);

        preview.Deliverable.Should().ContainSingle(r => r.Email == "member@test.com");
        preview.Skipped.Should().Contain(s =>
            s.Reason == NotificationRecipientSkipReasons.UserOptOut
            && s.Source == NotificationRecipientSources.OperationalTeamMembership);
        preview.InApp.Should().Contain(r => r.UserId == "user-1");
    }
}
