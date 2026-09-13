using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Ownership semantics for the three context-owned resources: Integrations, Connections and
/// ScheduleDefinitions.
/// </summary>
public sealed class ResourceOwnershipTests
{
    // -------------------------------------------------------------------------------------------
    // Integrations
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Integration_DefaultsToShared_WhenNoContextSupplied()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var created = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", "Sample.Mod"));

        created.ResourceContextId.Value.Should().Be(ResourceContextIds.SharedValue);
        (await h.IntegrationRepository.GetByIdAsync(created.Id))!.ResourceContextId.Value.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task Integration_AcceptsAnExplicitActiveContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");

        var created = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));

        created.ResourceContextId.Value.Should().Be(app.Id.Value);
    }

    [Fact]
    public async Task Integration_RejectsAnUnknownContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var act = async () => await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: new ResourceContextId("nope")));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*not found*");
    }

    [Fact]
    public async Task Integration_RejectsAnArchivedContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await h.ResourceContexts.ArchiveAsync(app.Id);

        var act = async () => await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*archived*");
    }

    /// <summary>
    /// ResourceContextId is ownership; ScopeKey is authorization. Setting one must not disturb the other.
    /// </summary>
    [Fact]
    public async Task Integration_ScopeKeyIsUnaffectedByResourceContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");

        var created = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ScopeKey: "environment:prod", ResourceContextId: app.Id));

        created.ScopeKey.Should().Be("environment:prod");
        created.GetIdentity().ScopeKey.Should().Be("environment:prod");
        created.ResourceContextId.Value.Should().Be(app.Id.Value);
    }

    // -------------------------------------------------------------------------------------------
    // Connections
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Connection_DefaultsToShared_WhenNoContextSupplied()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var created = await h.Connections.CreateConnectionAsync(NewConnectionRequest());

        created.ResourceContextId.Value.Should().Be(ResourceContextIds.SharedValue);
        (await h.ConnectionRepository.GetByIdAsync(created.Id))!.ResourceContextId.Value.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task Connection_AcceptsAnExplicitActiveContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");

        var created = await h.Connections.CreateConnectionAsync(NewConnectionRequest(app.Id));

        created.ResourceContextId.Value.Should().Be(app.Id.Value);
    }

    [Fact]
    public async Task Connection_RejectsAnUnknownContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var act = async () => await h.Connections.CreateConnectionAsync(NewConnectionRequest(new ResourceContextId("nope")));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*not found*");
    }

    [Fact]
    public async Task Connection_RejectsAnArchivedContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await h.ResourceContexts.ArchiveAsync(app.Id);

        var act = async () => await h.Connections.CreateConnectionAsync(NewConnectionRequest(app.Id));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*archived*");
    }

    /// <summary>
    /// Ownership is a namespace, not a secret boundary. Creating a context-owned Connection must not
    /// provision, copy or move any secret material.
    /// </summary>
    [Fact]
    public async Task Connection_OwnershipDoesNotTouchSecretStore()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var existingSecretRef = Guid.NewGuid().ToString();

        var created = await h.Connections.CreateConnectionAsync(
            NewConnectionRequest(app.Id) with { SecretRefs = new Dictionary<string, string> { ["Token"] = existingSecretRef } });

        created.ResourceContextId.Value.Should().Be(app.Id.Value);
        created.SecretRefs.Should().ContainKey("Token").WhoseValue.Should().Be(existingSecretRef);
        h.SecretStore.VerifyNoOtherCalls();
    }

    // -------------------------------------------------------------------------------------------
    // Schedules
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task StandaloneSchedule_DefaultsToShared_WhenNoContextSupplied()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var created = await h.Schedules.CreateScheduleAsync(NewSchedule("s-standalone"));

        created.ResourceContextId!.Value.Should().Be(ResourceContextIds.SharedValue);
        (await h.ScheduleRepository.GetDefinitionAsync(created.Id))!.ResourceContextId!.Value
            .Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task StandaloneSchedule_AcceptsAnExplicitActiveContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");

        var created = await h.Schedules.CreateScheduleAsync(NewSchedule("s-standalone") with { ResourceContextId = app.Id });

        created.ResourceContextId!.Value.Should().Be(app.Id.Value);
    }

    [Fact]
    public async Task StandaloneSchedule_RejectsAnArchivedContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await h.ResourceContexts.ArchiveAsync(app.Id);

        var act = async () => await h.Schedules.CreateScheduleAsync(NewSchedule("s-standalone") with { ResourceContextId = app.Id });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*archived*");
    }

    /// <summary>
    /// The rule the design gate insisted on: an Integration-attached Schedule inherits its
    /// Integration's ResourceContext. It is never blindly defaulted to Shared.
    /// </summary>
    [Fact]
    public async Task IntegrationSchedule_InheritsTheIntegrationsContext_WhenNoneSupplied()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));

        var created = await h.Schedules.CreateScheduleAsync(NewSchedule("s-owned", integration.Id));

        created.ResourceContextId!.Value.Should().Be(app.Id.Value);
        created.ResourceContextId.Value.Should().NotBe(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task IntegrationSchedule_AcceptsAnExplicitContextThatMatchesTheIntegration()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));

        var created = await h.Schedules.CreateScheduleAsync(
            NewSchedule("s-owned", integration.Id) with { ResourceContextId = app.Id });

        created.ResourceContextId!.Value.Should().Be(app.Id.Value);
    }

    [Fact]
    public async Task IntegrationSchedule_RejectsAnExplicitContextThatDiffersFromTheIntegration()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var appB = await h.ResourceContexts.CreateApplicationContextAsync("Application B");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: appA.Id));

        var act = async () => await h.Schedules.CreateScheduleAsync(
            NewSchedule("s-owned", integration.Id) with { ResourceContextId = appB.Id });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*does not match the owning Integration*");
    }

    /// <summary>Shared is not a free pass: an explicit Shared on an Application Integration is a mismatch.</summary>
    [Fact]
    public async Task IntegrationSchedule_RejectsExplicitShared_WhenTheIntegrationIsInAnApplication()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));

        var act = async () => await h.Schedules.CreateScheduleAsync(
            NewSchedule("s-owned", integration.Id) with { ResourceContextId = ResourceContextIds.Shared });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*does not match the owning Integration*");
    }

    [Fact]
    public async Task IntegrationSchedule_RejectsAnUnknownIntegration()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var act = async () => await h.Schedules.CreateScheduleAsync(NewSchedule("s-owned", new IntegrationId("missing")));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Integration missing not found*");
    }

    // -------------------------------------------------------------------------------------------
    // Optional context filtering
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListsWithoutAContextFilter_ReturnTheSameGlobalSets()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Shared Integration", "Sample.Mod"));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("App Integration", "Sample.Mod", ResourceContextId: app.Id));
        await h.Connections.CreateConnectionAsync(NewConnectionRequest());
        await h.Connections.CreateConnectionAsync(NewConnectionRequest(app.Id));

        (await h.Integrations.ListIntegrationsAsync()).Should().HaveCount(2);
        (await h.Connections.ListConnectionsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ListsWithAContextFilter_ReturnOnlyThatContext()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Shared Integration", "Sample.Mod"));
        var owned = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("App Integration", "Sample.Mod", ResourceContextId: app.Id));
        await h.Connections.CreateConnectionAsync(NewConnectionRequest());
        var ownedConnection = await h.Connections.CreateConnectionAsync(NewConnectionRequest(app.Id));
        await h.Schedules.CreateScheduleAsync(NewSchedule("s-shared"));
        var ownedSchedule = await h.Schedules.CreateScheduleAsync(NewSchedule("s-owned", owned.Id));

        (await h.Integrations.ListIntegrationsAsync(app.Id)).Should().ContainSingle()
            .Which.Id.Value.Should().Be(owned.Id.Value);
        (await h.Connections.ListConnectionsAsync(app.Id)).Should().ContainSingle()
            .Which.Id.Value.Should().Be(ownedConnection.Id.Value);
        (await h.Schedules.ListDefinitionsAsync(app.Id)).Should().ContainSingle()
            .Which.Id.Value.Should().Be(ownedSchedule.Id.Value);

        (await h.Integrations.ListIntegrationsAsync(ResourceContextIds.Shared)).Should().ContainSingle();
        (await h.Integrations.ListIntegrationsAsync(new ResourceContextId("unknown"))).Should().BeEmpty();
    }

    private static CreateConnectionRequest NewConnectionRequest(ResourceContextId? resourceContextId = null) =>
        new(
            new ConnectorDefinitionId("test.connector"),
            "Test Connection",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: resourceContextId);

    private static ScheduleDefinition NewSchedule(string id, IntegrationId? integrationId = null) =>
        new(
            new ScheduleId(id),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            integrationId);
}
