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
            new OperationalTeamRecipientExpander(db));

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
}
