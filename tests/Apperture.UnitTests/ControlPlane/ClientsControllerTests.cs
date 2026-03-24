using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;
using Apperture.Core.Domain.Connector;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 51: Clients API boundary.
/// </summary>
public sealed class ClientsControllerTests
{
    private static ClientDefinition CreateSampleDefinition() =>
        new(
            new ClientDefinitionId("test-client-1"),
            "Test Client",
            "A test client definition",
            new ConnectorDefinitionId("Apperture.Platform.CampusNexus"),
            null,
            false,
            [
                new ClientOperationMetadata("Op1", "Operation 1", null, true, true, null)
            ]);

    [Fact]
    public async Task List_WhenAuthorized_Returns200WithDefinitions()
    {
        var def = CreateSampleDefinition();
        var readService = new StubClientDefinitionReadService([def]);
        var controller = new ClientsController(readService);

        var result = await controller.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IReadOnlyList<ClientDefinitionDto>>().Subject;
        list.Should().ContainSingle();
        list[0].Id.Should().Be("test-client-1");
        list[0].DisplayName.Should().Be("Test Client");
        list[0].Operations.Should().ContainSingle(o => o.OperationKey == "Op1");
    }

    [Fact]
    public async Task List_WhenUnauthorized_Returns403Forbid()
    {
        var readService = new StubClientDefinitionReadService(throwsUnauthorized: true);
        var controller = new ClientsController(readService);

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Get_WhenFound_Returns200WithDefinition()
    {
        var def = CreateSampleDefinition();
        var readService = new StubClientDefinitionReadService([def]);
        var controller = new ClientsController(readService);

        var result = await controller.Get("test-client-1", CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<ClientDefinitionDto>().Subject;
        dto.Id.Should().Be("test-client-1");
    }

    [Fact]
    public async Task Get_WhenNotFound_Returns404()
    {
        var readService = new StubClientDefinitionReadService([]);
        var controller = new ClientsController(readService);

        var result = await controller.Get("nonexistent", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_WhenUnauthorized_Returns403Forbid()
    {
        var readService = new StubClientDefinitionReadService(throwsUnauthorized: true);
        var controller = new ClientsController(readService);

        var result = await controller.Get("any-id", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    private sealed class StubClientDefinitionReadService : IClientDefinitionReadService
    {
        private readonly IReadOnlyList<ClientDefinition> _definitions;
        private readonly bool _throwsUnauthorized;

        public StubClientDefinitionReadService(
            IReadOnlyList<ClientDefinition>? definitions = null,
            bool throwsUnauthorized = false)
        {
            _definitions = definitions ?? [];
            _throwsUnauthorized = throwsUnauthorized;
        }

        public ValueTask<IReadOnlyList<ClientDefinition>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (_throwsUnauthorized)
                throw new UnauthorizedAccessException("Not authorized");
            return ValueTask.FromResult(_definitions);
        }

        public ValueTask<ClientDefinition?> GetByIdAsync(ClientDefinitionId id, CancellationToken cancellationToken = default)
        {
            if (_throwsUnauthorized)
                throw new UnauthorizedAccessException("Not authorized");
            var found = _definitions.FirstOrDefault(d => d.Id.Value == id.Value);
            return ValueTask.FromResult<ClientDefinition?>(found);
        }
    }
}
