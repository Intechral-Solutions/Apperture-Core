using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class SqlAttentionIncidentNotificationLedgerTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task Append_and_list_roundtrip()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionIncidentNotificationLedger(db);
        var row = new AttentionIncidentNotificationWrite(
            "n1",
            "inc1",
            "int1",
            AttentionConditionKeys.ExpectedRunMissing,
            AttentionNotificationKinds.Activated,
            AttentionNotificationChannels.Email,
            "a@test.com",
            AttentionNotificationStatuses.Succeeded,
            null,
            DateTimeOffset.Parse("2025-03-01T10:00:00Z"),
            DateTimeOffset.Parse("2025-03-01T10:00:01Z"),
            DateTimeOffset.Parse("2025-03-01T10:00:02Z"),
            null,
            null,
            null,
            "subj",
            AttentionRecipientSources.GlobalDefault,
            false,
            false,
            false);
        await sut.AppendAsync(row, CancellationToken.None);

        var list = await sut.ListRecentForIncidentsAsync(new[] { "inc1" }, 10, CancellationToken.None);
        list.Should().ContainSingle();
        list[0].Recipient.Should().Be("a@test.com");

        (await sut.HasSucceededActivationAsync("inc1", CancellationToken.None)).Should().BeTrue();
        (await sut.HasNonSuppressedActivationAttemptAsync("inc1", CancellationToken.None)).Should().BeTrue();
    }
}
