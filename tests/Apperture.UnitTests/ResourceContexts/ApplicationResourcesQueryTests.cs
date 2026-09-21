using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// The Application Resources surface consumes Inventory's normalized resource read model with a fixed
/// ResourceContext and a three-family restriction. These tests pin the CURRENT-OWNERSHIP semantics:
/// <c>resource.ResourceContextId == context.Id</c>, never "referenced by" and never historical.
/// </summary>
public sealed class ApplicationResourcesQueryTests
{
    private static readonly string[] OperationalFamilies =
    [
        InventoryTypeTokens.Integration,
        InventoryTypeTokens.Connection,
        InventoryTypeTokens.Schedule
    ];

    private static InventoryQuery Owned(ResourceContextId id, string? q = null, IReadOnlyList<string>? types = null, int take = 100) =>
        new(
            Mode: InventoryModes.Resources,
            Q: q,
            Sort: InventorySorts.Name,
            Take: take,
            Context: InventoryContextFilter.ForContext(id.Value),
            Types: types ?? OperationalFamilies);

    [Fact]
    public async Task OwnedResources_ListEveryFamilyOwnedByTheContext_AndNothingOwnedElsewhere()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var appB = await harness.CreateApplicationContextAsync("B");
        await harness.CreateIntegrationAsync(appA.Id, "A integration");
        await harness.CreateConnectionAsync(appA.Id, "A connection");
        await harness.CreateScheduleAsync(appA.Id, "A schedule");
        await harness.CreateIntegrationAsync(appB.Id, "B integration");
        await harness.CreateConnectionAsync(appB.Id, "B connection");
        await harness.CreateScheduleAsync(appB.Id, "B schedule");
        await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id));

        result.Items.Select(i => i.Name).Should().BeEquivalentTo("A integration", "A connection", "A schedule");
        result.Items.Select(i => i.Type).Should().BeEquivalentTo(OperationalFamilies);
        result.TotalMatching.Should().Be(3);
    }

    [Fact]
    public async Task SharedConnection_ReferencedByAnApplicationIntegration_IsNotListedAsApplicationOwned()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var integration = await harness.CreateIntegrationAsync(appA.Id, "A integration");
        var shared = await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");
        await harness.BindConnectionAsync(integration.Id, shared.Id);

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("A integration");
    }

    [Fact]
    public async Task FamilyRestriction_ExcludesNotificationDefinitionsAndModules_ThatContextFilteringWouldOtherwiseIncludeOrSkip()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var integration = await harness.CreateIntegrationAsync(appA.Id, "A integration");
        await harness.CreateNotificationDefinitionAsync("A definition", NotificationDefinitionScopes.Integration, integrationId: integration.Id);
        await harness.CreateModuleAsync();

        var unrestricted = await harness.Inventory.QueryAsync(
            new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(appA.Id.Value)));
        var restricted = await harness.Inventory.QueryAsync(Owned(appA.Id));

        unrestricted.Items.Should().Contain(i => i.Type == InventoryTypeTokens.NotificationDefinition);
        restricted.Items.Should().ContainSingle().Which.Type.Should().Be(InventoryTypeTokens.Integration);
    }

    [Fact]
    public async Task SingleFamilyRestriction_ReturnsOnlyThatFamily()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "A integration");
        await harness.CreateConnectionAsync(appA.Id, "A connection");
        await harness.CreateScheduleAsync(appA.Id, "A schedule");

        var connections = await harness.Inventory.QueryAsync(Owned(appA.Id, types: [InventoryTypeTokens.Connection]));

        connections.Items.Should().ContainSingle().Which.Name.Should().Be("A connection");
    }

    [Fact]
    public async Task ArchivedApplication_StillListsTheRetiredAndDisabledResourcesItCurrentlyOwns()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var integration = await harness.CreateIntegrationAsync(appA.Id, "A integration");
        var connection = await harness.CreateConnectionAsync(appA.Id, "A connection");
        await harness.CreateScheduleAsync(appA.Id, "A schedule", enabled: false);
        await harness.RetireIntegrationAsync(integration.Id);
        await harness.RetireConnectionAsync(connection.Id);
        await harness.ResourceContexts.ArchiveAsync(appA.Id);

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id));

        result.Items.ToDictionary(i => i.Name, i => i.Status).Should().BeEquivalentTo(
            new Dictionary<string, string?>
            {
                ["A integration"] = "Retired",
                ["A connection"] = "Retired",
                ["A schedule"] = "Disabled"
            });
    }

    [Fact]
    public async Task Search_MatchesDisplayName_AndComposesWithTheFamilyRestriction()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "Payroll sync");
        await harness.CreateConnectionAsync(appA.Id, "Payroll database");
        await harness.CreateScheduleAsync(appA.Id, "Nightly export");

        var byName = await harness.Inventory.QueryAsync(Owned(appA.Id, q: "payroll"));
        var composed = await harness.Inventory.QueryAsync(Owned(appA.Id, q: "payroll", types: [InventoryTypeTokens.Connection]));
        var noMatch = await harness.Inventory.QueryAsync(Owned(appA.Id, q: "no such thing"));

        byName.Items.Select(i => i.Name).Should().BeEquivalentTo("Payroll sync", "Payroll database");
        composed.Items.Should().ContainSingle().Which.Name.Should().Be("Payroll database");
        noMatch.Items.Should().BeEmpty();
        noMatch.TotalMatching.Should().Be(0);
    }

    [Fact]
    public async Task TotalMatching_CountsEveryMatchBeforeTheTakeCap()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        for (var i = 1; i <= 4; i++)
            await harness.CreateIntegrationAsync(appA.Id, $"Integration {i}");

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id, take: 2));

        result.Items.Should().HaveCount(2);
        result.TotalMatching.Should().Be(4);
    }

    [Fact]
    public async Task UnauthorizedFamily_IsOmitted_WithoutLeakingItsRowsOrCounts()
    {
        await using var harness = await InventoryTestHarness.CreateAsync(canViewIntegrations: false);
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "A integration");
        await harness.CreateConnectionAsync(appA.Id, "A connection");

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("A connection");
        result.TotalMatching.Should().Be(1);
    }

    [Fact]
    public async Task ContextWithNoResources_ReturnsAnEmptyResult()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");

        var result = await harness.Inventory.QueryAsync(Owned(appA.Id));

        result.Items.Should().BeEmpty();
        result.TotalMatching.Should().Be(0);
    }
}
