using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationDefinitionQueryServiceTests
{
    [Fact]
    public async Task ListAsync_FiltersOutUnauthorizedDefinitions()
    {
        var defs = new[]
        {
            new OperationDefinition(new OperationId("op-1"), "One", "Module.A", OperationCategory.Command, null, Array.Empty<PermissionDescriptor>()),
            new OperationDefinition(new OperationId("op-2"), "Two", "Module.B", OperationCategory.Command, null, Array.Empty<PermissionDescriptor>())
        };
        var service = new OperationDefinitionQueryService(
            new StubRegistry(defs),
            new StubReadAuthz(deniedIds: new[] { "op-2" }),
            new StubStartAuthz(deniedIds: Array.Empty<string>()));

        var list = await service.ListAsync();

        list.Should().HaveCount(1);
        list[0].OperationDefinitionId.Should().Be("op-1");
    }

    [Fact]
    public async Task GetAsync_MapsCanStartAndInputSchema()
    {
        const string schema = """
        {
          "fields": [
            { "key": "name", "type": "string", "label": "Name", "required": true, "order": 0 }
          ]
        }
        """;
        var def = new OperationDefinition(new OperationId("op-1"), "One", "Module.A", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>());
        var service = new OperationDefinitionQueryService(
            new StubRegistry(new[] { def }),
            new StubReadAuthz(Array.Empty<string>()),
            new StubStartAuthz(deniedIds: new[] { "op-1" }));

        var dto = await service.GetAsync(new OperationId("op-1"));

        dto.Should().NotBeNull();
        dto!.CanStart.Should().BeFalse();
        dto.InputSchema.Should().NotBeNull();
        dto.InputSchema!.Fields.Should().ContainSingle(f => f.Key == "name" && f.Required);
    }

    private sealed class StubRegistry : IOperationRegistryService
    {
        private readonly IReadOnlyList<OperationDefinition> _defs;
        public StubRegistry(IReadOnlyList<OperationDefinition> defs) => _defs = defs;
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_defs);
        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_defs.FirstOrDefault(x => x.Id == operationId));
        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(_defs.Where(x => x.ModuleId == moduleId).ToList());
        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubReadAuthz : IOperationDefinitionReadAuthorizationService
    {
        private readonly HashSet<string> _denied;
        public StubReadAuthz(IEnumerable<string> deniedIds) => _denied = new HashSet<string>(deniedIds, StringComparer.Ordinal);
        public ValueTask EnsureCanReadDefinitionAsync(OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (_denied.Contains(operationId.Value))
                throw new UnauthorizedAccessException("deny");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubStartAuthz : IOperationStartAuthorizationService
    {
        private readonly HashSet<string> _denied;
        public StubStartAuthz(IEnumerable<string> deniedIds) => _denied = new HashSet<string>(deniedIds, StringComparer.Ordinal);
        public ValueTask EnsureCanStartOperationAsync(OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (_denied.Contains(operationId.Value))
                throw new UnauthorizedAccessException("deny");
            return ValueTask.CompletedTask;
        }
    }
}

