using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP2 review, L-1: the initial Platform Administrator role of a provisioned person, through both real entry points of
// UserProvisioningService (ProvisionFromEntraDirectoryAsync and ProvisionFromAccessRequestAsync), against real SQL Server. The gate is
// taken before the conflicting authority locks and every write, once per attempt, and the raise-only assertion runs inside the audited
// transaction (its reads come after the role is written and before the audit row is inserted).
public sealed partial class AdministratorViabilityWp2SqlServerTests
{
    public enum ProvisioningEntry
    {
        /// <summary><c>UserProvisioningService.ProvisionFromEntraDirectoryAsync</c>.</summary>
        Directory,

        /// <summary><c>UserProvisioningService.ProvisionFromAccessRequestAsync</c>.</summary>
        AccessRequest,
    }

    public static IEnumerable<object[]> EveryProvisioningEntryOnBothModes() =>
        from entry in Enum.GetValues<ProvisioningEntry>()
        from rcsi in new[] { false, true }
        select new object[] { entry, rcsi };


    private const string RegistrationId = "reg-1111";

    private sealed class AllowEverything : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class KnownDirectory : IEntraProvisioningDirectoryService
    {
        public ValueTask<EntraProvisioningDirectoryPage> SearchAsync(string registrationId, string? search, string? continuationToken, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(string registrationId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<EntraProvisioningDirectoryUser?>(
                new EntraProvisioningDirectoryUser(objectId, "Jane Smith", "jane@contoso.com", "jane@contoso.com", "Member", true, EntraDirectoryAppertureStatus.Available, null));
    }

    private sealed class PendingLinkAssociation : IEntraIdentityAssociationService
    {
        public ValueTask<EntraAssociationOutcome> CompleteSignInAsync(EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<EntraAssociationOutcome> CompleteLinkAsync(UserId initiatingUserId, EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(string objectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(string objectId, EntraInstanceRegistration registration, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IdentityLink(
                IdentitySource.Entra, objectId.ToLowerInvariant(), LastAuthenticatedAt: null, TenantId: registration.AzureTenantId, RegistrationId: registration.Id));
    }

    private sealed class NoMail : IAccountSecurityMailSender
    {
        public ValueTask<AccountSecurityMailResult> SendAsync(string recipient, string subject, string plainTextBody, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AccountSecurityMailResult(true, null));
    }

    private static UserProvisioningService Provisioning(RoleAuthorityWorld world, AppertureDbContext db, string actor, IAdministratorViabilityCoordinator coordinator)
    {
        var principal = new Principal(actor);
        return new UserProvisioningService(
            principal,
            new AllowEverything(),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlEntraInstanceRegistrationRepository(db),
            new KnownDirectory(),
            new PendingLinkAssociation(),
            new SqlEntraAccessRequestRepository(db),
            TimeProvider.System,
            new NoMail(),
            new AccountSecurityLinkBuilder(new ConfigurationBuilder().Build()),
            NewUnit(db, principal),
            world.NewWriter(principal),
            descriptorRegistry: RoleAuthorityWorld.CatalogRegistry,
            viability: coordinator);
    }

    private static async Task<UserId> ProvisionAdministratorAsync(
        RoleAuthorityWorld world, ProvisioningEntry entry, AppertureDbContext db, string actor, IAdministratorViabilityCoordinator coordinator)
    {
        var service = Provisioning(world, db, actor, coordinator);
        var objectId = Guid.NewGuid().ToString();
        if (entry == ProvisioningEntry.Directory)
        {
            var result = await service.ProvisionFromEntraDirectoryAsync(new EntraDirectoryProvisioningRequest(RegistrationId, objectId, Admin));
            return result.UserId;
        }

        var requestId = Guid.NewGuid().ToString("N")[..24];
        await using (var requests = world.Scratch.NewDb())
        {
            await new SqlEntraAccessRequestRepository(requests).AddAsync(new EntraAccessRequest(
                requestId, RegistrationId, Tenant, objectId, "Jane", "jane@contoso.com", "jane@contoso.com", "Member",
                DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), EntraAccessRequestStatus.Pending));
        }

        return (await service.ProvisionFromAccessRequestAsync(new AccessRequestApprovalProvisioningRequest(requestId, Admin))).UserId;
    }

    private async Task<(RoleAuthorityWorld World, string Actor)> ProvisioningWorldAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        await LocalAdminAsync(world, "spare");
        return (world, await ActorAsync(world, "actor"));
    }

    [Theory]
    [MemberData(nameof(EveryProvisioningEntryOnBothModes))]
    public async Task L1_a_provisioned_administrator_takes_the_gate_before_the_authority_locks_and_asserts_inside_the_audited_transaction(ProvisioningEntry entry, bool rcsi)
    {
        var (world, actor) = await ProvisioningWorldAsync(rcsi);
        var log = new OrderedLog();

        await using var db = world.Scratch.NewDb(new LoggingStatements(log));
        var gate = new ProbingGate(new SqlPlatformAdministratorSetGate(db), log, db);
        var coordinator = new AdministratorViabilityCoordinator(
            gate, AdministratorViabilitySqlServerTests.Service(db, EntraReading));

        var userId = await ProvisionAdministratorAsync(world, entry, db, actor, coordinator);

        (await world.AssignmentExistsAsync(userId.Value, Admin)).Should().BeTrue();
        (await world.AuditRowsAsync(AuditActionNames.RoleAssigned, userId.Value)).Should().Be(1);

        var entries = log.Entries.ToList();
        entries.Count(e => e == GateEntry).Should().Be(1, "one gate acquisition for the one attempt");
        var gateAt = entries.IndexOf(GateEntry);
        gate.HeldBefore.Should().Be(1, "the operation lock is held when the gate is requested, and nothing else");
        gate.HeldAfter.Should().Be(2);
        entries.Take(gateAt).Should().NotContain(e => IsLockingOrWriting(e), "no authority lock and no write may precede the gate");

        var firstAuthorityLock = entries.FindIndex(e => e.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase));
        firstAuthorityLock.Should().BeGreaterThan(gateAt, "the authority row locks follow the gate");

        // The role is written, the state after is read, and only then is the operation's final audit row inserted (the provisioning row
        // staged earlier travels with the role insert): the assertion is inside the audited transaction.
        var roleInsert = entries.FindIndex(e => e.Contains("INSERT INTO [UserRoles]", StringComparison.Ordinal));
        var finalAuditInsert = entries.FindLastIndex(e => e.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        roleInsert.Should().BeGreaterThan(gateAt);
        finalAuditInsert.Should().BeGreaterThan(roleInsert);
        entries.Skip(roleInsert + 1).Take(finalAuditInsert - roleInsert - 1)
            .Should().Contain(e => e.Contains("[UserRoles]", StringComparison.Ordinal) && e.Contains("SELECT", StringComparison.Ordinal),
                "the facts after the change are read before the unit inserts its closing audit row");
        log.OutsideTransaction.Should().NotContain(position => position > gateAt && position <= finalAuditInsert,
            "everything from the gate to the unit's closing audit row, the viability reads included, runs on its transaction");
    }

    [Theory]
    [MemberData(nameof(EveryProvisioningEntryOnBothModes))]
    public async Task M2_a_provisioned_administrator_succeeds_when_the_authentication_environment_changes_between_the_reads(ProvisioningEntry entry, bool rcsi)
    {
        var (world, actor) = await ProvisioningWorldAsync(rcsi);

        await using var db = world.Scratch.NewDb();
        var coordinator = FlippingCoordinator(db, EntraReading, LocalSignInOff(), out var deployment);

        var userId = await ProvisionAdministratorAsync(world, entry, db, actor, coordinator);

        deployment.Reads.Should().Be(2);
        (await world.AssignmentExistsAsync(userId.Value, Admin)).Should().BeTrue("Local sign-in going off between the reads is not the provisioning's doing");
        (await world.AuditRowsAsync(AuditActionNames.RoleAssigned, userId.Value)).Should().Be(1);
    }
}
