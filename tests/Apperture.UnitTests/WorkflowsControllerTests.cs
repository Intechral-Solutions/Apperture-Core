using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
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

    [Fact]
    public void Start_WhenBodyContainsSecretBearingParameter_ThrowsArgumentException()
    {
        var controller = new WorkflowsController(CreateAllowAllAuthService());
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
    public async Task Start_WhenBodyHasSafeParameters_Returns501NotImplemented()
    {
        var controller = new WorkflowsController(CreateAllowAllAuthService());
        var body = new StartWorkflowRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" },
            IdempotencyKey = "key-1"
        };

        var result = await controller.Start("workflow-1", body, CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(501);
    }

    [Fact]
    public async Task Start_WhenBodyIsNull_DoesNotThrow()
    {
        var controller = new WorkflowsController(CreateAllowAllAuthService());

        var result = await controller.Start("workflow-1", null, CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(501);
    }

    private sealed class MockWorkflowStartAuthorizationService : IWorkflowStartAuthorizationService
    {
        public ValueTask EnsureCanStartWorkflowAsync(Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
