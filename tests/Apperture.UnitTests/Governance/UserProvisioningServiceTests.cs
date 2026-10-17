using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
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
    private const string OtherTid = "99999999-8888-7777-6666-555555555555";
    private const string OtherOid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";

    [Fact]
    public async Task Directory_provision_creates_pending_entra_link_without_local_credential()
    {
        var harness = Harness.Create();

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true));

        var user = harness.Users.Added.Should().ContainSingle().Subject;
        user.Username.Should().Be("jane", "generated from the UPN local part, never the object id");
        result.Username.Should().Be(user.Username, "the result reports the username that was persisted");
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
            IsBreakGlassEligible: false)
        { Username = "existing" });

        var act =() => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid)).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Same_oid_in_a_different_tenant_is_not_a_duplicate()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("existing"),
            "Other tenant",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, OtherTid, "reg-other")],
            [],
            IsBreakGlassEligible: false)
        { Username = "existing" });

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid));

        result.UserId.Value.Should().NotBe("existing");
        harness.Users.Added.Should().ContainSingle(u =>
            u.IdentityLinks.Any(l => l.ExternalId == Oid && l.TenantId == Tid));
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
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow)
        { Username = "existing" });

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
        harness.Users.Added.Single().Username.Should().Be("jane", "generated from the request's UPN snapshot");
        result.Username.Should().Be(harness.Users.Added.Single().Username, "the result reports the username that was persisted");
    }

    [Fact]
    public async Task The_result_reports_the_persisted_username_when_the_repository_stores_a_later_candidate()
    {
        var harness = Harness.Create();
        harness.Users.RejectUsernamesOnce("jane");

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid));

        var stored = harness.Users.Added.Should().ContainSingle().Subject;
        stored.Username.Should().Be("jane-2", "the first candidate lost the race, so the next one was stored");
        result.Username.Should().Be("jane-2", "the result is the stored name, not a recomputed base");
    }

    [Fact]
    public async Task A_reconciled_directory_provision_reports_the_new_users_username()
    {
        var harness = Harness.Create();
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1", "reg-1", Tid, Oid, "Jane", "jane@contoso.com", "jane@contoso.com", "Member",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-5), EntraAccessRequestStatus.Pending));

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid));

        result.ReconciledRequestId.Should().Be("req-1");
        result.Username.Should().Be(harness.Users.Added.Single().Username);
    }

    [Fact]
    public async Task Approving_a_request_for_an_existing_identity_reconciles_it_and_returns_no_result()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("existing"),
            "Existing",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, Tid, "reg-1")],
            [],
            IsBreakGlassEligible: false)
        { Username = "existing" });
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1", "reg-1", Tid, Oid, "Jane", "jane@contoso.com", "jane@contoso.com", "Member",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-5), EntraAccessRequestStatus.Pending));

        var act = () => harness.Service.ProvisionFromAccessRequestAsync(
            new AccessRequestApprovalProvisioningRequest("req-1")).AsTask();

        var ex = await act.Should().ThrowAsync<UserProvisioningException>();
        ex.Which.Code.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity, "an existing identity yields a failure, not a result");
        harness.Users.Added.Should().BeEmpty();
        harness.Requests.Items["req-1"].ProvisionedUserId.Should().Be("existing");
        var (reconciled, actor) = harness.Audit.Events.Should().ContainSingle(
            "the reconciliation committed with its row; the refusal followed the confirmed commit (clarification 4)").Subject;
        reconciled.Action.Should().Be(UserProvisioningAuditActionNames.RequestReconciled);
        actor.Should().Be(AuditActor.CurrentPrincipal);
        reconciled.Target.Should().Be(new AuditTarget(AuditTargetTypes.EntraAccessRequest, "req-1"));
        reconciled.Fields["userId"].Should().Be("existing");
    }

    [Fact]
    public async Task A_provisioning_commits_the_user_its_initial_role_and_their_rows_and_mails_the_welcome_once_after()
    {
        var harness = Harness.Create();

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true, InitialRoleId: BootstrapConstants.PlatformAdminRoleId));

        harness.Audit.Actions.Should().Equal(
            UserProvisioningAuditActionNames.EntraDirectoryProvisioned,
            AuditActionNames.RoleAssigned,
            UserProvisioningAuditActionNames.WelcomeSent);
        harness.Audit.Events.Should().OnlyContain(e => e.Actor == AuditActor.CurrentPrincipal, "the administrator provisions");
        var provisioned = harness.Audit.Events[0].Event;
        provisioned.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, result.UserId.Value));
        provisioned.Fields["tenantId"].Should().Be(Tid);
        provisioned.Fields["objectId"].Should().Be(Oid);
        provisioned.Fields.Values.Should().NotContain("jane@contoso.com", "email and UPN are never recorded");
        harness.Audit.Events[2].Event.Definition.WriteClass.Should().Be(AuditWriteClass.Attempt);
        harness.Mail.Recipients.Should().ContainSingle();
    }

    [Fact]
    public async Task A_welcome_send_that_throws_is_unknown_and_never_fails_the_committed_provisioning()
    {
        var harness = Harness.Create();
        harness.Mail.Throw = true;

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, SendWelcomeEmail: true));

        result.WelcomeMailDelivered.Should().BeFalse();
        harness.Users.Added.Should().ContainSingle();
        var welcome = harness.Audit.Last;
        welcome.Action.Should().Be(UserProvisioningAuditActionNames.WelcomeFailed);
        welcome.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown);
        welcome.Fields["failure"].Should().Be("error");
    }

    [Fact]
    public async Task Directory_provision_of_a_guest_skips_the_ext_upn_and_uses_mail_then_display_name()
    {
        var harness = Harness.Create();
        harness.Directory.User = DirectoryUser(mail: null, upn: "jane_contoso.com#EXT#@tenant.onmicrosoft.com");

        await harness.Service.ProvisionFromEntraDirectoryAsync(new EntraDirectoryProvisioningRequest("reg-1", Oid));

        harness.Users.Added.Should().ContainSingle().Which.Username.Should().Be("jane.smith");
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

    // ---- IAM-10.2a: the initial role is decided under the authority lock set, by the same rules as an assignment ----

    private static readonly PermissionDescriptor Held = new("connection.read", new ResourceDescriptor("Connection", "instance"));
    private static readonly PermissionDescriptor NotHeld = new("connection.update", new ResourceDescriptor("Connection", "instance"));
    private static readonly PermissionDescriptor RoleAssignGrant = new("role.assign", new ResourceDescriptor("Role", "instance"));

    private static Harness HarnessWithOrdinaryAdministrator()
    {
        var harness = Harness.Create();
        harness.Roles.ActorRoles = [new Role(new RoleId("role-people-admin"), "People admin", null, [RoleAssignGrant, Held], IsSystemRole: false)];
        harness.Roles.Defined["role-subset"] = new Role(new RoleId("role-subset"), "Subset", null, [Held], IsSystemRole: false);
        harness.Roles.Defined["role-exceeding"] = new Role(new RoleId("role-exceeding"), "Exceeding", null, [Held, NotHeld], IsSystemRole: false);
        harness.Roles.Defined["role-broken"] = new Role(new RoleId("role-broken"), "Broken", null, [], IsSystemRole: false, PermissionsReadable: false);
        return harness;
    }

    [Fact]
    public async Task An_administrator_who_is_not_a_platform_administrator_cannot_give_the_administrator_role_to_a_new_person()
    {
        var harness = HarnessWithOrdinaryAdministrator();

        var act = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: BootstrapConstants.PlatformAdminRoleId)).AsTask();

        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        harness.Users.Added.Should().BeEmpty("the person was not added: the refusal rolled the unit back");
        harness.Roles.Assigned.Should().BeEmpty();
        harness.Audit.Actions.Should().Equal(AuditActionNames.RoleAssignmentRejected);
    }

    [Fact]
    public async Task An_initial_role_exceeding_the_administrator_is_refused_and_a_subset_role_is_assigned_with_the_person()
    {
        var harness = HarnessWithOrdinaryAdministrator();

        var refused = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: "role-exceeding")).AsTask();
        (await refused.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("assignment_exceeds_authority");
        harness.Users.Added.Should().BeEmpty();

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: "role-subset"));

        result.RoleAssigned.Should().BeTrue();
        harness.Roles.Assigned.Should().ContainSingle(a => a.RoleId == "role-subset" && a.UserId == result.UserId.Value);
        harness.Audit.Actions.Should().Equal(
            AuditActionNames.RoleAssignmentRejected,
            UserProvisioningAuditActionNames.EntraDirectoryProvisioned,
            AuditActionNames.RoleAssigned);
    }

    [Fact]
    public async Task An_unreadable_initial_role_is_a_typed_conflict_and_adds_nobody()
    {
        var harness = HarnessWithOrdinaryAdministrator();

        var broken = () => harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: "role-broken")).AsTask();

        (await broken.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.RolePermissionsUnreadable);
        harness.Users.Added.Should().BeEmpty();
        harness.Audit.Single(AuditActionNames.RoleAssignmentRejected).Fields["reason"].Should().Be("role_permissions_unreadable");
    }

    [Fact]
    public async Task Approving_an_access_request_applies_the_same_rules_to_the_initial_role()
    {
        var harness = HarnessWithOrdinaryAdministrator();
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1", "reg-1", Tid, Oid, "Jane Smith", "jane@contoso.com", "jane@contoso.com", "Member",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), EntraAccessRequestStatus.Pending));

        var refused = () => harness.Service.ProvisionFromAccessRequestAsync(
            new AccessRequestApprovalProvisioningRequest("req-1", InitialRoleId: BootstrapConstants.PlatformAdminRoleId)).AsTask();
        (await refused.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        harness.Users.Added.Should().BeEmpty();
        harness.Requests.Items["req-1"].Status.Should().Be(EntraAccessRequestStatus.Pending, "a refused approval leaves the request pending");

        var result = await harness.Service.ProvisionFromAccessRequestAsync(
            new AccessRequestApprovalProvisioningRequest("req-1", InitialRoleId: "role-subset"));

        result.RoleAssigned.Should().BeTrue();
        harness.Requests.Items["req-1"].Status.Should().Be(EntraAccessRequestStatus.Approved);
    }

    [Fact]
    public async Task A_platform_administrator_may_give_any_initial_role()
    {
        var harness = Harness.Create();

        var result = await harness.Service.ProvisionFromEntraDirectoryAsync(
            new EntraDirectoryProvisioningRequest("reg-1", Oid, InitialRoleId: BootstrapConstants.PlatformAdminRoleId));

        result.RoleAssigned.Should().BeTrue();
    }

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public AuditRecording Audit { get; } = new();
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
                TimeProvider.System,
                Mail,
                new AccountSecurityLinkBuilder(config),
                Audit,
                Audit);
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
            string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.FirstOrDefault(r =>
                r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(
            string tenantId, IReadOnlyList<string> objectIds, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(Items.Values
                .Where(r => r.Status == EntraAccessRequestStatus.Pending
                    && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && objectIds.Contains(r.ProviderObjectId, StringComparer.OrdinalIgnoreCase))
                .ToList());

        public ValueTask<EntraAccessRequest?> FindLatestAsync(
            string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values
                .Where(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RequestedAtUtc)
                .FirstOrDefault());

        public ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(
                Items.Values.Where(r => r.Status == EntraAccessRequestStatus.Pending).ToList());

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
        private readonly HashSet<string> _rejectOnce = new(StringComparer.Ordinal);
        public List<User> Added { get; } = [];

        public void Seed(User user) => _users[user.Id.Value] = user;

        /// <summary>Simulates losing the race for these candidate names: the first insert of each is refused as taken.</summary>
        public void RejectUsernamesOnce(params string[] usernames)
        {
            foreach (var username in usernames)
                _rejectOnce.Add(username);
        }

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(id.Value));

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            GetByIdentityLinkAsync(ProviderIdentityKey.FromSourceAndExternalId(source, externalId), cancellationToken);

        public ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(identity.Matches)));

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            if (_rejectOnce.Remove(user.Username))
                throw new UsernameException(UsernameErrorCodes.Taken, "That username is taken.");

            foreach (var link in user.IdentityLinks)
            {
                var key = link.Source == IdentitySource.Entra
                    ? ProviderIdentityKey.Entra(link.TenantId ?? "", link.ExternalId)
                    : ProviderIdentityKey.Local(link.ExternalId);
                var existing = _users.Values.FirstOrDefault(u => u.IdentityLinks.Any(key.Matches));
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

        /// <summary>Roles a test defines; any other id reads as an empty system role.</summary>
        public Dictionary<string, Role> Defined { get; } = new(StringComparer.Ordinal);

        /// <summary>The signed-in administrator's roles; null means a Platform Administrator.</summary>
        public List<Role>? ActorRoles { get; set; }

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(Defined.TryGetValue(roleId.Value, out var defined) ? defined : new Role(roleId, "Role", null, [], IsSystemRole: true));

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        // The signed-in administrator ("admin") is a Platform Administrator, who may give any initial role.
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>(
                userId.Value != "admin"
                    ? []
                    : ActorRoles ?? [new Role(new RoleId(BootstrapConstants.PlatformAdminRoleId), "Platform Administrator", null, [], IsSystemRole: true)]);

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

    private sealed class RecordingMail : IAccountSecurityMailSender
    {
        public bool Fail { get; set; }
        public bool Throw { get; set; }
        public List<string> Recipients { get; } = [];

        public ValueTask<AccountSecurityMailResult> SendAsync(
            string recipient, string subject, string plainTextBody, CancellationToken cancellationToken = default)
        {
            Recipients.Add(recipient);
            if (Throw)
                throw new InvalidOperationException("Injected: the transport threw.");
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
