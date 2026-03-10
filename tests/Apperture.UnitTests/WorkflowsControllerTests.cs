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
    [Fact]
    public void Start_WhenBodyContainsSecretBearingParameter_ThrowsArgumentException()
    {
        var controller = new WorkflowsController();
        var resolvedSecret = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret"),
            Version: null);
        var body = new StartWorkflowRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["apiKey"] = resolvedSecret }
        };

        Action act = () => controller.Start("workflow-1", body);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public void Start_WhenBodyHasSafeParameters_Returns501NotImplemented()
    {
        var controller = new WorkflowsController();
        var body = new StartWorkflowRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" },
            IdempotencyKey = "key-1"
        };

        var result = controller.Start("workflow-1", body);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(501);
    }

    [Fact]
    public void Start_WhenBodyIsNull_DoesNotThrow()
    {
        var controller = new WorkflowsController();

        var result = controller.Start("workflow-1", null);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(501);
    }
}
