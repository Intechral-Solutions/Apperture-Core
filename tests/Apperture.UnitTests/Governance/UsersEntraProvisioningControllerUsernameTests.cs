using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-9 gate §22: the Entra provisioning endpoints report the username the service says it stored, on success only.
/// A failure identifies no user and so carries no username.
/// </summary>
public sealed class UsersEntraProvisioningControllerUsernameTests
{
    private const string Oid1 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1";
    private const string Oid2 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee2";
    private const string Oid3 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee3";

    [Fact]
    public async Task Batch_rows_carry_the_services_stored_username_and_failures_carry_none()
    {
        var provisioning = new ScriptedProvisioning
        {
            [Oid1] = Result(Oid1, "ada.lovelace"),
            [Oid2] = new UserProvisioningException(UserProvisioningErrorCodes.DuplicateIdentity, "Already a user."),
            [Oid3] = new InvalidOperationException("Graph unavailable.")
        };
        var controller = NewController(provisioning, new ScriptedAccessRequests());

        var action = await controller.Provision(
            new EntraDirectoryProvisionRequestBody("reg-1", [Oid1, Oid2, Oid3], null, false),
            CancellationToken.None);

        var results = action.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<EntraProvisioningBatchResultDto>().Which.Results;
        results.Should().HaveCount(3);
        results[0].Succeeded.Should().BeTrue();
        results[0].Username.Should().Be("ada.lovelace");
        results[0].UserId.Should().Be("u-" + Oid1);
        results.Skip(1).Should().OnlyContain(r => !r.Succeeded && r.Username == null && r.UserId == null);
        results[1].ErrorCode.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        results[2].ErrorCode.Should().Be("user.provisioning.failed");
    }

    [Fact]
    public async Task A_batch_keeps_the_committed_people_and_reports_an_unknown_or_not_completed_person_on_its_own_row()
    {
        // AUD-1.2 review (gate §13.2): one person's uncertain or not-completed operation never turns the whole batch into
        // "nothing was changed" while the people before it committed.
        var provisioning = new ScriptedProvisioning
        {
            [Oid1] = Result(Oid1, "ada.lovelace"),
            [Oid2] = new Apperture.Core.Application.Audit.AuditOutcomeUnknownException("op-unknown"),
            [Oid3] = new Apperture.Core.Application.Audit.AuditOperationNotCompletedException("op-rolled-back"),
        };
        var controller = NewController(provisioning, new ScriptedAccessRequests());

        var action = await controller.Provision(
            new EntraDirectoryProvisionRequestBody("reg-1", [Oid1, Oid2, Oid3], null, false),
            CancellationToken.None);

        var results = action.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<EntraProvisioningBatchResultDto>().Which.Results;
        results.Should().HaveCount(3);
        results[0].Succeeded.Should().BeTrue();
        results[1].ErrorCode.Should().Be("outcome_unknown");
        results[1].ErrorMessage.Should().NotContainEquivalentOf("nothing was changed");
        results[2].ErrorCode.Should().Be("operation_not_completed");
    }

    [Fact]
    public async Task Approval_returns_the_services_stored_username()
    {
        var accessRequests = new ScriptedAccessRequests { Approval = Result(Oid1, "grace.hopper") };
        var controller = NewController(new ScriptedProvisioning(), accessRequests);

        var action = await controller.Approve("req-1", new EntraAccessRequestDecisionBody(null, false), CancellationToken.None);

        var body = action.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<EntraProvisioningPersonResultDto>().Subject;
        body.Succeeded.Should().BeTrue();
        body.Username.Should().Be("grace.hopper");
        body.UserId.Should().Be("u-" + Oid1);
        body.ObjectId.Should().Be(Oid1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_refused_approval_stays_a_409_with_no_result_and_so_no_username(bool provisioningFailure)
    {
        Exception failure = provisioningFailure
            ? new UserProvisioningException(UserProvisioningErrorCodes.DuplicateIdentity, "Already a user.")
            : new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotPending, "No longer pending.");
        var controller = NewController(new ScriptedProvisioning(), new ScriptedAccessRequests { ApprovalFailure = failure });

        var action = await controller.Approve("req-1", new EntraAccessRequestDecisionBody(null, false), CancellationToken.None);

        var conflict = action.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().NotBeOfType<EntraProvisioningPersonResultDto>();
        conflict.Value!.GetType().GetProperty("username").Should().BeNull();
    }

    private static UsersEntraProvisioningController NewController(
        IUserProvisioningService provisioning,
        IEntraAccessRequestService accessRequests) =>
        new(new NoDirectory(), provisioning, accessRequests, new NoRegistrations());

    private static UserProvisioningResult Result(string objectId, string username) =>
        new(new UserId("u-" + objectId), username, "Display " + username, objectId, EntraLinkProven: false,
            WelcomeMailAttempted: false, WelcomeMailDelivered: false, WelcomeMailFailureSummary: null,
            RoleAssigned: false, ReconciledRequestId: null);

    private sealed class ScriptedProvisioning : IUserProvisioningService
    {
        private readonly Dictionary<string, object> _outcomes = new(StringComparer.OrdinalIgnoreCase);

        public object this[string objectId]
        {
            set => _outcomes[objectId] = value;
        }

        public ValueTask<UserProvisioningResult> ProvisionFromEntraDirectoryAsync(
            EntraDirectoryProvisioningRequest request, CancellationToken cancellationToken = default) =>
            _outcomes[request.ObjectId] switch
            {
                UserProvisioningResult result => ValueTask.FromResult(result),
                Exception exception => throw exception,
                _ => throw new NotSupportedException()
            };

        public ValueTask<UserProvisioningResult> ProvisionFromAccessRequestAsync(
            AccessRequestApprovalProvisioningRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ScriptedAccessRequests : IEntraAccessRequestService
    {
        public UserProvisioningResult? Approval { get; init; }
        public Exception? ApprovalFailure { get; init; }

        public ValueTask<UserProvisioningResult> ApproveAsync(
            string requestId, string? initialRoleId, bool sendWelcomeEmail, CancellationToken cancellationToken = default) =>
            ApprovalFailure is not null ? throw ApprovalFailure : ValueTask.FromResult(Approval!);

        public ValueTask<EntraAccessRequestSessionSnapshot> GetSessionSnapshotAsync(
            EntraAccessRequestPrincipal principal, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<EntraAccessRequest> SubmitAsync(
            EntraAccessRequestPrincipal principal, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<(IReadOnlyList<EntraAccessRequest> Items, int PendingCount)> ListPendingForReviewAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask RejectAsync(string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoDirectory : IEntraProvisioningDirectoryService
    {
        public ValueTask<EntraProvisioningDirectoryPage> SearchAsync(
            string registrationId, string? search, string? continuationToken, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(
            string registrationId, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoRegistrations : IEntraInstanceRegistrationRepository
    {
        public Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetActiveAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateOpenIdValidationOutcomeAsync(string id, OpenIdConfigurationStatus status, DateTimeOffset validatedUtc, string? summary, string? detail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateDirectoryDiscoveryOutcomeAsync(string id, DirectoryDiscoveryStatus status, DateTimeOffset utc, string? summary, string? detail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
