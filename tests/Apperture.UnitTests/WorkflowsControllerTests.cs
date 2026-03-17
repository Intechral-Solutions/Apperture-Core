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
/// Workflow start API boundary: request is built from route + body; OrchestrationSafeValidator is applied to parameters.
/// </summary>
public sealed class WorkflowsControllerTests
{
    private static IWorkflowStartAuthorizationService CreateAllowAllAuthService()
    {
        var mock = new MockWorkflowStartAuthorizationService();
        return mock;
    }

    private static IWorkflowStartDispatchService CreateDispatchService(Func<StartWorkflowRequest, WorkflowStartDispatchResult?> handler) =>
        new MockWorkflowStartDispatchService(handler);

    [Fact]
    public void Start_WhenBodyContainsSecretBearingParameter_ThrowsArgumentException()
    {
        var controller = new WorkflowsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(_ => throw new InvalidOperationException("dispatch should not be called")));
        var resolvedSecret = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret"),
            Version: null);
        var body = new StartWorkflowRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["apiKey"] = resolvedSecret }
        };

        Func<Task> act = () => controller.Start("workflow-1", body, CancellationToken.None);

        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public async Task Start_WhenBodyHasSafeParameters_Returns202Accepted()
    {
        var controller = new WorkflowsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(req => new WorkflowStartDispatchResult(
                new OperationRunId("run-1"),
                new WorkflowInstanceId("inst-1"),
                req.WorkflowDefinitionId,
                "requested")));
        var body = new StartWorkflowRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" },
            IdempotencyKey = "key-1"
        };

        var result = await controller.Start("workflow-1", body, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Start_WhenBodyIsNull_DoesNotThrow()
    {
        var controller = new WorkflowsController(
            CreateAllowAllAuthService(),
            CreateDispatchService(req => new WorkflowStartDispatchResult(
                new OperationRunId("run-1"),
                new WorkflowInstanceId("inst-1"),
                req.WorkflowDefinitionId,
                "requested")));

        var result = await controller.Start("workflow-1", null, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    private sealed class MockWorkflowStartAuthorizationService : IWorkflowStartAuthorizationService
    {
        public ValueTask EnsureCanStartWorkflowAsync(Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class MockWorkflowStartDispatchService : IWorkflowStartDispatchService
    {
        private readonly Func<StartWorkflowRequest, WorkflowStartDispatchResult?> _handler;

        public MockWorkflowStartDispatchService(Func<StartWorkflowRequest, WorkflowStartDispatchResult?> handler)
        {
            _handler = handler;
        }

        public ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(request));
    }
}
