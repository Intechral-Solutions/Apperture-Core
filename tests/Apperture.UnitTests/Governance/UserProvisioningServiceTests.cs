using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UserProvisioningServiceTests
{
    private const string Oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tid = "11111111-2222-3333-4444-555555555555";
    private const string OtherOid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";

    [Fact]
    public async Task Directory_provision_creates_pending_entra_link_without_local_credential()
    {
        var harness = Harness.Create();

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true));

        var user = harness.Users.Added.Should().ContainSingle().Subject;
        user.EmailAddress.Should().BeNull();
        user.EmailVerifiedAtUtc.Should().BeNull();
        user.IdentityLinks.Should().ContainSingle(l =>
            l.Source == IdentitySource.Entra
            && l.ExternalId == Oid
            && l.LastAuthenticatedAt == null
            && l.TenantId == Tid);
        user.IdentityLinks.Should().NotContain(l => l.Source == IdentitySource.Local);
        result.EntraLinkProven.Should().BeFalse();
        result.WelcomeMailAttempted.Should().BeTrue();
        result.WelcomeMailDelivered.Should().BeTrue();
        harness.Mail.Recipients.Should().Equal("jane@contoso.com");
        harness.Audit.Actions.Should().Contain(UserProvisioningAuditActionNames.EntraDirectoryProvisioned);
    }

    [Fact]
    public async Task Directory_provision_does_not_treat_upn_as_a_mailbox()
    {
        var harness = Harness.Create();
        harness.Directory.User = DirectoryUser(mail: null, upn: "jane@contoso.com");

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true));

        result.WelcomeMailAttempted.Should().BeFalse();
        harness.Mail.Recipients.Should().BeEmpty();
        harness.Users.Added.Should().ContainSingle();
    }

    [Fact]
    public async Task Welcome_mail_failure_does_not_roll_back_the_user()
    {
        var harness = Harness.Create();
        harness.Mail.Fail = true;

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true));

        harness.Users.Added.Should().ContainSingle();
        result.WelcomeMailAttempted.Should().BeTrue();
        result.WelcomeMailDelivered.Should().BeFalse();
        harness.Audit.Actions.Should().Contain(UserProvisioningAuditActionNames.WelcomeFailed);
    }

    [Fact]
    public async Task Disabled_directory_accounts_are_rejected()
    {
        var harness = Harness.Create();
        harness.Directory.User = DirectoryUser(enabled: false);

        var act = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid)).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.DirectoryUserDisabled);
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_immutable_identity_is_rejected()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("existing"),
            "Existing",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, Tid, "reg-1")],
            [],
            IsBreakGlassEligible: false));

        var act = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid)).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Same_mail_with_a_different_oid_does_not_collide()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("existing"),
            "Existing",
            [new IdentityLink(IdentitySource.Entra, OtherOid, DateTimeOffset.UtcNow, Tid, "reg-1")],
            [],
            IsBreakGlassEligible: false,
            EmailAddress: "jane@contoso.com",
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow));

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid));

        result.UserId.Value.Should().NotBe("existing");
        harness.Users.Added.Should().ContainSingle(u => u.IdentityLinks.Any(l => l.ExternalId == Oid));
    }

    [Fact]
    public async Task Role_assignment_requires_role_assign()
    {
        var harness = Harness.Create(allowRoleAssign: false);

        var act = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: BootstrapConstants.PlatformAdminRoleId)).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.RoleAssignForbidden);
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Directory_provision_reconciles_a_pending_access_request()
    {
        var harness = Harness.Create();
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1",
            "reg-1",
            Tid,
            Oid,
            "Jane",
            "jane@contoso.com",
            "jane@contoso.com",
            "Member",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-5),
            EntraAccessRequestStatus.Pending));

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid));

        result.ReconciledRequestId.Should().Be("req-1");
        harness.Requests.Items["req-1"].Status.Should().Be(EntraAccessRequestStatus.Approved);
        harness.Requests.Items["req-1"].ProvisionedUserId.Should().Be(result.UserId.Value);
        harness.Audit.Actions.Should().Contain(UserProvisioningAuditActionNames.RequestReconciled);
    }

    [Fact]
    public async Task Access_request_approval_creates_a_proven_entra_link()
    {
        var provenAt = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var harness = Harness.Create();
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1",
            "reg-1",
            Tid,
            Oid,
            "Jane Smith",
            "jane@contoso.com",
            "jane@contoso.com",
            "Member",
            provenAt,
            provenAt.AddMinutes(1),
            EntraAccessRequestStatus.Pending));

        var result = await harness.Service.ProvisionFromAccessRequestAsync(
            new AccessRequestApprovalProvisioningRequest("req-1"));

        result.EntraLinkProven.Should().BeTrue();
        var link = harness.Users.Added.Single().IdentityLinks.Single();
        link.LastAuthenticatedAt.Should().Be(provenAt);
        link.Source.Should().Be(IdentitySource.Entra);
        link.ExternalId.Should().Be(Oid);
        harness.Requests.Items["req-1"].Status.Should().Be(EntraAccessRequestStatus.Approved);
        harness.Requests.Items["req-1"].ProvisionedUserId.Should().Be(result.UserId.Value);
        harness.Users.Added.Single().EmailAddress.Should().BeNull();
    }

    [Fact]
    public async Task Email_is_never_an_identity_key()
    {
        EntraTokenClaims.IsValidObjectId("jane@contoso.com").Should().BeFalse();
        EntraTokenClaims.IsValidObjectId(Oid).Should().BeTrue();

        var harness = Harness.Create();
        var act = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", "jane@contoso.com")).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.InvalidObjectId);
        harness.Users.Added.Should().BeEmpty();
    }

    private static EntraProvisioningDirectoryUser DirectoryUser(
        bool enabled = true,
        string? mail = "jane@contoso.com",
        string? upn = "jane@contoso.com") =>
        new(Oid, "Jane Smith", upn, mail, "Member", enabled, EntraDirectoryAppertureStatus.Available, null);

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public RecordingMail Mail { get; } = new();
        public RecordingRequests Requests { get; } = new();
        public StubDirectory Directory { get; } = new();
        public UserProvisioningService Service { get; }

        private Harness(bool allowUserManage, bool allowRoleAssign)
        {
            Directory.User = DirectoryUser();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ShellUi:ManagementUiUrl"] = "http://localhost:9001"
            }).Build();
            Service = new UserProvisioningService(
                new StubPrincipal(),
                new SelectiveAuth(allowUserManage, allowRoleAssign),
                Users,
                Roles,
                new StubRegistrations(),
                Directory,
                new StubAssociation(),
                Requests,
                Audit,
                TimeProvider.System,
                Mail,
                new AccountSecurityLinkBuilder(config));
        }

        public static Harness Create(bool allowUserManage = true, bool allowRoleAssign = true) =>
            new(allowUserManage, allowRoleAssign);
    }

    private sealed class StubDirectory : IEntraProvisioningDirectoryService
    {
        public EntraProvisioningDirectoryUser? User { get; set; }

        public ValueTask<EntraProvisioningDirectoryPage> SearchAsync(
            string registrationId,
            string? search,
            string? continuationToken,
            int top,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(
            string registrationId,
            string objectId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(User);
    }

    private sealed class StubAssociation : IEntraIdentityAssociationService
    {
        public ValueTask<EntraAssociationOutcome> CompleteSignInAsync(
            EntraIdentityAssertion assertion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraAssociationOutcome> CompleteLinkAsync(
            UserId initiatingUserId,
            EntraIdentityAssertion assertion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
            string objectId,
            CancellationToken cancellationToken = default) =>
            BuildPendingAdminLinkAsync(objectId, new EntraInstanceRegistration { Id = "reg-1", AzureTenantId = Tid }, cancellationToken);

        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
            string objectId,
            EntraInstanceRegistration registration,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IdentityLink(
                IdentitySource.Entra,
                EntraTokenClaims.Normalize(objectId)!,
                LastAuthenticatedAt: null,
                TenantId: EntraTokenClaims.Normalize(registration.AzureTenantId),
                RegistrationId: registration.Id));
    }

    private sealed class StubRegistrations : IEntraInstanceRegistrationRepository
    {
        private readonly EntraInstanceRegistration _registration = new()
        {
            Id = "reg-1",
            DisplayName = "Contoso",
            Authority = "https://login.microsoftonline.com/" + Tid,
            ClientId = "client",
            Enabled = true,
            AzureTenantId = Tid,
            UsesDeploymentSecret = true
        };

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

        public void Seed(EntraAccessRequest request) => Items[request.Id] = request;

        public ValueTask<EntraAccessRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.GetValueOrDefault(id));

        public ValueTask<EntraAccessRequest?> FindPendingAsync(
            string registrationId, string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.FirstOrDefault(r =>
                r.Status == EntraAccessRequestStatus.Pending
                && r.RegistrationId == registrationId
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(
            string registrationId, IReadOnlyList<string> objectIds, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(Items.Values
                .Where(r => r.Status == EntraAccessRequestStatus.Pending && r.RegistrationId == registrationId)
                .ToList());

        public ValueTask<EntraAccessRequest?> FindLatestAsync(
            string registrationId, string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values
                .Where(r => r.RegistrationId == registrationId
                    && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RequestedAtUtc)
                .FirstOrDefault());

        public ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(
                Items.Values.Where(r => r.Status == EntraAccessRequestStatus.Pending).ToList());

        public ValueTask<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.Count(r => r.Status == EntraAccessRequestStatus.Pending));

        public ValueTask<int> CountRecentForIdentityAsync(
            string registrationId, string tenantId, string objectId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.Count(r =>
                r.RegistrationId == registrationId
                && r.RequestedAtUtc >= sinceUtc
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));

        public ValueTask AddAsync(EntraAccessRequest request, CancellationToken cancellationToken = default)
        {
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

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(id.Value));

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u =>
                u.IdentityLinks.Any(l => l.Source == source && string.Equals(l.ExternalId, externalId, StringComparison.OrdinalIgnoreCase))));

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            foreach (var link in user.IdentityLinks)
            {
                var existing = _users.Values.FirstOrDefault(u =>
                    u.IdentityLinks.Any(l => l.Source == link.Source && string.Equals(l.ExternalId, link.ExternalId, StringComparison.OrdinalIgnoreCase)));
                if (existing is not null)
                    throw new InvalidOperationException("An identity link with this source and external id already exists.");
            }

            _users[user.Id.Value] = user;
            Added.Add(user);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.Skip(skip).Take(take).ToList(), _users.Count));

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingRoles : IRoleRepository
    {
        public List<(string UserId, string RoleId)> Assigned { get; } = [];

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(new Role(roleId, "Role", null, [], IsSystemRole: true));

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Assigned.Count(a => a.RoleId == roleId.Value));

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assigned.Add((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class RecordingMail : IAccountSecurityMailSender
    {
        public bool Fail { get; set; }
        public List<string> Recipients { get; } = [];

        public ValueTask<AccountSecurityMailResult> SendAsync(
            string recipient, string subject, string plainTextBody, CancellationToken cancellationToken = default)
        {
            Recipients.Add(recipient);
            return ValueTask.FromResult(Fail
                ? new AccountSecurityMailResult(false, "smtp unavailable")
                : new AccountSecurityMailResult(true, null));
        }
    }

    private sealed class SelectiveAuth : IAuthorizationService
    {
        private readonly bool _userManage;
        private readonly bool _roleAssign;

        public SelectiveAuth(bool userManage, bool roleAssign)
        {
            _userManage = userManage;
            _roleAssign = roleAssign;
        }

        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default)
        {
            if (permission.Action == ActionConstants.UserManage)
                return ValueTask.FromResult(_userManage);
            if (permission.Action == ActionConstants.RoleAssign)
                return ValueTask.FromResult(_roleAssign);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("admin"), "admin"));
    }
}
