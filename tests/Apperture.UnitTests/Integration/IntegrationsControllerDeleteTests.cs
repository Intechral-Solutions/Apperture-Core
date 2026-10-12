using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// AUD-1.4 slice 3, work package 4b: the HTTP mapping of the audited integration delete. Each outcome is mapped from a typed
/// exception, never from message text, and nothing that is not a refusal (an audit invariant, a retirement failure, an unconfirmed
/// commit) is turned into a 4xx (AUD-1.3 D19).
/// </summary>
public sealed class IntegrationsControllerDeleteTests
{
    [Fact]
    public async Task A_completed_delete_is_204()
    {
        var result = await Delete(error: null);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task A_denied_or_stale_authorization_is_403()
    {
        var result = await Delete(new UnauthorizedAccessException("Not authorized to delete this integration."));

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task A_missing_integration_is_404()
    {
        var result = await Delete(new IntegrationNotFoundException(new IntegrationId("int-1")));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task A_blocker_keeps_its_400_with_the_operator_facing_reason()
    {
        var result = await Delete(new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.Bindings, 2));

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        Json(bad.Value).Should().Contain("Cannot delete integration: 2 binding(s) reference it.");
    }

    [Fact]
    public async Task An_undetermined_credential_is_409_with_a_code_and_names_no_key_or_secret()
    {
        var secret = Guid.NewGuid();
        var refusal = new IntegrationSettingDispositionRefusal(["Sample.Credential"], [IntegrationSecretAmbiguityReason.SettingKeyMismatch]);

        var result = await Delete(new IntegrationSettingDispositionRefusedException(refusal));

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = Json(conflict.Value);
        body.Should().Contain(IntegrationDeleteRefusalCodes.UndeterminedCredential);
        body.Should().NotContain("Sample.Credential", "the public body names no setting key");
        body.Should().NotContainEquivalentOf(secret.ToString("D"));
        body.Should().NotContain("SettingKeyMismatch", "no classification detail is exposed");
    }

    [Fact]
    public async Task A_concurrent_settings_change_is_409()
    {
        var result = await Delete(new DbUpdateConcurrencyException("An integration setting was added by another request during the delete."));

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Failures_that_are_not_refusals_reach_the_host_handler_unchanged()
    {
        var failures = new Exception[]
        {
            new AuditInvariantException("Integration deletion requires the audited unit of work."),
            new IntegrationSettingSecretRetirementException("Sample.Credential", nameof(InvalidOperationException)),
            new InvalidOperationException("The transaction was rolled back."),
            new AuditOutcomeUnknownException("evt-1"),
            new AuditCommittedResultUnavailableException("evt-1", new InvalidOperationException("read failed")),
            new AuditOperationNotCompletedException("evt-1"),
        };

        foreach (var failure in failures)
        {
            var act = () => Delete(failure);

            (await act.Should().ThrowAsync<Exception>($"{failure.GetType().Name} is not a refusal")).Which.Should().BeSameAs(failure);
        }
    }

    private static async Task<IActionResult> Delete(Exception? error)
    {
        var service = new Mock<IIntegrationManagementService>();
        var setup = service.Setup(s => s.DeleteIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()));
        if (error is null)
            setup.Returns(ValueTask.CompletedTask);
        else
            setup.Returns(ValueTask.FromException(error));

        var controller = new IntegrationsController(
            service.Object,
            Mock.Of<IIntegrationReadinessService>(),
            Mock.Of<IIntegrationConfigurationRegistry>(),
            Mock.Of<IIntegrationScheduleDiagnosticsService>(),
            Mock.Of<IAttentionIncidentTracker>(),
            Mock.Of<IAttentionRoutingManagementService>(),
            Mock.Of<IPrincipalContext>(),
            TimeProvider.System,
            Mock.Of<IResourceContextService>(),
            Mock.Of<IResourceOwnershipMoveService>(),
            UnusedIntegrationAuthorization.Create());

        return await controller.Delete("int-1", CancellationToken.None);
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);
}
