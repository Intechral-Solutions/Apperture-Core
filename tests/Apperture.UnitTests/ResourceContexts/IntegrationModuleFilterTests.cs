using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// HSK-A Task 2: the Integrations list path (repository and service) accepts an optional
/// <c>moduleId</c> filter, pushed to SQL and composable (AND) with the existing
/// <c>resourceContextId</c> filter, exercised over the same production classes as
/// <see cref="ResourceOwnershipTests"/> (<see cref="ResourceContextOwnershipTestHarness"/>).
/// The <c>moduleId</c>-alone case here is the same shape the Module detail page's "Integrations using
/// this module" section relies on — that call site deliberately passes <c>resourceContextId: null</c>
/// (confirmed intentional: Module is a system/catalog concept, not View/Context-scoped). Do not
/// "fix" that call site to consume the shell View/Context filter.
/// </summary>
public sealed class IntegrationModuleFilterTests
{
    // -------------------------------------------------------------------------------------------
    // Service level (IIntegrationManagementService.ListIntegrationsAsync)
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListIntegrationsAsync_WithModuleIdAlone_ReturnsOnlyThatModule()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Alpha", "Module.A"));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Beta", "Module.B"));

        var result = await h.Integrations.ListIntegrationsAsync(resourceContextId: null, moduleId: "Module.A");

        result.Should().ContainSingle(i => i.DisplayName == "Alpha");
    }

    [Fact]
    public async Task ListIntegrationsAsync_WithResourceContextIdAlone_ReturnsOnlyThatContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var contextA = await h.ResourceContexts.CreateApplicationContextAsync("Context A");
        var contextB = await h.ResourceContexts.CreateApplicationContextAsync("Context B");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Alpha", "Module.A", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Beta", "Module.A", ResourceContextId: contextB.Id));

        var result = await h.Integrations.ListIntegrationsAsync(resourceContextId: contextA.Id, moduleId: null);

        result.Should().ContainSingle(i => i.DisplayName == "Alpha");
    }

    [Fact]
    public async Task ListIntegrationsAsync_WithBothFilters_ReturnsOnlyIntegrationsMatchingBoth()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var contextA = await h.ResourceContexts.CreateApplicationContextAsync("Context A");
        var contextB = await h.ResourceContexts.CreateApplicationContextAsync("Context B");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Match", "Module.A", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("WrongModule", "Module.B", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("WrongContext", "Module.A", ResourceContextId: contextB.Id));

        var result = await h.Integrations.ListIntegrationsAsync(resourceContextId: contextA.Id, moduleId: "Module.A");

        result.Should().ContainSingle(i => i.DisplayName == "Match");
    }

    [Fact]
    public async Task ListIntegrationsAsync_WithNeitherFilter_ReturnsEverything()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var contextA = await h.ResourceContexts.CreateApplicationContextAsync("Context A");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Alpha", "Module.A", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Beta", "Module.B"));

        var result = await h.Integrations.ListIntegrationsAsync(resourceContextId: null, moduleId: null);

        result.Should().Contain(i => i.DisplayName == "Alpha").And.Contain(i => i.DisplayName == "Beta");
    }

    [Fact]
    public async Task ListIntegrationsAsync_WithModuleIdMatchingNothing_ReturnsEmptyNotError()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Alpha", "Module.A"));

        var result = await h.Integrations.ListIntegrationsAsync(resourceContextId: null, moduleId: "Module.DoesNotExist");

        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------------------------
    // Repository level (IIntegrationRepository.GetVisibleAsync) -- proves the SQL-backed
    // implementation itself composes both filters, not merely the service wrapper around it.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetVisibleAsync_WithModuleIdAlone_ReturnsOnlyThatModule()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Alpha", "Module.A"));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Beta", "Module.B"));

        var result = await h.IntegrationRepository.GetVisibleAsync(ScopeKeyConstants.Instance, resourceContextId: null, moduleId: "Module.A");

        result.Should().ContainSingle(i => i.DisplayName == "Alpha");
    }

    [Fact]
    public async Task GetVisibleAsync_WithBothFilters_ReturnsOnlyIntegrationsMatchingBoth()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var contextA = await h.ResourceContexts.CreateApplicationContextAsync("Context A");
        var contextB = await h.ResourceContexts.CreateApplicationContextAsync("Context B");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Match", "Module.A", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("WrongModule", "Module.B", ResourceContextId: contextA.Id));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("WrongContext", "Module.A", ResourceContextId: contextB.Id));

        var result = await h.IntegrationRepository.GetVisibleAsync(ScopeKeyConstants.Instance, contextA.Id, "Module.A");

        result.Should().ContainSingle(i => i.DisplayName == "Match");
    }
}
