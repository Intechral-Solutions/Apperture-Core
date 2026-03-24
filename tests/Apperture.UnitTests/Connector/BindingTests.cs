using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Connector;

public sealed class BindingTests
{
    [Fact]
    public void Binding_implements_IGovernedResource()
    {
        var binding = new Binding(
            new BindingId("bind-1"),
            null,
            "ModuleA",
            "DbConnection",
            "System.Data.IDbConnection",
            new ConnectionId("conn-1"),
            null,
            BindingState.Active);

        var identity = binding.GetIdentity();

        identity.ResourceType.Should().Be(ResourceTypeConstants.Binding);
        identity.ResourceId.Should().Be("bind-1");
        identity.ScopeKey.Should().Be("module:ModuleA:slot:DbConnection");
    }

    [Fact]
    public void Binding_SlotScopeKey_formats_correctly()
    {
        var binding = new Binding(
            new BindingId("b1"),
            null,
            "MyModule",
            "ApiClient",
            "HttpClient",
            null,
            "cap-xyz",
            BindingState.Active);

        binding.SlotScopeKey.Should().Be("module:MyModule:slot:ApiClient");
    }

    [Fact]
    public async Task SqlBindingRepository_GetByConnectionIdAsync_ReturnsBindingsForConnection()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppertureDbContext(opts);
        db.Bindings.Add(new BindingEntity
        {
            Id = "b1",
            ModuleId = "ModA",
            SlotName = "Db",
            ContractType = "IDbConnection",
            ConnectionId = "conn-1",
            State = (int)BindingState.Active
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "b2",
            ModuleId = "ModB",
            SlotName = "Api",
            ContractType = "HttpClient",
            ConnectionId = "conn-1",
            State = (int)BindingState.Active
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "b3",
            ModuleId = "ModC",
            SlotName = "Other",
            ContractType = "IDbConnection",
            ConnectionId = "conn-2",
            State = (int)BindingState.Active
        });
        await db.SaveChangesAsync();

        var repo = new SqlBindingRepository(db);
        var result = await repo.GetByConnectionIdAsync(new ConnectionId("conn-1"));

        result.Should().HaveCount(2);
        result.Should().Contain(b => b.ModuleId == "ModA" && b.SlotName == "Db");
        result.Should().Contain(b => b.ModuleId == "ModB" && b.SlotName == "Api");
        result.Should().NotContain(b => b.ConnectionId!.Value == "conn-2");
    }
}
