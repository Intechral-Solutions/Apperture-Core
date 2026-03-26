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
}
