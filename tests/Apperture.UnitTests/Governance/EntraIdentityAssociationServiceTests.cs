using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-1 acceptance behaviors for Entra identity association. These run without a live OIDC round trip:
/// the token is represented by an <see cref="EntraIdentityAssertion"/>.
/// </summary>
public sealed class EntraIdentityAssociationServiceTests
{
    private const string ActiveTenantId = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenantId = "22222222-2222-2222-2222-222222222222";
    private static readonly string Oid = "33333333-3333-3333-3333-333333333333";
    private static readonly string OtherOid = "44444444-4444-4444-4444-444444444444";
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sign_in_with_unknown_object_id_is_rejected_as_not_linked()
    {
        var harness = new Harness();

        var outcome = await harness.Service.CompleteSignInAsync(Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.AccountNotLinked);
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.RejectedUnlinked);
    }

    [Fact]
    public async Task Sign_in_from_a_different_directory_is_rejected_and_audited()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now, tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteSignInAsync(Assertion(tenantId: OtherTenantId));

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.TenantMismatch);
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.TenantMismatch);
    }

    [Fact]
    public async Task Sign_in_resolves_the_existing_platform_user()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now, tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteSignInAsync(Assertion());

        outcome.Succeeded.Should().BeTrue();
        outcome.UserId!.Value.Should().Be("usr-1");
    }

    [Fact]
    public async Task First_sign_in_proves_a_pending_admin_association()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: null, tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteSignInAsync(Assertion());

        outcome.Succeeded.Should().BeTrue();
        harness.Repository.ProofCalls.Should().ContainSingle(c => c.ExternalId == Oid && c.ProvenAt == Now && c.TenantId == ActiveTenantId);
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.Proven);
    }

    [Fact]
    public async Task First_sign_in_of_a_pending_association_applies_entra_primary_policy()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: null, tenantId: ActiveTenantId)));

        await harness.Service.CompleteSignInAsync(Assertion());

        harness.Policy.AppliedUserIds.Should().ContainSingle().Which.Should().Be("usr-1");
    }

    [Fact]
    public async Task Repeated_proven_sign_in_reapplies_entra_primary_policy()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now.AddDays(-1), tenantId: ActiveTenantId)));

        await harness.Service.CompleteSignInAsync(Assertion());

        harness.Policy.AppliedUserIds.Should().ContainSingle().Which.Should().Be("usr-1");
        harness.Audit.Actions.Should().NotContain(EntraLinkAuditActionNames.Proven);
    }

    [Fact]
    public async Task Pending_admin_link_construction_does_not_apply_entra_primary_policy()
    {
        var harness = new Harness();

        await harness.Service.BuildPendingAdminLinkAsync(Oid);

        harness.Policy.AppliedUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Sign_in_does_not_resolve_a_matching_oid_from_another_tenant()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now, tenantId: OtherTenantId)));

        var outcome = await harness.Service.CompleteSignInAsync(Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.AccountNotLinked);
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.RejectedUnlinked);
        harness.Audit.Actions.Should().NotContain(EntraLinkAuditActionNames.TenantMismatch);
    }

    [Fact]
    public async Task Same_oid_in_two_tenants_are_distinct_identities()
    {
        var harness = new Harness();
        harness.Repository.Seed(
            NewUser("usr-a", EntraLink(Oid, provenAt: Now, tenantId: ActiveTenantId)),
            NewUser("usr-b", EntraLink(Oid, provenAt: Now, tenantId: OtherTenantId)));

        var outcome = await harness.Service.CompleteSignInAsync(Assertion());

        outcome.Succeeded.Should().BeTrue();
        outcome.UserId!.Value.Should().Be("usr-a");
    }

    [Fact]
    public async Task Self_link_binds_the_identity_to_the_initiating_user_as_proven()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", LocalLink("operator")));

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        outcome.Succeeded.Should().BeTrue();
        outcome.UserId!.Value.Should().Be("usr-1");
        var added = harness.Repository.AddedLinks.Should().ContainSingle().Subject;
        added.Link.Source.Should().Be(IdentitySource.Entra);
        added.Link.ExternalId.Should().Be(Oid);
        added.Link.TenantId.Should().Be(ActiveTenantId);
        added.Link.LastAuthenticatedAt.Should().Be(Now);
        added.Link.LinkedAt.Should().Be(Now);
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.Linked);
        harness.Policy.AppliedUserIds.Should().ContainSingle().Which.Should().Be("usr-1");
    }

    [Fact]
    public async Task Self_link_is_rejected_for_the_designated_recovery_account()
    {
        var harness = new Harness();
        harness.Repository.Seed(new User(
            new UserId("emerg-1"),
            EmergencyAccessIdentity.DisplayName,
            [LocalLink(EmergencyAccessIdentity.LocalUsername)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername });

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("emerg-1"), Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.EmergencyAccount);
        harness.Repository.AddedLinks.Should().BeEmpty();
        harness.Policy.AppliedUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_link_of_an_identity_owned_by_another_user_is_a_collision()
    {
        var harness = new Harness();
        harness.Repository.Seed(
            NewUser("usr-1", LocalLink("operator")),
            NewUser("usr-2", EntraLink(Oid, provenAt: Now, tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.Collision);
        harness.Repository.AddedLinks.Should().BeEmpty();
        harness.Audit.Actions.Should().Contain(EntraLinkAuditActionNames.Collision);
    }

    [Fact]
    public async Task Self_link_of_the_same_oid_in_another_tenant_is_not_a_collision()
    {
        var harness = new Harness();
        harness.Repository.Seed(
            NewUser("usr-1", LocalLink("operator")),
            NewUser("usr-2", EntraLink(Oid, provenAt: Now, tenantId: OtherTenantId)));

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        outcome.Succeeded.Should().BeTrue();
        harness.Repository.AddedLinks.Should().ContainSingle(l => l.UserId.Value == "usr-1");
    }

    [Fact]
    public async Task A_user_may_hold_only_one_entra_identity()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(OtherOid, provenAt: Now, tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.AlreadyLinked);
        harness.Repository.AddedLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_link_is_idempotent_when_already_linked_to_the_same_user()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now.AddDays(-1), tenantId: ActiveTenantId)));

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        outcome.Succeeded.Should().BeTrue();
        harness.Repository.AddedLinks.Should().BeEmpty();
        harness.Policy.AppliedUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_link_from_another_directory_is_rejected_without_creating_a_link()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", LocalLink("operator")));

        var outcome = await harness.Service.CompleteLinkAsync(
            new UserId("usr-1"),
            Assertion(tenantId: OtherTenantId));

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.TenantMismatch);
        harness.Repository.AddedLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task An_assertion_without_object_id_or_tenant_is_refused()
    {
        var harness = new Harness();

        var missingTenant = await harness.Service.CompleteSignInAsync(new EntraIdentityAssertion(Oid, ""));
        var missingOid = await harness.Service.CompleteSignInAsync(new EntraIdentityAssertion("user@contoso.com", ActiveTenantId));

        missingTenant.FailureCode.Should().Be(EntraLinkFailureCodes.InvalidIdentity);
        missingOid.FailureCode.Should().Be(EntraLinkFailureCodes.InvalidIdentity);
    }

    [Fact]
    public async Task Self_link_for_a_missing_user_fails_without_creating_a_user()
    {
        var harness = new Harness();

        var outcome = await harness.Service.CompleteLinkAsync(new UserId("nobody"), Assertion());

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.UserNotFound);
        harness.Repository.AddedUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_association_is_pending_and_carries_tenant_and_snapshots()
    {
        var harness = new Harness();
        harness.Directory.User = new EntraDirectoryUser(Oid, "Dana Operator", "dana@contoso.com", "dana@contoso.com");

        var link = await harness.Service.BuildPendingAdminLinkAsync(Oid);

        link.LastAuthenticatedAt.Should().BeNull();
        link.TenantId.Should().Be(ActiveTenantId);
        link.RegistrationId.Should().Be("reg-1");
        link.DisplayNameSnapshot.Should().Be("Dana Operator");
        link.UserPrincipalNameSnapshot.Should().Be("dana@contoso.com");
    }

    [Fact]
    public async Task Admin_association_still_persists_object_id_when_directory_is_unavailable()
    {
        var harness = new Harness();
        harness.Directory.Fail = true;

        var link = await harness.Service.BuildPendingAdminLinkAsync(Oid);

        link.ExternalId.Should().Be(Oid);
        link.TenantId.Should().Be(ActiveTenantId);
        link.DisplayNameSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task Admin_association_rejects_a_non_guid_object_id()
    {
        var harness = new Harness();

        Func<Task> act = async () => await harness.Service.BuildPendingAdminLinkAsync("dana@contoso.com");

        var ex = await act.Should().ThrowAsync<IdentityLinkException>();
        ex.Which.Code.Should().Be(IdentityLinkErrorCodes.InvalidEntraExternalId);
    }

    [Fact]
    public async Task Guest_directory_objects_are_not_filtered_out()
    {
        var harness = new Harness();
        harness.Directory.User = new EntraDirectoryUser(Oid, "Guest Person", "guest_partner#EXT#@contoso.com", null);

        var link = await harness.Service.BuildPendingAdminLinkAsync(Oid);

        link.ExternalId.Should().Be(Oid);
        link.UserPrincipalNameSnapshot.Should().Be("guest_partner#EXT#@contoso.com");
    }

    // ---- AUD-1.2: actors and targets (D1, D2, clarification 2) ----

    [Fact]
    public async Task A_refused_sign_in_is_recorded_as_the_validated_external_identity_never_as_a_user()
    {
        var harness = new Harness();

        await harness.Service.CompleteSignInAsync(Assertion());
        await harness.Service.CompleteSignInAsync(Assertion(tenantId: OtherTenantId));

        var (unlinked, unlinkedActor) = harness.Audit.Events[0];
        unlinked.Action.Should().Be(EntraLinkAuditActionNames.RejectedUnlinked);
        unlinkedActor.Should().Be(External(ActiveTenantId, Oid));
        unlinked.Target.Should().Be(new AuditTarget(AuditTargetTypes.ExternalIdentity, $"entra/{ActiveTenantId}/{Oid}"));
        var (mismatch, mismatchActor) = harness.Audit.Events[1];
        mismatch.Action.Should().Be(EntraLinkAuditActionNames.TenantMismatch);
        mismatchActor.Should().Be(External(OtherTenantId, Oid), "the token was validated; only its tenant was not this instance's");
        mismatch.Fields["flow"].Should().Be("sign_in");
    }

    [Fact]
    public async Task Proving_a_pending_link_at_sign_in_is_the_signed_in_users_change_against_the_persisted_link()
    {
        var harness = new Harness();
        var link = EntraLink(Oid, provenAt: null, tenantId: ActiveTenantId);
        harness.Repository.Seed(NewUser("usr-1", link));

        await harness.Service.CompleteSignInAsync(Assertion());

        var (proven, actor) = harness.Audit.Events.Should().ContainSingle().Subject;
        proven.Action.Should().Be(EntraLinkAuditActionNames.Proven);
        actor.Should().Be(AuditActor.AuthenticatedUser(new UserId("usr-1")));
        proven.Target.Should().Be(new AuditTarget(AuditTargetTypes.IdentityLink, link.LinkId!));
    }

    [Fact]
    public async Task A_self_link_is_the_initiating_users_change_and_its_refusals_name_that_user()
    {
        var harness = new Harness();
        harness.Repository.Seed(
            NewUser("usr-1", LocalLink("operator")),
            NewUser("usr-2", EntraLink(OtherOid, provenAt: Now, tenantId: ActiveTenantId)));

        await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());
        await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion(objectId: OtherOid));

        var added = harness.Repository.AddedLinks.Should().ContainSingle().Subject.Link;
        var (linked, linkedActor) = harness.Audit.Events[0];
        linked.Action.Should().Be(EntraLinkAuditActionNames.Linked);
        linkedActor.Should().Be(AuditActor.AuthenticatedUser(new UserId("usr-1")), "the initiating user from the protected sign-in state");
        linked.Target.Should().Be(new AuditTarget(AuditTargetTypes.IdentityLink, added.LinkId!), "the id the link was persisted with");
        var (collision, collisionActor) = harness.Audit.Events[1];
        collision.Action.Should().Be(EntraLinkAuditActionNames.Collision);
        collisionActor.Should().Be(AuditActor.AuthenticatedUser(new UserId("usr-1")));
        collision.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        collision.Definition.WriteClass.Should().Be(AuditWriteClass.Attempt);
        collision.Target.Should().Be(new AuditTarget(AuditTargetTypes.ExternalIdentity, $"entra/{ActiveTenantId}/{OtherOid}"));
    }

    [Fact]
    public async Task Linking_an_identity_already_proven_for_the_same_user_changes_nothing_and_records_nothing()
    {
        var harness = new Harness();
        harness.Repository.Seed(NewUser("usr-1", EntraLink(Oid, provenAt: Now.AddDays(-1), tenantId: ActiveTenantId)));

        await harness.Service.CompleteLinkAsync(new UserId("usr-1"), Assertion());

        harness.Audit.Events.Should().BeEmpty("re-linking the same proven identity is not a change (D7)");
    }

    private static AuditActor External(string tenantId, string objectId) =>
        AuditActor.ValidatedExternalIdentity(new AuditExternalActor(AuditExternalActor.EntraProvider, tenantId, objectId));

    private static EntraIdentityAssertion Assertion(string? objectId = null, string? tenantId = null) =>
        new(objectId ?? Oid, tenantId ?? ActiveTenantId, "sub-value");

    private static IdentityLink EntraLink(string oid, DateTimeOffset? provenAt, string? tenantId) =>
        new(IdentitySource.Entra, oid, provenAt, tenantId, "reg-1", Now.AddDays(-10), LinkId: "lnk-" + oid[..8] + (tenantId ?? "none0000")[..8]);

    private static IdentityLink LocalLink(string username) =>
        new(IdentitySource.Local, username, Now.AddDays(-10));

    private static User NewUser(string id, params IdentityLink[] links) =>
        new(new UserId(id), "Operator", links, Array.Empty<RoleId>(), IsBreakGlassEligible: false)
        {
            Username = links.FirstOrDefault(l => l.Source == IdentitySource.Local)?.ExternalId ?? id,
        };

    private sealed class Harness
    {
        public Harness()
        {
            Repository = new RecordingUserRepository();
            Audit = new AuditRecording();
            Directory = new StubDirectoryDiscovery();
            Service = new EntraIdentityAssociationService(
                Repository,
                new StubRegistrationRepository(),
                Directory,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)] = "deployment-secret"
                    })
                    .Build(),
                new FixedTimeProvider(Now),
                NullLogger<EntraIdentityAssociationService>.Instance,
                entraPrimaryPolicy: Policy,
                auditedUnitOfWork: Audit,
                auditEvents: Audit);
        }

        public RecordingUserRepository Repository { get; }

        internal AuditRecording Audit { get; }

        public StubDirectoryDiscovery Directory { get; }

        public RecordingEntraPrimaryPolicy Policy { get; } = new();

        public EntraIdentityAssociationService Service { get; }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubRegistrationRepository : IEntraInstanceRegistrationRepository
    {
        public Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<EntraInstanceRegistration?>(new EntraInstanceRegistration
            {
                Id = "reg-1",
                DisplayName = "Contoso",
                Authority = $"https://login.microsoftonline.com/{ActiveTenantId}",
                ClientId = "client-1",
                ClientSecretRef = null,
                UsesDeploymentSecret = true,
                AzureTenantId = ActiveTenantId,
                Enabled = true,
                IsActive = true
            });

        public Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            GetActiveAsync(cancellationToken);

        public Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntraInstanceRegistration>>([]);

        public Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default) =>
            Task.FromResult(registration);

        public Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetActiveAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateOpenIdValidationOutcomeAsync(string id, OpenIdConfigurationStatus status, DateTimeOffset validatedUtc, string? summary, string? detail, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateDirectoryDiscoveryOutcomeAsync(string id, DirectoryDiscoveryStatus status, DateTimeOffset utc, string? summary, string? detail, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class StubDirectoryDiscovery : IEntraDirectoryDiscoveryService
    {
        public EntraDirectoryUser? User { get; set; }

        public bool Fail { get; set; }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Fail
                ? Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(false, DiscoveryFailureCategory.Permissions, "denied", null))
                : Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(true, DiscoveryFailureCategory.None, "ok", null, User));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(true, DiscoveryFailureCategory.None, "ok", null, null));
    }

    internal sealed record ProofCall(string TenantId, string ExternalId, DateTimeOffset ProvenAt);

    internal sealed class RecordingEntraPrimaryPolicy : IEntraPrimaryAuthenticationPolicy
    {
        public List<string> AppliedUserIds { get; } = [];

        public ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            AppliedUserIds.Add(userId.Value);
            return ValueTask.FromResult(EntraPrimaryLocalDisableOutcome.Disabled);
        }

        public ValueTask<LocalAuthenticationPresentation> DescribeAsync(
            User user,
            LocalCredentialData? credential,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LocalAuthenticationPresentation(
                AuthenticationMethodStatuses.EntraNone,
                AuthenticationMethodStatuses.LocalEnabled,
                false));
    }

    internal sealed class RecordingUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

        public List<(UserId UserId, IdentityLink Link)> AddedLinks { get; } = [];

        public List<ProofCall> ProofCalls { get; } = [];

        public List<User> AddedUsers { get; } = [];

        public bool LocalCredentialDisableRequested => false;

        public void Seed(params User[] users)
        {
            foreach (var user in users)
                _users[user.Id.Value] = user;
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
            AddedUsers.Add(user);
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            ProofCalls.Add(new ProofCall("", externalId, at));
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(ProviderIdentityKey identity, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            ProofCalls.Add(new ProofCall(identity.TenantId ?? "", identity.ExternalId, at));
            return ValueTask.CompletedTask;
        }

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            AddIdentityLinkAsync(userId, new IdentityLink(source, externalId, null), cancellationToken);

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentityLink link, CancellationToken cancellationToken = default)
        {
            AddedLinks.Add((userId, link));
            if (_users.TryGetValue(userId.Value, out var user))
            {
                var links = user.IdentityLinks.ToList();
                links.Add(link);
                _users[userId.Value] = user with { IdentityLinks = links };
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteEntraLinkProofAsync(
            string tenantId,
            string objectId,
            DateTimeOffset provenAt,
            string? registrationIdWhenMissing,
            string? providerSubject,
            CancellationToken cancellationToken = default)
        {
            ProofCalls.Add(new ProofCall(tenantId, objectId, provenAt));
            return ValueTask.CompletedTask;
        }
    }
}
