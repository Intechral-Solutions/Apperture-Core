using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Connector;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Design gate §8.2: movement must never become a field on an ordinary update DTO. The assertions cover
/// both the application-layer request records **and** the API request bodies, because the API body is the
/// boundary an operator actually reaches — an ownership field added there would be reachable even if the
/// application-layer record stayed clean.
/// </summary>
public sealed class ResourceMoveDtoImmutabilityTests
{
    [Fact]
    public void UpdateIntegrationRequest_has_no_ResourceContextId()
    {
        typeof(UpdateIntegrationRequest).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionRequest_has_no_ResourceContextId()
    {
        typeof(UpdateConnectionRequest).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateIntegrationRequestBody_has_no_ResourceContextId()
    {
        typeof(UpdateIntegrationRequestBody).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionRequestBody_has_no_ResourceContextId()
    {
        typeof(UpdateConnectionRequestBody).GetProperty("ResourceContextId").Should().BeNull();
    }

    /// <summary>
    /// The Schedule body gets both assertions. <c>ResourceContextId</c> for the same reason as the other
    /// two families; <c>IntegrationId</c> because <c>SqlScheduleRepository.UpdateDefinitionAsync</c>
    /// genuinely does write <c>e.IntegrationId</c> while deliberately never writing
    /// <c>ResourceContextId</c> — so a body field that re-attached a standalone Schedule to an Integration
    /// would create permanent <c>schedule_context_inconsistent</c> drift that nothing can repair and that
    /// permanently blocks moving that Integration. Verified at review time: the body has neither today.
    /// </summary>
    [Fact]
    public void UpdateScheduleRequestBody_has_neither_ResourceContextId_nor_IntegrationId()
    {
        typeof(UpdateScheduleRequestBody).GetProperty("ResourceContextId").Should().BeNull();
        typeof(UpdateScheduleRequestBody).GetProperty("IntegrationId").Should().BeNull(
            "re-attaching a Schedule through an ordinary update would rewrite IntegrationId without rewriting ResourceContextId");
    }

    /// <summary>
    /// Part C: behavioral counterpart to the reflection assertions above. There is no application-layer
    /// <c>UpdateScheduleRequest</c> record -- <see cref="Apperture.Core.Application.Services.IScheduleManagementService.UpdateScheduleAsync"/>
    /// takes a whole <see cref="Apperture.Core.Domain.Scheduling.ScheduleDefinition"/>, which genuinely
    /// carries both <c>ResourceContextId</c> and <c>IntegrationId</c>. So the Schedule family's ownership
    /// boundary cannot be pinned by reflection the way Integration's and Connection's Update*Request(Body)
    /// records are above; it must be pinned behaviorally instead. This test reproduces exactly the
    /// guarantee <c>SqlScheduleRepository.UpdateDefinitionAsync</c> documents in its own inline comment:
    /// "Ownership is established at create time. CTX-1 has no move operation, so UpdateDefinitionAsync
    /// deliberately does not rewrite ResourceContextId."
    /// </summary>
    [Fact]
    public async Task UpdateScheduleAsync_does_not_move_ownership_even_when_the_definition_carries_a_different_ResourceContextId()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var sourceContext = await h.CreateApplicationAsync("CampusNexus");
        var targetContext = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(sourceContext.Id);

        // Tamper with ResourceContextId as if a careless caller round-tripped the definition after
        // reassigning it client-side, and change an innocuous field (Name) alongside it so the update is
        // genuinely applied -- if UpdateScheduleAsync silently no-op'd entirely, this test would pass
        // vacuously without the Name assertion below.
        var tampered = schedule with { ResourceContextId = targetContext.Id, Name = "Renamed" };
        await h.Schedules.UpdateScheduleAsync(tampered);

        var persisted = await h.ScheduleRepository.GetDefinitionAsync(schedule.Id)
            ?? throw new InvalidOperationException($"Schedule '{schedule.Id.Value}' not found after update.");

        persisted.ResourceContextId.Should().Be(sourceContext.Id,
            "SqlScheduleRepository.UpdateDefinitionAsync deliberately does not rewrite ResourceContextId -- ownership is established at create time and CTX-1 has no move operation (see that method's inline comment)");
        persisted.Name.Should().Be("Renamed",
            "the innocuous field change must actually land, proving this update genuinely ran and this test is not vacuously passing because nothing was written at all");
    }
}
