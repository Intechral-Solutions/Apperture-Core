using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
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
    public async Task CreateFirstAdmin_when_valid_returns_created_and_audits_success()
    {
        var audit = new CapturingAuditService();
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
        audit.Records.Should().ContainSingle(r => r.Action == "bootstrap.first_admin.created");
    }

    [Fact]
    public async Task CreateFirstAdmin_when_already_initialized_returns_conflict_payload_and_audits_rejection()
    {
        var audit = new CapturingAuditService();
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
        audit.Records.Should().ContainSingle(r => r.Action == "bootstrap.first_admin.rejected_already_initialized");
    }

    [Fact]
    public async Task CreateFirstAdmin_when_invalid_request_returns_bad_request_and_audits_rejection()
    {
        var audit = new CapturingAuditService();
        var controller = NewController(new StubBootstrapService(BootstrapSetupStatus.FromUserCount(0)), audit);

        var result = await controller.CreateFirstAdmin(
            new FirstAdminRequest("", "Local", "admin"),
            CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = badRequest.Value.Should().BeOfType<BootstrapErrorResponse>().Subject;
        body.Code.Should().Be(BootstrapErrorCodes.InvalidRequest);
        body.Initialized.Should().BeFalse();
        audit.Records.Should().ContainSingle(r => r.Action == "bootstrap.first_admin.rejected_invalid_request");
    }

    private static BootstrapController NewController(
        IFirstAdminBootstrapService bootstrap,
        IAuditService? audit = null) =>
        new(bootstrap, audit ?? new CapturingAuditService());

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

    private sealed class CapturingAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }
}
