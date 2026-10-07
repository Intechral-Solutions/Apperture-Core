using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class BootstrapControllerTests
{
    [Fact]
    public async Task GetStatus_returns_setup_flags()
    {
        var controller = NewController(new StubBootstrapService(BootstrapSetupStatus.FromUserCount(0)));
        var result = await controller.GetStatus(CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<BootstrapStatusResponse>().Subject;
        body.Initialized.Should().BeFalse();
        body.AllowsFirstAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task CreateFirstAdmin_when_valid_returns_created_and_leaves_the_created_row_to_the_service()
    {
        var audit = new AuditRecording();
        var userId = new UserId("user-bootstrap-001");
        var controller = NewController(
            new StubBootstrapService(
                BootstrapSetupStatus.FromUserCount(0),
                FirstAdminBootstrapResult.Success(userId)),
            audit);

        var result = await controller.CreateFirstAdmin(
            new FirstAdminRequest("Admin", "Local", "admin", "Password1!"),
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().BeOfType<FirstAdminResponse>();
        audit.Events.Should().BeEmpty("AUD-1.2: the created row commits with the user inside the bootstrap service");
    }

    [Fact]
    public async Task CreateFirstAdmin_when_already_initialized_returns_conflict_payload_and_records_an_anonymous_attempt()
    {
        var audit = new AuditRecording();
        var controller = NewController(
            new StubBootstrapService(
                BootstrapSetupStatus.FromUserCount(1),
                FirstAdminBootstrapResult.AlreadyExists),
            audit);

        var result = await controller.CreateFirstAdmin(
            new FirstAdminRequest("Admin", "Local", "admin", "Password1!"),
            CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = conflict.Value.Should().BeOfType<BootstrapErrorResponse>().Subject;
        body.Code.Should().Be(BootstrapErrorCodes.AlreadyInitialized);
        body.Initialized.Should().BeTrue();
        var (refused, actor) = audit.Events.Should().ContainSingle().Subject;
        refused.Action.Should().Be("bootstrap.first_admin.rejected_already_initialized");
        refused.Definition.WriteClass.Should().Be(AuditWriteClass.Attempt);
        actor.Should().Be(AuditActor.Anonymous);
    }

    [Fact]
    public async Task CreateFirstAdmin_when_invalid_request_returns_bad_request_and_records_the_reason_never_the_request()
    {
        var audit = new AuditRecording();
        var controller = NewController(new StubBootstrapService(BootstrapSetupStatus.FromUserCount(0)), audit);

        var result = await controller.CreateFirstAdmin(
            new FirstAdminRequest("", "Local", "admin", "Secret-Password-1"),
            CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = badRequest.Value.Should().BeOfType<BootstrapErrorResponse>().Subject;
        body.Code.Should().Be(BootstrapErrorCodes.InvalidRequest);
        body.Initialized.Should().BeFalse();
        var refused = audit.Events.Should().ContainSingle().Subject.Event;
        refused.Action.Should().Be("bootstrap.first_admin.rejected_invalid_request");
        refused.Fields["reason"].Should().Be("missing_fields");
        refused.Fields.Values.Should().NotContain("Secret-Password-1").And.NotContain("admin");
    }

    [Fact]
    public async Task A_refusal_row_that_cannot_be_written_never_changes_the_response()
    {
        var audit = new AuditRecording { FailWrites = true };
        var controller = NewController(
            new StubBootstrapService(BootstrapSetupStatus.FromUserCount(1), FirstAdminBootstrapResult.AlreadyExists),
            audit);

        var result = await controller.CreateFirstAdmin(new FirstAdminRequest("Admin", "Local", "admin"), CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    private static BootstrapController NewController(
        IFirstAdminBootstrapService bootstrap,
        AuditRecording? audit = null) =>
        new(bootstrap, audit ?? new AuditRecording());

    private sealed class StubBootstrapService : IFirstAdminBootstrapService
    {
        private readonly BootstrapSetupStatus _status;
        private readonly FirstAdminBootstrapResult? _createResult;

        public StubBootstrapService(BootstrapSetupStatus status, FirstAdminBootstrapResult? createResult = null)
        {
            _status = status;
            _createResult = createResult;
        }

        public Task<BootstrapSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
            string displayName,
            IdentitySource source,
            string externalId,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_createResult ?? FirstAdminBootstrapResult.AlreadyExists);
    }
}
