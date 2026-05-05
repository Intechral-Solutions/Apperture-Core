using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class SqlAttentionRecipientResolverTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Integration_primary_overrides_instance()
    {
        await using var db = CreateDb();
        var gInst = "grp_inst";
        var gInt = "grp_int";
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = gInst,
            Name = "Instance primary",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = gInt,
            Name = "Integration primary",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = gInst,
            MemberKind = "ExternalEmail",
            EmailAddress = "global@test.com",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m2",
            OperationalTeamId = gInt,
            MemberKind = "ExternalEmail",
            EmailAddress = "override@test.com",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            PrimaryOperationalTeamId = gInst,
        });
        var intRow = new IntegrationAttentionRoutingEntity
        {
            Id = "ir1",
            IntegrationId = "int1",
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryOperationalTeamId = gInt,
        };
        db.IntegrationAttentionRoutings.Add(intRow);
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.PrimaryRecipients.Should().Equal("override@test.com");
        r.PrimarySource.Should().Be(AttentionRecipientSources.IntegrationOverride);
        r.EscalationRecipients.Should().BeEmpty();
        r.EscalationSource.Should().Be(AttentionRecipientSources.None);
        r.IntegrationRowPrimaryOperationalTeamId.Should().Be(gInt);
        r.IntegrationRowEscalationOperationalTeamId.Should().BeNull();
    }

    [Fact]
    public async Task When_no_instance_row_returns_disabled_resolution()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.Enabled.Should().BeFalse();
        r.HasRoute.Should().BeFalse();
    }

    [Fact]
    public async Task Principal_member_resolves_from_user_email()
    {
        await using var db = CreateDb();
        db.Users.Add(new UserEntity
        {
            Id = "user_1",
            DisplayName = "Primary Owner",
            EmailAddress = "owner@test.com",
        });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = "team_1",
            Name = "Primary",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "tm_1",
            OperationalTeamId = "team_1",
            MemberKind = "Principal",
            PrincipalUserId = "user_1",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            PrimaryOperationalTeamId = "team_1",
        });
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);
        r.PrimaryRecipients.Should().ContainSingle().Which.Should().Be("owner@test.com");
        r.ResolvedPrimaryPrincipalUserIds.Should().ContainSingle().Which.Should().Be("user_1");
    }

    [Fact]
    public async Task Principal_member_without_email_reports_honest_no_route_reason()
    {
        await using var db = CreateDb();
        db.Users.Add(new UserEntity
        {
            Id = "user_no_mail",
            DisplayName = "No Mail User",
            EmailAddress = null,
        });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = "team_principal_only",
            Name = "Principal only",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "tm_no_mail",
            OperationalTeamId = "team_principal_only",
            MemberKind = "Principal",
            PrincipalUserId = "user_no_mail",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            PrimaryOperationalTeamId = "team_principal_only",
        });
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.HasPrimaryRoute.Should().BeFalse();
        r.PrimaryRecipients.Should().BeEmpty();
        r.PrimaryNoRouteReason.Should().Be(AttentionNoRouteReasons.PrincipalMembersNoEmail);
    }

    [Fact]
    public async Task Mixed_members_resolve_external_fallback_when_some_principals_unusable()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            new UserEntity { Id = "u_ok", DisplayName = "OK", EmailAddress = "ok@test.com" },
            new UserEntity { Id = "u_bad", DisplayName = "BAD", EmailAddress = null });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = "team_mixed",
            Name = "Mixed",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.AddRange(
            new OperationalTeamMemberEntity
            {
                Id = "tm1", OperationalTeamId = "team_mixed", MemberKind = "Principal", PrincipalUserId = "u_ok",
                IsEnabled = true, CreatedAtUtc = UtcNow(), UpdatedAtUtc = UtcNow(),
            },
            new OperationalTeamMemberEntity
            {
                Id = "tm2", OperationalTeamId = "team_mixed", MemberKind = "Principal", PrincipalUserId = "u_bad",
                IsEnabled = true, CreatedAtUtc = UtcNow(), UpdatedAtUtc = UtcNow(),
            },
            new OperationalTeamMemberEntity
            {
                Id = "tm3", OperationalTeamId = "team_mixed", MemberKind = "ExternalEmail", EmailAddress = "fallback@test.com",
                IsEnabled = true, CreatedAtUtc = UtcNow(), UpdatedAtUtc = UtcNow(),
            });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            PrimaryOperationalTeamId = "team_mixed",
        });
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.PrimaryRecipients.Should().BeEquivalentTo(new[] { "ok@test.com", "fallback@test.com" });
        r.PrimaryNoRouteReason.Should().BeNull();
    }

    [Fact]
    public async Task Managed_contact_member_resolves_email_from_managed_contacts_table()
    {
        await using var db = CreateDb();
        db.ManagedContacts.Add(new ManagedContactEntity
        {
            Id = "mcontact_mc1",
            DisplayName = "Importer",
            EmailAddress = "mc@test.com",
            Source = "Custom",
            IsEnabled = true,
            LinkedUserId = null,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = "team_mc",
            Name = "MC",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "mcmem1",
            OperationalTeamId = "team_mc",
            MemberKind = "ManagedContact",
            ManagedContactId = "mcontact_mc1",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            PrimaryOperationalTeamId = "team_mc",
        });
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.PrimaryRecipients.Should().Equal("mc@test.com");
        r.PrimaryNoRouteReason.Should().BeNull();
    }
}
