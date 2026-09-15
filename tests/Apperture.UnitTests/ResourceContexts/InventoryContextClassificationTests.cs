using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 4, Task 9 (§5.5, A12-A14): Config Inventory's Context filter and column. Unlike the four
/// scoped lists (Integrations/Connections/Schedules/Runs), Inventory's Context filter is local to this
/// page and independent of the shell's View selector -- these tests never touch ResourceContextViewState.
/// </summary>
public sealed class InventoryContextClassificationTests
{
    [Fact]
    public async Task Resources_ClassifyByCurrentOwnership_NeverNotRecorded()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var app = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(app.Id, "Integration A");
        await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");
        await harness.CreateModuleAsync();

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));

        result.Items.Single(i => i.Name == "Integration A").ResourceContextClassification.Should().Be("application");
        result.Items.Single(i => i.Name == "Shared connection").ResourceContextClassification.Should().Be("shared");
        result.Items.Single(i => i.Type == InventoryTypeTokens.Module).ResourceContextClassification.Should().Be("system");
        result.Items.Should().NotContain(i => i.ResourceContextClassification == "notRecorded");
    }

    [Fact]
    public async Task NotificationDefinition_IntegrationScoped_DerivesFromItsIntegration_EvenWithoutIntegrationViewCapability()
    {
        await using var harness = await InventoryTestHarness.CreateAsync(canViewIntegrations: false);
        var app = await harness.CreateApplicationContextAsync("A");
        var integration = await harness.CreateIntegrationAsync(app.Id, "Integration A");
        await harness.CreateNotificationDefinitionAsync("Integration def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);
        await harness.CreateNotificationDefinitionAsync("Platform def", NotificationDefinitionScopes.Platform);
        await harness.CreateNotificationDefinitionAsync("Module def", NotificationDefinitionScopes.Module, moduleId: "Sample.HelloWorld");

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));

        result.Items.Single(i => i.Name == "Integration def").ResourceContextId.Should().Be(app.Id.Value);
        result.Items.Single(i => i.Name == "Platform def").ResourceContextClassification.Should().Be("system");
        result.Items.Single(i => i.Name == "Module def").ResourceContextClassification.Should().Be("system");
    }

    [Fact]
    public async Task Audit_NullContext_ClassifiesByActionPrefix_NotByCategory()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        await harness.SeedAuditRecordAsync("platform.settings.updated", resourceContextId: null);
        await harness.SeedAuditRecordAsync("emergency.access.designated", resourceContextId: null);
        await harness.SeedAuditRecordAsync("entra.access_request.approved", resourceContextId: null);
        await harness.SeedAuditRecordAsync("auth.login", resourceContextId: null);
        await harness.SeedAuditRecordAsync("module.lifecycle.enabled", resourceContextId: null);
        await harness.SeedAuditRecordAsync("recovery.retry.requested", resourceContextId: null);
        await harness.SeedAuditRecordAsync("execution.completed", resourceContextId: null);
        await harness.SeedAuditRecordAsync("integration.settings.updated", resourceContextId: null);

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));

        Classification(result, "platform.settings.updated").Should().Be("system");
        Classification(result, "emergency.access.designated").Should().Be("system");
        Classification(result, "entra.access_request.approved").Should().Be("system");
        Classification(result, "auth.login").Should().Be("system");
        Classification(result, "module.lifecycle.enabled").Should().Be("system");
        Classification(result, "recovery.retry.requested").Should().Be("notRecorded");
        Classification(result, "execution.completed").Should().Be("notRecorded");
        Classification(result, "integration.settings.updated").Should().Be("notRecorded");
    }

    [Fact]
    public async Task Audit_WithContext_ClassifiesFromIdRegardlessOfAction()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var app = await harness.CreateApplicationContextAsync("A");
        await harness.SeedAuditRecordAsync("resource_context.archived", resourceContextId: app.Id.Value);
        await harness.SeedAuditRecordAsync("integration.settings.updated", resourceContextId: ResourceContextIds.SharedValue);

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));

        Classification(result, "resource_context.archived").Should().Be("application");
        Classification(result, "integration.settings.updated").Should().Be("shared");
    }

    [Fact]
    public async Task Run_NullContext_IsNotRecorded_AndSystemFilterNeverReturnsRuns()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        await harness.SeedOperationRunAsync(resourceContextId: null);

        var all = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));
        var system = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity, Context: InventoryContextFilter.System));

        all.Items.Single(i => i.Type == InventoryTypeTokens.Run).ResourceContextClassification.Should().Be("notRecorded");
        system.Items.Should().NotContain(i => i.Type == InventoryTypeTokens.Run);
    }

    [Fact]
    public async Task ContextFilter_ExcludesNonMatchingRows_NoMerging()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "In A");
        await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");

        var underA = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(appA.Id.Value)));

        underA.Items.Should().ContainSingle(i => i.Name == "In A");
        underA.Items.Should().NotContain(i => i.Name == "Shared connection");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("notRecorded")]
    [InlineData("system")]
    public async Task CappedActivitySources_KeepMatchingRowsDiscoverable_BeyondTheFirst100(string kind)
    {
        // Each case puts 150 newer non-matching rows in front of one older matching row in a source capped
        // at 100. Without the Context predicate inside the source query, the matching row would be cut
        // before classification and the result would be falsely empty.
        await using var harness = await InventoryTestHarness.CreateAsync();
        var target = await harness.CreateApplicationContextAsync("Target");
        var noise = await harness.CreateApplicationContextAsync("Noise");
        var now = harness.Clock.GetUtcNow();
        InventoryContextFilter filter;

        switch (kind)
        {
            case "context":
                await harness.SeedOperationRunAsync(resourceContextId: target.Id.Value, createdAt: now.AddHours(-2), name: "match");
                for (var i = 0; i < 150; i++)
                    await harness.SeedOperationRunAsync(resourceContextId: noise.Id.Value, createdAt: now.AddMinutes(-i));
                filter = InventoryContextFilter.ForContext(target.Id.Value);
                break;
            case "notRecorded":
                await harness.SeedOperationRunAsync(resourceContextId: null, createdAt: now.AddHours(-2), name: "match");
                for (var i = 0; i < 150; i++)
                    await harness.SeedOperationRunAsync(resourceContextId: noise.Id.Value, createdAt: now.AddMinutes(-i));
                filter = InventoryContextFilter.NotRecorded;
                break;
            default:
                await harness.SeedAuditRecordAsync("auth.login", resourceContextId: null, at: now.AddHours(-2), summary: "match");
                for (var i = 0; i < 150; i++)
                    await harness.SeedAuditRecordAsync("execution.completed", resourceContextId: noise.Id.Value, at: now.AddMinutes(-i));
                filter = InventoryContextFilter.System;
                break;
        }

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity, Context: filter, Take: 100));

        result.Items.Should().Contain(i => i.Name.Contains("match"));
    }

    [Fact]
    public async Task NotificationDefinitions_ContextFilter_ReachesBeyondTheFirst100ByName()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var target = await harness.CreateApplicationContextAsync("Target");
        var integration = await harness.CreateIntegrationAsync(target.Id, "Target integration");
        for (var i = 0; i < 150; i++)
            await harness.CreateNotificationDefinitionAsync($"A platform def {i:D3}", NotificationDefinitionScopes.Platform);
        await harness.CreateNotificationDefinitionAsync("Z target def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);

        var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(target.Id.Value)));

        result.Items.Should().Contain(i => i.Name == "Z target def");
    }

    [Fact]
    public async Task NotificationDefinition_WhoseIntegrationWasDeleted_IsUnknownContext_OnlyUnderAll()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var app = await harness.CreateApplicationContextAsync("A");
        var integration = await harness.CreateIntegrationAsync(app.Id, "Deleted integration");
        await harness.CreateNotificationDefinitionAsync("Orphan def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);
        await harness.HardDeleteIntegrationRowAsync(integration.Id);

        var all = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));
        var system = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.System));
        var underA = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(app.Id.Value)));

        all.Items.Single(i => i.Name == "Orphan def").ResourceContextClassification.Should().Be("unknown");
        system.Items.Should().NotContain(i => i.Name == "Orphan def");
        underA.Items.Should().NotContain(i => i.Name == "Orphan def");
    }

    private static string Classification(InventoryQueryResult result, string action) =>
        result.Items.Single(i => i.Name.Contains(action)).ResourceContextClassification;
}
