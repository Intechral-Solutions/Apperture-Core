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
        db.AttentionContactGroups.Add(new AttentionContactGroupEntity
        {
            Id = gInst,
            Name = "Instance primary",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.AttentionContactGroups.Add(new AttentionContactGroupEntity
        {
            Id = gInt,
            Name = "Integration primary",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.AttentionContactGroupMembers.Add(new AttentionContactGroupMemberEntity
        {
            Id = "m1",
            AttentionContactGroupId = gInst,
            EmailAddress = "global@test.com",
            IsEnabled = true,
            CreatedAtUtc = UtcNow(),
            UpdatedAtUtc = UtcNow(),
        });
        db.AttentionContactGroupMembers.Add(new AttentionContactGroupMemberEntity
        {
            Id = "m2",
            AttentionContactGroupId = gInt,
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
            PrimaryContactGroupId = gInst,
        });
        var intRow = new IntegrationAttentionRoutingEntity
        {
            Id = "ir1",
            IntegrationId = "int1",
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryContactGroupId = gInt,
        };
        db.IntegrationAttentionRoutings.Add(intRow);
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.PrimaryRecipients.Should().Equal("override@test.com");
        r.PrimarySource.Should().Be(AttentionRecipientSources.IntegrationOverride);
        r.EscalationRecipients.Should().BeEmpty();
        r.EscalationSource.Should().Be(AttentionRecipientSources.None);
        r.IntegrationRowPrimaryContactGroupId.Should().Be(gInt);
        r.IntegrationRowEscalationContactGroupId.Should().BeNull();
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
}
