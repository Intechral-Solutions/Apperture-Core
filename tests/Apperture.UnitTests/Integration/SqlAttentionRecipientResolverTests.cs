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

    [Fact]
    public async Task Integration_primary_overrides_instance()
    {
        await using var db = CreateDb();
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
        });
        db.AttentionRoutingRecipients.Add(new AttentionRoutingRecipientEntity
        {
            Id = "r_inst",
            InstanceAttentionRoutingId = InstanceAttentionRoutingDefaults.RowId,
            Role = AttentionRoutingRecipientRoles.Primary,
            Address = "global@test.com",
        });
        var intRow = new IntegrationAttentionRoutingEntity
        {
            Id = "ir1",
            IntegrationId = "int1",
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
        };
        db.IntegrationAttentionRoutings.Add(intRow);
        db.AttentionRoutingRecipients.Add(new AttentionRoutingRecipientEntity
        {
            Id = "r_int",
            IntegrationAttentionRoutingId = "ir1",
            Role = AttentionRoutingRecipientRoles.Primary,
            Address = "override@test.com",
        });
        await db.SaveChangesAsync();

        var sut = new SqlAttentionRecipientResolver(db);
        var r = await sut.ResolveAsync(new IntegrationId("int1"), AttentionConditionKeys.ExpectedRunMissing);

        r.PrimaryRecipients.Should().Equal("override@test.com");
        r.PrimarySource.Should().Be(AttentionRecipientSources.IntegrationOverride);
        r.EscalationRecipients.Should().BeEmpty();
        r.EscalationSource.Should().Be(AttentionRecipientSources.None);
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
