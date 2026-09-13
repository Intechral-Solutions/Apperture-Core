using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EntraAccessRequestServiceTests
{
    private const string Oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task Configured_member_can_request_when_member_requests_are_enabled()
    {
        var harness = Harness.Create(allowMembers: true);
        var principal = Principal();

        var snapshot = await harness.Service.GetSessionSnapshotAsync(principal);
        snapshot.State.Should().Be(EntraAccessRequestSessionStates.Eligible);
        snapshot.CanRequest.Should().BeTrue();

        var created = await harness.Service.SubmitAsync(principal);
        created.Status.Should().Be(EntraAccessRequestStatus.Pending);
        harness.Users.Added.Should().BeEmpty();
        harness.Requests.Items.Should().ContainSingle();
        harness.Audit.Actions.Should().Contain(EntraAccessRequestAuditActionNames.Created);

        var again = await harness.Service.SubmitAsync(principal);
        again.Id.Should().Be(created.Id);
        harness.Requests.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Guest_cannot_request_when_guest_requests_are_disabled()
    {
        var harness = Harness.Create(allowMembers: true, allowGuests: false, userType: "Guest");

        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal());

        snapshot.State.Should().Be(EntraAccessRequestSessionStates.GuestDisabled);
        snapshot.CanRequest.Should().BeFalse();
    }

    [Fact]
    public async Task Member_guest_classification_uses_directory_user_type()
    {
        var harness = Harness.Create(allowMembers: true, userType: "Member");
        (await harness.Service.GetSessionSnapshotAsync(Principal())).State.Should().Be(EntraAccessRequestSessionStates.Eligible);

        harness.Discovery.User = DirectoryUser("Guest");
        (await harness.Service.GetSessionSnapshotAsync(Principal())).State.Should().Be(EntraAccessRequestSessionStates.GuestDisabled);
    }

    [Fact]
    public async Task Requests_disabled_tenant_cannot_submit()
    {
        var harness = Harness.Create(allowMembers: false);
        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal());
        snapshot.State.Should().Be(EntraAccessRequestSessionStates.RequestsDisabled);

        var act = () => harness.Service.SubmitAsync(Principal()).AsTask();
        await act.Should().ThrowAsync<EntraAccessRequestException>();
        harness.Requests.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Graph_failure_fails_safe_without_email_heuristics()
    {
        var harness = Harness.Create(allowMembers: true);
        harness.Discovery.Fail = true;

        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal(upn: "jane@contoso.com"));

        snapshot.State.Should().Be(EntraAccessRequestSessionStates.EligibilityUnavailable);
        snapshot.CanRequest.Should().BeFalse();
    }

    [Fact]
    public async Task Already_provisioned_identity_does_not_create_a_request()
    {
        var harness = Harness.Create(allowMembers: true);
        harness.Users.Seed(new User(
            new UserId("u-1"),
            "Jane",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, Tid, "reg-1")],
            [],
            IsBreakGlassEligible: false));

        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal());
        snapshot.State.Should().Be(EntraAccessRequestSessionStates.AlreadyProvisioned);

        var act = () => harness.Service.SubmitAsync(Principal()).AsTask();
        await act.Should().ThrowAsync<EntraAccessRequestException>();
        harness.Requests.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Same_oid_in_another_tenant_is_not_already_provisioned()
    {
        var harness = Harness.Create(allowMembers: true);
        harness.Users.Seed(new User(
            new UserId("u-other"),
            "Other tenant",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, "99999999-8888-7777-6666-555555555555", "reg-other")],
            [],
            IsBreakGlassEligible: false));

        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal());
        snapshot.State.Should().Be(EntraAccessRequestSessionStates.Eligible);
        snapshot.CanRequest.Should().BeTrue();
    }

    [Fact]
    public async Task Reject_does_not_create_a_user()
    {
        var harness = Harness.Create(allowMembers: true);
        var created = await harness.Service.SubmitAsync(Principal());

        await harness.Service.RejectAsync(created.Id);

        harness.Users.Added.Should().BeEmpty();
        harness.Requests.Items[created.Id].Status.Should().Be(EntraAccessRequestStatus.Rejected);
        harness.Audit.Actions.Should().Contain(EntraAccessRequestAuditActionNames.Rejected);
        var snapshot = await harness.Service.GetSessionSnapshotAsync(Principal());
        snapshot.State.Should().Be(EntraAccessRequestSessionStates.Rejected);
        snapshot.Message.Should().Be("Your request was not approved.");
    }

    private static EntraAccessRequestPrincipal Principal(string? upn = "jane@contoso.com") =>
        new(Tid, Oid, "reg-1", new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), "Jane Smith", upn, "jane@contoso.com");

    private static EntraDirectoryUser DirectoryUser(string userType) =>
        new(Oid, "Jane Smith", "jane@contoso.com", "jane@contoso.com", userType, true);

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingRequests Requests { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public StubDiscovery Discovery { get; } = new();
        public EntraAccessRequestService Service { get; }

        private Harness(bool allowMembers, bool allowGuests, string userType)
        {
            Discovery.User = DirectoryUser(userType);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entra:ClientSecret"] = "secret"
            }).Build();
            Service = new EntraAccessRequestService(
                new StubPrincipal(),
                new AllowAllAuth(),
                Users,
                new StubRegistrations(allowMembers, allowGuests),
                Discovery,
                Requests,
                new ThrowingProvisioning(),
                config,
                Audit,
                TimeProvider.System);
        }

        public static Harness Create(bool allowMembers, bool allowGuests = false, string userType = "Member") =>
            new(allowMembers, allowGuests, userType);
    }

    private sealed class ThrowingProvisioning : IUserProvisioningService
    {
        public ValueTask<UserProvisioningResult> ProvisionFromEntraDirectoryAsync(
            EntraDirectoryProvisioningRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<UserProvisioningResult> ProvisionFromAccessRequestAsync(
            AccessRequestApprovalProvisioningRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubDiscovery : IEntraDirectoryDiscoveryService
    {
        public EntraDirectoryUser? User { get; set; }
        public bool Fail { get; set; }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Fail
                ? Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(false, DiscoveryFailureCategory.Transient, "unavailable", null))
                : Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(true, DiscoveryFailureCategory.None, "ok", null, User));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(true, DiscoveryFailureCategory.None, "ok", null, null));
    }

    private sealed class StubRegistrations : IEntraInstanceRegistrationRepository
    {
        private readonly EntraInstanceRegistration _registration;

        public StubRegistrations(bool allowMembers, bool allowGuests)
        {
            _registration = new EntraInstanceRegistration
            {
                Id = "reg-1",
                DisplayName = "Contoso",
                Authority = "https://login.microsoftonline.com/" + Tid,
                ClientId = "client",
                Enabled = true,
                AzureTenantId = Tid,
                UsesDeploymentSecret = true,
                AllowMemberAccessRequests = allowMembers,
                AllowGuestAccessRequests = allowGuests
            };
        }

        public Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<EntraInstanceRegistration?>(_registration);

        public Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<EntraInstanceRegistration?>(id == "reg-1" ? _registration : null);

        public Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntraInstanceRegistration>>([_registration]);

        public Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateOpenIdValidationOutcomeAsync(
            string id, OpenIdConfigurationStatus status, DateTimeOffset validatedUtc, string? summary, string? detail, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateDirectoryDiscoveryOutcomeAsync(
            string id, DirectoryDiscoveryStatus status, DateTimeOffset utc, string? summary, string? detail, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRequests : IEntraAccessRequestRepository
    {
        public Dictionary<string, EntraAccessRequest> Items { get; } = new(StringComparer.Ordinal);

        public ValueTask<EntraAccessRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.GetValueOrDefault(id));

        public ValueTask<EntraAccessRequest?> FindPendingAsync(
            string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.FirstOrDefault(r =>
                r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(
            string tenantId, IReadOnlyList<string> objectIds, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(Items.Values.Where(r =>
                r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList());

        public ValueTask<EntraAccessRequest?> FindLatestAsync(
            string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values
                .Where(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RequestedAtUtc)
                .FirstOrDefault());

        public ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(Items.Values.Where(r => r.Status == EntraAccessRequestStatus.Pending).ToList());

        public ValueTask<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.Count(r => r.Status == EntraAccessRequestStatus.Pending));

        public ValueTask<int> CountRecentForIdentityAsync(
            string tenantId, string objectId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.Count(r =>
                r.RequestedAtUtc >= sinceUtc
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));

        public ValueTask AddAsync(EntraAccessRequest request, CancellationToken cancellationToken = default)
        {
            if (Items.Values.Any(r => r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, request.TenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, request.ProviderObjectId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("pending exists");
            }

            Items[request.Id] = request;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(EntraAccessRequest request, CancellationToken cancellationToken = default)
        {
            Items[request.Id] = request;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public List<User> Added { get; } = [];

        public void Seed(User user) => _users[user.Id.Value] = user;

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            GetByIdentityLinkAsync(ProviderIdentityKey.FromSourceAndExternalId(source, externalId), cancellationToken);
        public ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(identity.Matches)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            Added.Add(user);
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) { _users[user.Id.Value] = user; return ValueTask.CompletedTask; }
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class AllowAllAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("admin"), "admin"));
    }
}
