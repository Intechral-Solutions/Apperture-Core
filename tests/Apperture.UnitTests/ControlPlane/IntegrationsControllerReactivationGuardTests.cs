using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// CTX-3A carried-forward fix (Plan 1 final review, finding I1): <see cref="IntegrationsController.Update"/>
/// and <see cref="IntegrationsController.Activate"/> caught bare <see cref="InvalidOperationException"/>
/// and returned 404, so an archived-context rejection from Plan 1's Integration-activation guard read as
/// "not found" to the operator instead of the design-gate-specified 409. A
/// <see cref="ResourceContextArchivedException"/> catch, ordered before the existing plain
/// <see cref="InvalidOperationException"/> catch (since it is a subtype), now maps that specific case to
/// 409 while every other <see cref="InvalidOperationException"/> keeps returning 404 unchanged.
/// </summary>
public sealed class IntegrationsControllerReactivationGuardTests
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
        Mock.Of<IResourceContextService>(),
        Mock.Of<IResourceOwnershipMoveService>());

    private static ResourceContextArchivedException ArchivedException() => new(
        new ResourceContextId("app-a"),
        "Canvas",
        "ResourceContext 'Canvas' (app-a) is archived and cannot own new resources.");

    // -------------------------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_WhenReactivationGuardThrowsResourceContextArchived_Returns409WithMessage()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.UpdateIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<UpdateIntegrationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArchivedException());
        var controller = NewController(service);

        var result = await controller.Update("i1", new UpdateIntegrationRequestBody(DisplayName: null, State: "Active"), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = ArchivedException().Message });
    }

    [Fact]
    public async Task Update_WhenPlainInvalidOperationException_StillReturns404()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.UpdateIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<UpdateIntegrationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Integration 'i1' not found."));
        var controller = NewController(service);

        var result = await controller.Update("i1", new UpdateIntegrationRequestBody(DisplayName: null, State: "Active"), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // -------------------------------------------------------------------------------------------
    // Activate
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activate_WhenReactivationGuardThrowsResourceContextArchived_Returns409WithMessage()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.ActivateIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArchivedException());
        var controller = NewController(service);

        var result = await controller.Activate("i1", CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = ArchivedException().Message });
    }

    [Fact]
    public async Task Activate_WhenPlainInvalidOperationException_StillReturns404()
    {
        var service = new Mock<IIntegrationManagementService>();
        service
            .Setup(s => s.ActivateIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Integration 'i1' not found."));
        var controller = NewController(service);

        var result = await controller.Activate("i1", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
