using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// AUD-1.4 slice 5: the application routes no longer report a programming error of an audited write (an audit invariant) as a
/// refusal, and an archive that waited too long for the ownership gate is the retryable conflict every other ownership route
/// answers, not a 500.
/// </summary>
public sealed class ResourceContextsControllerAuditMappingTests
{
    private static readonly ResourceContext Context = new(
        new ResourceContextId("app-a"), "Canvas", "canvas", ResourceContextKinds.Application, ResourceContextStates.Active,
        IsReserved: false, Description: null, CreatedAtUtc: DateTimeOffset.UnixEpoch, UpdatedAtUtc: DateTimeOffset.UnixEpoch);

    private static (ResourceContextsController Controller, Mock<IResourceContextManagementService> Management) NewController()
    {
        var management = new Mock<IResourceContextManagementService>();
        management.Setup(m => m.GetAsync(Context.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Context);
        return (new ResourceContextsController(management.Object, Mock.Of<IPrincipalContext>(), Mock.Of<IAuthorizationEvaluationService>()), management);
    }

    private static readonly AuditInvariantException Invariant = new("The audited operation expected a row and staged none.");

    [Fact]
    public async Task Archive_whenTheGateIsBusy_IsARetryable409WithTheStableCode()
    {
        var (controller, management) = NewController();
        management.Setup(m => m.ArchiveAsync(Context.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceOwnershipBusyException("Another ownership operation is in progress."));

        var result = await controller.Archive("app-a", CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = "Another ownership operation is in progress.", code = ResourceOwnershipBusyException.BusyCode });
    }

    [Fact]
    public async Task An_audit_invariant_is_never_reported_as_a_refusal_by_any_mutation_route()
    {
        var (controller, management) = NewController();
        management.Setup(m => m.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(Invariant);
        management.Setup(m => m.UpdateAsync(Context.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(Invariant);
        management.Setup(m => m.ArchiveAsync(Context.Id, It.IsAny<CancellationToken>())).ThrowsAsync(Invariant);
        management.Setup(m => m.UnarchiveAsync(Context.Id, It.IsAny<CancellationToken>())).ThrowsAsync(Invariant);

        await FluentActions.Awaiting(() => controller.Create(new CreateResourceContextRequest("Canvas", null, null), CancellationToken.None))
            .Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => controller.Update("app-a", new UpdateResourceContextRequest("x", null, null), CancellationToken.None))
            .Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => controller.Archive("app-a", CancellationToken.None))
            .Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => controller.Unarchive("app-a", CancellationToken.None))
            .Should().ThrowAsync<AuditInvariantException>();
    }

    [Fact]
    public async Task A_refusal_that_is_not_an_audit_invariant_keeps_its_established_status()
    {
        var (controller, management) = NewController();
        management.Setup(m => m.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ResourceContext slug 'canvas' is already in use."));
        management.Setup(m => m.UpdateAsync(Context.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("nope"));

        (await controller.Create(new CreateResourceContextRequest("Canvas", null, null), CancellationToken.None)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.Update("app-a", new UpdateResourceContextRequest("x", null, null), CancellationToken.None)).Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
