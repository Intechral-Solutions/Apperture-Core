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
}
