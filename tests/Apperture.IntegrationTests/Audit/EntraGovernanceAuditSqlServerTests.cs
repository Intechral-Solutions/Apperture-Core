using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.2 provisioning, access requests, Entra association and the Entra-primary policy against SQL Server (audit hardening
/// design gate §10.4, §13, §22; record D1, D6; clarifications 2, 4, 6): each change commits with its row or not at all; an
/// approval of an identity that already has a user commits its reconciliation and is refused only after a confirmed commit,
/// while an unverifiable commit stays outcome_unknown; a refused self-link is decided in the transaction and recorded after
/// it rolled back, as the initiating user; a validated identity without a user is recorded as the external triple. Fails,
/// never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraGovernanceAuditSqlServerTests : IAsyncLifetime
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string RegistrationId = "reg-aud12";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public EntraGovernanceAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _db = await AuditSqlScratch.CreateAsync("Apperture_Aud12Entra_");
        await using var db = _db.NewDb();
        await new SqlEntraInstanceRegistrationRepository(db).AddAsync(new EntraInstanceRegistration
        {
            Id = RegistrationId,
            DisplayName = "Contoso",
            Authority = "https://login.microsoftonline.com/" + Tenant,
            ClientId = "client",
            Enabled = true,
            IsActive = true,
            AzureTenantId = Tenant,
            UsesDeploymentSecret = true,
            AllowMemberAccessRequests = true,
        });
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Access request approval (clarification 4) ----

    [Fact]
    public async Task Approving_an_identity_that_already_has_a_user_commits_the_reconciliation_and_refuses_only_after_a_confirmed_commit()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (owner, _) = await AddUserAsync("owner");
        var objectId = Guid.NewGuid().ToString();
        await LinkEntraAsync(owner, objectId);
        var requestId = await AddRequestAsync(objectId);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
        {
            var act = () => Provisioning(db, new Principal(admin)).ProvisionFromAccessRequestAsync(new AccessRequestApprovalProvisioningRequest(requestId)).AsTask();
            (await act.Should().ThrowAsync<UserProvisioningException>()).Which.Code.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        }

        attempts.Started.Should().Be(1, "verification confirmed the commit");
        (await _db.ScalarAsync<string>($"SELECT ProvisionedUserId FROM EntraAccessRequests WHERE Id = N'{requestId}';")).Should().Be(owner);
        var row = (await RowsAsync("entra_access_request", requestId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.AccessRequestReconciled);
        row["Actor"].Should().Be(admin);
    }

    [Fact]
    public async Task An_approval_whose_commit_cannot_be_verified_is_outcome_unknown_never_the_refusal()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (owner, _) = await AddUserAsync("owner");
        var objectId = Guid.NewGuid().ToString();
        await LinkEntraAsync(owner, objectId);
        var requestId = await AddRequestAsync(objectId);
        var lost = new LostCommitAcknowledgement();

        await using (var db = _db.NewDb(lost))
        {
            var unit = NewUnit(db, new Principal(admin), verification: new UnreachableVerification());
            var act = () => Provisioning(db, new Principal(admin), unit).ProvisionFromAccessRequestAsync(new AccessRequestApprovalProvisioningRequest(requestId)).AsTask();
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>("a refusal would assert the reconciliation's outcome, which nobody confirmed");
        }
    }

    [Fact]
    public async Task When_the_audit_insert_fails_an_approval_leaves_no_user_link_role_or_decision_and_sends_nothing()
    {
        var (admin, _) = await AddUserAsync("admin");
        var objectId = Guid.NewGuid().ToString();
        var requestId = await AddRequestAsync(objectId);
        var mail = new CountingMail();
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Provisioning(db, new Principal(admin), mail: mail).ProvisionFromAccessRequestAsync(
                new AccessRequestApprovalProvisioningRequest(requestId, Apperture.Core.Application.BootstrapConstants.PlatformAdminRoleId, SendWelcomeEmail: true)).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>($"SELECT Status FROM EntraAccessRequests WHERE Id = N'{requestId}';")).Should().Be(0, "still pending");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE ExternalId = N'{objectId}';")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM UserRoles;")).Should().Be(0);
        mail.Sent.Should().Be(0, "nothing is sent for a change that did not commit");
    }

    // ---- Access request submission: the validated external identity (D1) ----

    [Fact]
    public async Task A_submitted_access_request_is_recorded_as_the_validated_external_identity()
    {
        var objectId = Guid.NewGuid().ToString();

        await using (var db = _db.NewDb())
            await AccessRequests(db).SubmitAsync(new EntraAccessRequestPrincipal(Tenant, objectId, RegistrationId, DateTimeOffset.UtcNow, "Jane", "jane@contoso.com", "jane@contoso.com"));

        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.AccessRequestCreated}';")).Should().ContainSingle().Which;
        row["ActorKind"].Should().Be("external");
        row["Actor"].Should().Be($"entra/{Tenant}/{objectId}");
        row["ActorExternalTenantId"].Should().Be(Tenant);
        row["ActorExternalSubjectId"].Should().Be(objectId);
        row["ActorUserId"].Should().BeNull("the requester has no Apperture user");
        ((string)row["Details"]!).Should().NotContain("jane@contoso.com");
    }

    // ---- Self-link and sign-in (clarifications 2 and 6) ----

    [Fact]
    public async Task A_self_link_refused_inside_the_transaction_is_recorded_after_rollback_as_the_initiating_user()
    {
        var (initiator, _) = await AddUserAsync("initiator");
        var (owner, _) = await AddUserAsync("owner");
        var objectId = Guid.NewGuid().ToString();
        await LinkEntraAsync(owner, objectId);

        EntraAssociationOutcome outcome;
        await using (var db = _db.NewDb())
            outcome = await Association(db, new Principal(initiator)).CompleteLinkAsync(new UserId(initiator), new EntraIdentityAssertion(objectId, Tenant, "sub"));

        outcome.FailureCode.Should().Be(EntraLinkFailureCodes.Collision);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE UserId = N'{initiator}' AND Source = 0;")).Should().Be(0);
        var row = (await RowsAsync("external_identity", $"entra/{Tenant}/{objectId}")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.EntraLinkCollision);
        row["Actor"].Should().Be(initiator, "the initiating user carried in the protected sign-in state");
        row["Basis"].Should().Be("attempt");
        row["OutcomeCode"].Should().Be("rejected");
    }

    [Fact]
    public async Task Proving_a_pending_link_at_sign_in_targets_the_persisted_link_as_its_user_and_a_repeat_records_nothing()
    {
        var (user, _) = await AddUserAsync("pending");
        var objectId = Guid.NewGuid().ToString();
        var linkId = await LinkEntraAsync(user, objectId, proven: false);

        await using (var db = _db.NewDb())
            (await Association(db, null).CompleteSignInAsync(new EntraIdentityAssertion(objectId, Tenant, "sub"))).Succeeded.Should().BeTrue();
        await using (var db = _db.NewDb())
            (await Association(db, null).CompleteSignInAsync(new EntraIdentityAssertion(objectId, Tenant, "sub"))).Succeeded.Should().BeTrue();

        var row = (await RowsAsync("identity_link", linkId)).Should().ContainSingle("the second sign-in changed no security state").Which;
        row["Action"].Should().Be(AuditActionNames.EntraLinkProven);
        row["Actor"].Should().Be(user);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE Id = N'{linkId}' AND LastAuthenticatedAt IS NOT NULL;")).Should().Be(1);
    }

    [Fact]
    public async Task A_sign_in_by_an_unlinked_identity_is_recorded_as_that_external_identity()
    {
        var objectId = Guid.NewGuid().ToString();

        await using (var db = _db.NewDb())
            (await Association(db, null).CompleteSignInAsync(new EntraIdentityAssertion(objectId, Tenant, "sub"))).FailureCode
                .Should().Be(EntraLinkFailureCodes.AccountNotLinked);

        var row = (await RowsAsync("external_identity", $"entra/{Tenant}/{objectId}")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.EntraSignInRejectedUnlinked);
        row["ActorKind"].Should().Be("external");
        row["Actor"].Should().Be($"entra/{Tenant}/{objectId}");
    }

    // ---- Entra-primary policy (D6) ----

    [Fact]
    public async Task The_policy_disables_local_sign_in_with_its_row_or_not_at_all()
    {
        var (user, _) = await AddUserAsync("entraprimary");
        await using (var db = _db.NewDb())
            await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(user), "hash", "salt", "pbkdf2");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Policy(db).ApplyAfterEntraProvenAsync(new UserId(user)).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        (await _db.ScalarAsync<bool>($"SELECT IsDisabled FROM LocalCredentials WHERE UserId = N'{user}';")).Should().BeFalse();

        await using (var db = _db.NewDb())
            (await Policy(db).ApplyAfterEntraProvenAsync(new UserId(user))).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await _db.ScalarAsync<bool>($"SELECT IsDisabled FROM LocalCredentials WHERE UserId = N'{user}';")).Should().BeTrue();
        var row = (await RowsAsync("user", user)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.LocalDisabledEntraPrimary);
        row["Actor"].Should().Be("system:entra-primary-policy");
        row["ActorKind"].Should().Be("system");
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private async Task<string> LinkEntraAsync(string userId, string objectId, bool proven = true)
    {
        var linkId = IdentityLink.NewLinkId();
        await _db.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, RegistrationId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'{linkId}', N'{userId}', 0, N'{objectId}', N'{Tenant}', N'{RegistrationId}', SYSDATETIMEOFFSET(), {(proven ? "SYSDATETIMEOFFSET()" : "NULL")});
            """);
        return linkId;
    }

    private async Task<string> AddRequestAsync(string objectId)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        await using var db = _db.NewDb();
        await new SqlEntraAccessRequestRepository(db).AddAsync(new EntraAccessRequest(
            id, RegistrationId, Tenant, objectId, "Jane", "jane@contoso.com", "jane@contoso.com", "Member",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), EntraAccessRequestStatus.Pending));
        return id;
    }

    private SqlAuditEventWriter Events(IPrincipalContext? principal) =>
        new(_db.Options(), principal, HarnessCatalog, null, null, null);

    private UserProvisioningService Provisioning(
        AppertureDbContext db,
        IPrincipalContext principal,
        SqlAuditedUnitOfWork? unit = null,
        CountingMail? mail = null) =>
        new(
            principal,
            new AllowAll(),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlEntraInstanceRegistrationRepository(db),
            new NoProvisioningDirectory(),
            new NoAssociation(),
            new SqlEntraAccessRequestRepository(db),
            TimeProvider.System,
            mail ?? new CountingMail(),
            new AccountSecurityLinkBuilder(new ConfigurationBuilder().Build()),
            unit ?? NewUnit(db, principal),
            Events(principal));

    private EntraAccessRequestService AccessRequests(AppertureDbContext db) =>
        new(
            new NoPrincipal(),
            new AllowAll(),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlEntraInstanceRegistrationRepository(db),
            new MemberDiscovery(),
            new SqlEntraAccessRequestRepository(db),
            new NoProvisioning(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:ClientSecret"] = "deployment-secret" }).Build(),
            TimeProvider.System,
            auditedUnitOfWork: NewUnit(db, null));

    private EntraIdentityAssociationService Association(AppertureDbContext db, IPrincipalContext? principal) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlEntraInstanceRegistrationRepository(db),
            new NoDiscovery(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<EntraIdentityAssociationService>.Instance,
            auditedUnitOfWork: NewUnit(db, principal),
            auditEvents: Events(principal));

    private EntraPrimaryAuthenticationPolicy Policy(AppertureDbContext db) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new SqlRoleRepository(db),
            NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
            emergencyAccess: null,
            auditedUnitOfWork: NewUnit(db, null),
            auditEvents: Events(null));

    private Task<List<Dictionary<string, object?>>> RowsAsync(string targetType, string targetId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'{targetType}' AND TargetId = N'{targetId}' ORDER BY Id;");

    private sealed class CountingMail : IAccountSecurityMailSender
    {
        public int Sent { get; private set; }

        public ValueTask<AccountSecurityMailResult> SendAsync(string recipient, string subject, string plainTextBody, CancellationToken cancellationToken = default)
        {
            Sent++;
            return ValueTask.FromResult(new AccountSecurityMailResult(true, null));
        }
    }

    private sealed class AllowAll : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class NoPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IActor?>(null);
    }

    /// <summary>The directory is unavailable: snapshots are convenience only and association proceeds on tid and oid.</summary>
    private sealed class NoDiscovery : IEntraDirectoryDiscoveryService
    {
        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(false, DiscoveryFailureCategory.Transient, "unavailable", null));

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(false, DiscoveryFailureCategory.Transient, "unavailable", null));

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(false, DiscoveryFailureCategory.Transient, "unavailable", null));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(false, DiscoveryFailureCategory.Transient, "unavailable", null));
    }

    /// <summary>The directory confirms a member identity, so the access request is eligible.</summary>
    private sealed class MemberDiscovery : IEntraDirectoryDiscoveryService
    {
        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(true, DiscoveryFailureCategory.None, "ok", null, new EntraDirectoryUser(objectId, "Jane Smith", "jane@contoso.com", "jane@contoso.com", "Member", true)));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(true, DiscoveryFailureCategory.None, "ok", null, null));
    }

    private sealed class NoProvisioningDirectory : IEntraProvisioningDirectoryService
    {
        public ValueTask<EntraProvisioningDirectoryPage> SearchAsync(string registrationId, string? search, string? continuationToken, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(string registrationId, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoAssociation : IEntraIdentityAssociationService
    {
        public ValueTask<EntraAssociationOutcome> CompleteSignInAsync(EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<EntraAssociationOutcome> CompleteLinkAsync(UserId initiatingUserId, EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(string objectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(string objectId, EntraInstanceRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoProvisioning : IUserProvisioningService
    {
        public ValueTask<UserProvisioningResult> ProvisionFromEntraDirectoryAsync(EntraDirectoryProvisioningRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<UserProvisioningResult> ProvisionFromAccessRequestAsync(AccessRequestApprovalProvisioningRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
