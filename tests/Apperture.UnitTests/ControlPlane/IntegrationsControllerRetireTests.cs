using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Exception-mapping for the new <c>POST /integrations/{id}/retire</c> route, mirroring the established
/// pattern in <see cref="IntegrationsControllerReactivationGuardTests"/> for Activate/Deactivate/Update.
/// </summary>
public sealed class IntegrationsControllerRetireTests
{
    private static IntegrationsController NewController(Mock<IIntegrationManagementService> service) => new(
        service.Object,
        Mock.Of<IIntegrationReadinessService>(),
        Mock.Of<IIntegrationConfigurationRegistry>(),
        Mock.Of<IIntegrationScheduleDiagnosticsService>(),
        Mock.Of<IAttentionIncidentTracker>(),
        Mock.Of<IAttentionRoutingManagementService>(),
        Mock.Of<IPrincipalContext>(),
        TimeProvider.System,
        Mock.Of<IResourceContextService>());

    [Fact]
    public async Task Retire_WhenPlainInvalidOperationException_Returns404()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.RetireIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Integration 'i1' not found."));
        var controller = NewController(service);

        var result = await controller.Retire("i1", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Retire_WhenUnauthorized_ReturnsForbid()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.RetireIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Not authorized to update this integration."));
        var controller = NewController(service);

        var result = await controller.Retire("i1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }
}
