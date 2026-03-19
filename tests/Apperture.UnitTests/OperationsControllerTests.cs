using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests;

/// <summary>
/// Operation start API boundary: request is built from route + body; OrchestrationSafeValidator is applied to parameters.
/// </summary>
public sealed class OperationsControllerTests
{
    private static IOperationStartAuthorizationService CreateAllowAllAuthService()
    {
        var mock = new MockOperationStartAuthorizationService();
        return mock;
    }

    private static IOperationStartDispatchService CreateDispatchService(Func<StartOperationRequest, OperationStartDispatchResult?> handler) =>
        new MockOperationStartDispatchService(handler);

    [Fact]
    public void Start_WhenBodyContainsSecretBearingParameter_ThrowsArgumentException()
    {
        var controller = new OperationsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(_ => throw new InvalidOperationException("dispatch should not be called")));

        var resolvedSecret = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret"),
            Version: null);

        var body = new StartOperationRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["apiKey"] = resolvedSecret }
        };

        Func<Task> act = () => controller.Start("operation-1", body, CancellationToken.None);

        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public async Task Start_WhenBodyHasSafeParameters_Returns202Accepted()
    {
        var controller = new OperationsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(req =>
                new OperationStartDispatchResult(
                    new OperationRunId("run-1"),
                    new OperationInstanceId("inst-1"),
                    new Apperture.Core.Domain.Operations.OperationId(req.OperationDefinitionId),
                    "requested")));

        var body = new StartOperationRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" },
            IdempotencyKey = "key-1"
        };

        var result = await controller.Start("operation-1", body, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Start_WhenBodyIsNull_DoesNotThrow()
    {
        var controller = new OperationsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(req =>
                new OperationStartDispatchResult(
                    new OperationRunId("run-1"),
                    new OperationInstanceId("inst-1"),
                    new Apperture.Core.Domain.Operations.OperationId(req.OperationDefinitionId),
                    "requested")));

        var result = await controller.Start("operation-1", null, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    private sealed class MockOperationStartAuthorizationService : IOperationStartAuthorizationService
    {
        public ValueTask EnsureCanStartOperationAsync(Apperture.Core.Domain.Operations.OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MockOperationStartDispatchService : IOperationStartDispatchService
    {
        private readonly Func<StartOperationRequest, OperationStartDispatchResult?> _handler;

        public MockOperationStartDispatchService(Func<StartOperationRequest, OperationStartDispatchResult?> handler) =>
            _handler = handler;

        public ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(request));
    }
}

