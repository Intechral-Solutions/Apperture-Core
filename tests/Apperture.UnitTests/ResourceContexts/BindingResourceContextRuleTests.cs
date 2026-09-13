using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// An Integration may bind a Connection in its own ResourceContext or in Shared, and nothing else.
/// The rule is enforced twice: during eligibility discovery, and independently in the authoritative
/// binding mutation path — integrity must never depend on a discovery query.
/// </summary>
public sealed class BindingResourceContextRuleTests
{
    private const string ModuleId = "Sample.Mod";
    private const string SlotName = "Db";
    private const string ContractType = "System.Data.IDbConnection";

    // -------------------------------------------------------------------------------------------
    // Eligibility
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Eligibility_IncludesSameContextAndShared_AndExcludesOtherContexts()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var appB = await h.ResourceContexts.CreateApplicationContextAsync("Application B");

        var sharedConnection = await CreateConnectionAsync(h, "Shared Connection", null);
        var appAConnection = await CreateConnectionAsync(h, "A Connection", appA.Id);
        var appBConnection = await CreateConnectionAsync(h, "B Connection", appB.Id);

        var eligible = await h.Eligibility.GetEligibleConnectionsAsync(ModuleId, SlotName, ContractType, appA.Id);

        eligible.Select(c => c.Id.Value).Should().BeEquivalentTo([sharedConnection.Id.Value, appAConnection.Id.Value]);
        eligible.Select(c => c.Id.Value).Should().NotContain(appBConnection.Id.Value);
    }

    [Fact]
    public async Task Eligibility_ForASharedIntegration_ExcludesApplicationOnlyConnections()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var sharedConnection = await CreateConnectionAsync(h, "Shared Connection", null);
        await CreateConnectionAsync(h, "A Connection", appA.Id);

        var eligible = await h.Eligibility.GetEligibleConnectionsAsync(
            ModuleId, SlotName, ContractType, ResourceContextIds.Shared);

        eligible.Select(c => c.Id.Value).Should().BeEquivalentTo([sharedConnection.Id.Value]);
    }

    /// <summary>
    /// Without an owning Integration there is no context to compare against, so no context filter is
    /// applied. That is discovery only; it is not a path to an invalid binding, because the mutation
    /// path re-checks the rule.
    /// </summary>
    [Fact]
    public async Task Eligibility_WithoutAnOwningContext_AppliesNoContextFilter()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await CreateConnectionAsync(h, "Shared Connection", null);
        await CreateConnectionAsync(h, "A Connection", appA.Id);

        var eligible = await h.Eligibility.GetEligibleConnectionsAsync(
            ModuleId, SlotName, ContractType, owningResourceContextId: null);

        eligible.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------------------------
    // Authoritative mutation
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateBinding_AllowsAConnectionInTheSameContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", ModuleId, ResourceContextId: appA.Id));
        var connection = await CreateConnectionAsync(h, "A Connection", appA.Id);

        var binding = await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);

        binding.ConnectionId!.Value.Should().Be(connection.Id.Value);
        binding.IntegrationId!.Value.Should().Be(integration.Id.Value);
    }

    [Fact]
    public async Task CreateBinding_AllowsASharedConnection()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", ModuleId, ResourceContextId: appA.Id));
        var connection = await CreateConnectionAsync(h, "Shared Connection", null);

        var binding = await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);

        binding.ConnectionId!.Value.Should().Be(connection.Id.Value);
    }

    [Fact]
    public async Task CreateBinding_RejectsAConnectionInAnotherApplicationContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var appB = await h.ResourceContexts.CreateApplicationContextAsync("Application B");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", ModuleId, ResourceContextId: appA.Id));
        var foreign = await CreateConnectionAsync(h, "B Connection", appB.Id);

        var act = async () => await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, foreign.Id, integration.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*neither this Integration's ResourceContext*nor Shared*");
    }

    [Fact]
    public async Task CreateBinding_RejectsAnApplicationConnectionForASharedIntegration()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var sharedIntegration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));
        var appConnection = await CreateConnectionAsync(h, "A Connection", appA.Id);

        var act = async () => await h.Bindings.CreateBindingAsync(
            ModuleId, SlotName, ContractType, appConnection.Id, sharedIntegration.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*neither this Integration's ResourceContext*nor Shared*");
    }

    [Fact]
    public async Task CreateBinding_RejectsAMissingConnection()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));

        var act = async () => await h.Bindings.CreateBindingAsync(
            ModuleId, SlotName, ContractType, new ConnectionId("missing"), integration.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Connection missing not found*");
    }

    private static async Task<Connection> CreateConnectionAsync(
        ResourceContextOwnershipTestHarness harness,
        string name,
        ResourceContextId? resourceContextId) =>
        await harness.Connections.CreateConnectionAsync(new CreateConnectionRequest(
            new ConnectorDefinitionId("test.connector"),
            name,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: resourceContextId));
}
