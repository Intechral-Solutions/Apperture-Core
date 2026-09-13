using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class IntegrationProceduresControllerTests
{
    [Fact]
    public async Task List_EmptyIntegrationId_Returns400()
    {
        var procedures = new Mock<IIntegrationProcedureService>();
        var controller = new IntegrationProceduresController(procedures.Object);

        var result = await controller.List("   ", CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        procedures.Verify(
            s => s.ListAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_EmptyIntegrationId_Returns400()
    {
        var procedures = new Mock<IIntegrationProcedureService>();
        var controller = new IntegrationProceduresController(procedures.Object);

        var result = await controller.Get("", "proc-1", CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        procedures.Verify(
            s => s.GetProcedureAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
