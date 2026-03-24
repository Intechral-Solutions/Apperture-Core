using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Epic 50: Integration composition — settings, bindings, schedules.
/// </summary>
public sealed class Epic50IntegrationCompositionTests
{
    [Fact]
    public async Task SqlIntegrationRepository_Settings_RoundTrip_CreateUpdateRead()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppertureDbContext(opts);

        var integration = new DomainIntegration(
            new IntegrationId("int-1"),
            "Test Integration",
            "Sample.Module",
            null,
            IntegrationState.Active,
            new Dictionary<string, string> { ["key1"] = "val1", ["key2"] = "val2" },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var repo = new SqlIntegrationRepository(db);
        await repo.AddAsync(integration);

        var read = await repo.GetByIdAsync(new IntegrationId("int-1"));
        read.Should().NotBeNull();
        read!.Settings.Should().NotBeNull();
        read.Settings!["key1"].Should().Be("val1");
        read.Settings["key2"].Should().Be("val2");

        var updated = read with
        {
            Settings = new Dictionary<string, string> { ["key1"] = "updated", ["key3"] = "new" },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repo.UpdateAsync(updated);

        var afterUpdate = await repo.GetByIdAsync(new IntegrationId("int-1"));
        afterUpdate!.Settings.Should().NotBeNull();
        afterUpdate.Settings!["key1"].Should().Be("updated");
        afterUpdate.Settings["key3"].Should().Be("new");
        afterUpdate.Settings.Should().NotContainKey("key2");
    }

    [Fact]
    public async Task SqlBindingRepository_GetByIntegrationIdAsync_ReturnsBindingsForIntegration()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppertureDbContext(opts);

        db.Bindings.Add(new BindingEntity
        {
            Id = "b1",
            IntegrationId = "int-1",
            ModuleId = "ModA",
            SlotName = "Db",
            ContractType = "IDbConnection",
            ConnectionId = "conn-1",
            State = 1
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "b2",
            IntegrationId = "int-1",
            ModuleId = "ModA",
            SlotName = "Api",
            ContractType = "HttpClient",
            ConnectionId = "conn-2",
            State = 1
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "b3",
            IntegrationId = "int-2",
            ModuleId = "ModB",
            SlotName = "Db",
            ContractType = "IDbConnection",
            ConnectionId = "conn-1",
            State = 1
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "b4",
            IntegrationId = null,
            ModuleId = "ModA",
            SlotName = "Other",
            ContractType = "IDbConnection",
            ConnectionId = "conn-3",
            State = 1
        });
        await db.SaveChangesAsync();

        var repo = new SqlBindingRepository(db);
        var result = await repo.GetByIntegrationIdAsync(new IntegrationId("int-1"));

        result.Should().HaveCount(2);
        result.Should().Contain(b => b.Id.Value == "b1" && b.SlotName == "Db");
        result.Should().Contain(b => b.Id.Value == "b2" && b.SlotName == "Api");
        result.Should().NotContain(b => b.Id.Value == "b3");
        result.Should().NotContain(b => b.Id.Value == "b4");
    }

    [Fact]
    public async Task SqlScheduleRepository_GetByIntegrationIdAsync_ReturnsSchedulesForIntegration()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppertureDbContext(opts);

        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "s1",
            IntegrationId = "int-1",
            Name = "Schedule 1",
            RecurrenceExpression = "0 * * * *",
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = 0,
            Version = 1
        });
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "s2",
            IntegrationId = "int-1",
            Name = "Schedule 2",
            RecurrenceExpression = "0 0 * * *",
            TimeZone = "UTC",
            Enabled = false,
            TargetJobType = 0,
            Version = 1
        });
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "s3",
            IntegrationId = "int-2",
            Name = "Other Integration",
            RecurrenceExpression = "0 * * * *",
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = 0,
            Version = 1
        });
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "s4",
            IntegrationId = null,
            Name = "Global Schedule",
            RecurrenceExpression = "0 * * * *",
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = 0,
            Version = 1
        });
        await db.SaveChangesAsync();

        var repo = new SqlScheduleRepository(db);
        var result = await repo.GetByIntegrationIdAsync(new IntegrationId("int-1"));

        result.Should().HaveCount(2);
        result.Should().Contain(s => s.Id.Value == "s1" && s.Name == "Schedule 1");
        result.Should().Contain(s => s.Id.Value == "s2" && s.Name == "Schedule 2");
        result.Should().NotContain(s => s.Id.Value == "s3");
        result.Should().NotContain(s => s.Id.Value == "s4");
    }
}
