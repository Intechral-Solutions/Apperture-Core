using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationInputValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_WhenRequiredFieldMissing_Throws()
    {
        const string schema = """
        {
          "fields": [
            { "key": "id", "type": "string", "required": true }
          ]
        }
        """;
        var svc = new OperationInputValidationService(new StubRegistry(new OperationDefinition(
            new OperationId("op-1"), "One", "M", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>())));

        Func<Task> act = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?>());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateAsync_WhenEnumFieldOutsideAllowed_Throws()
    {
        const string schema = """
        {
          "fields": [
            { "key": "mode", "type": "enum", "required": true, "allowedValues": ["a","b"] }
          ]
        }
        """;
        var svc = new OperationInputValidationService(new StubRegistry(new OperationDefinition(
            new OperationId("op-1"), "One", "M", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>())));

        Func<Task> act = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?> { ["mode"] = "x" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateAsync_WhenInputValid_DoesNotThrow()
    {
        const string schema = """
        {
          "fields": [
            { "key": "mode", "type": "enum", "required": true, "allowedValues": ["a","b"] },
            { "key": "retries", "type": "int", "required": false }
          ]
        }
        """;
        var svc = new OperationInputValidationService(new StubRegistry(new OperationDefinition(
            new OperationId("op-1"), "One", "M", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>())));

        Func<Task> act = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?> { ["mode"] = "a", ["retries"] = 1 });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenMaxLengthExceeded_Throws()
    {
        const string schema = """
        {
          "fields": [
            { "key": "name", "type": "string", "required": true, "maxLength": 3 }
          ]
        }
        """;
        var svc = new OperationInputValidationService(new StubRegistry(new OperationDefinition(
            new OperationId("op-1"), "One", "M", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>())));

        Func<Task> act = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?> { ["name"] = "toolong" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateAsync_WhenOutsideMinMax_Throws()
    {
        const string schema = """
        {
          "fields": [
            { "key": "count", "type": "int", "required": true, "min": 2, "max": 4 }
          ]
        }
        """;
        var svc = new OperationInputValidationService(new StubRegistry(new OperationDefinition(
            new OperationId("op-1"), "One", "M", OperationCategory.Command, schema, Array.Empty<PermissionDescriptor>())));

        Func<Task> actLow = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?> { ["count"] = 1 });
        await actLow.Should().ThrowAsync<ArgumentException>();
        Func<Task> actHigh = async () => await svc.ValidateAsync(new OperationId("op-1"), new Dictionary<string, object?> { ["count"] = 5 });
        await actHigh.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class StubRegistry : IOperationRegistryService
    {
        private readonly OperationDefinition _definition;
        public StubRegistry(OperationDefinition definition) => _definition = definition;
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(new[] { _definition });
        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(_definition);
        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(new[] { _definition });
        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

