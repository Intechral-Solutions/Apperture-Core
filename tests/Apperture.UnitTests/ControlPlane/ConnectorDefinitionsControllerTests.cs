using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 52: Connector definitions catalog API (replaces platform-managed clients).
/// </summary>
public sealed class ConnectorDefinitionsControllerTests
{
    private static ConnectorDefinition CreateSampleDefinition() =>
        new(WellKnownConnectorDefinitionIds.PlatformCampusNexus, "Campus Nexus API", null, new Dictionary<string, string>())
        {
            Description = "Test description",
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("BaseUrl", "API base URL", false),
            ],
        };

    [Fact]
    public async Task List_WhenAuthorized_Returns200WithDefinitions()
    {
        var def = CreateSampleDefinition();
        var readService = new StubCatalogReadService([def]);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IReadOnlyList<ConnectorDefinitionCatalogDto>>().Subject;
        list.Should().ContainSingle();
        list[0].Id.Should().Be(WellKnownConnectorDefinitionIds.PlatformCampusNexus.Value);
        list[0].Name.Should().Be("Campus Nexus API");
        list[0].Fields.Should().ContainSingle(f => f.Key == "BaseUrl");
    }

    [Fact]
    public async Task List_ReturnsCatalogPayloadWithExpectedShape()
    {
        var def = CreateSampleDefinition();
        var readService = new StubCatalogReadService([def]);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IReadOnlyList<ConnectorDefinitionCatalogDto>>().Subject;
        var dto = list.Should().ContainSingle().Subject;
        dto.Id.Should().NotBeNullOrWhiteSpace();
        dto.Name.Should().NotBeNullOrWhiteSpace();
        dto.Fields.Should().NotBeNull();
        dto.Fields.Should().ContainSingle(f => f.Key == "BaseUrl" && f.DisplayName == "API base URL" && f.IsSecret == false);
        dto.Capabilities.Should().NotBeNull();
    }

    [Fact]
    public async Task List_WhenUnauthorized_Returns403Forbid()
    {
        var readService = new StubCatalogReadService(throwsUnauthorized: true);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Get_WhenFound_Returns200WithDefinition()
    {
        var def = CreateSampleDefinition();
        var readService = new StubCatalogReadService([def]);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.Get(WellKnownConnectorDefinitionIds.PlatformCampusNexus.Value, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<ConnectorDefinitionCatalogDto>().Subject;
        dto.Id.Should().Be(WellKnownConnectorDefinitionIds.PlatformCampusNexus.Value);
    }

    [Fact]
    public async Task Get_WhenNotFound_Returns404()
    {
        var readService = new StubCatalogReadService([]);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.Get("nonexistent", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_WhenUnauthorized_Returns403Forbid()
    {
        var readService = new StubCatalogReadService(throwsUnauthorized: true);
        var controller = new ConnectorDefinitionsController(readService);

        var result = await controller.Get("any-id", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    private sealed class StubCatalogReadService : IConnectorDefinitionCatalogReadService
    {
        private readonly IReadOnlyList<ConnectorDefinition> _definitions;
        private readonly bool _throwsUnauthorized;

        public StubCatalogReadService(
            IReadOnlyList<ConnectorDefinition>? definitions = null,
            bool throwsUnauthorized = false)
        {
            _definitions = definitions ?? [];
            _throwsUnauthorized = throwsUnauthorized;
        }

        public ValueTask<IReadOnlyList<ConnectorDefinition>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (_throwsUnauthorized)
                throw new UnauthorizedAccessException("Not authorized");
            return ValueTask.FromResult(_definitions);
        }

        public ValueTask<ConnectorDefinition?> GetByIdAsync(ConnectorDefinitionId id, CancellationToken cancellationToken = default)
        {
            if (_throwsUnauthorized)
                throw new UnauthorizedAccessException("Not authorized");
            var found = _definitions.FirstOrDefault(d => d.Id.Value == id.Value);
            return ValueTask.FromResult<ConnectorDefinition?>(found);
        }
    }
}
