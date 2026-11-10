using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5: the Entra-primary policy against the writers the WP4A pairs left out (A10.2b.1). D-A: the registration activated or
// switched to another directory, and the active registration edited to accept another directory. D-B: two applications of the policy for the
// same user, directly and through the proof at sign-in. And the removal of the same user's Entra link. Each pair runs in both orders, on both
// isolation modes, concurrently (the first writer parked after its decision, holding the gate) and one after the other. The sequential twins
// of the WP4A cross-package pairs (X1, X2) are added here too.
public sealed partial class EntraPrimaryPolicyViabilitySqlServerTests
{
    private static readonly string Refused = "refused:";

    // ------------------------------------------------------------------------------------------------ D-A: activate/switch, change tenant

    public enum DirectoryChange
    {
        /// <summary>Activate the second registration, which accepts another directory (a switch).</summary>
        Switch,

        /// <summary>Edit the active registration so that it accepts another directory.</summary>
        ChangeTenant,
    }

    /// <summary>
    /// <c>X</c> signs in locally and through Microsoft; <c>Y</c> only through Microsoft; Recovery Access is healthy, so only I-VIABLE decides.
    /// Disabling <c>X</c>'s Local credential leaves both on Microsoft; moving sign-in to another directory leaves <c>X</c> on its Local
    /// credential; both together leave nobody able to sign in.
    /// </summary>
    private sealed record DirectoryPair(RoleAuthorityWorld World, string X, string Y, string R1, string R2, string Actor)
    {
        public AuditSqlScratch Scratch => World.Scratch;
    }

    private async Task<DirectoryPair> DirectoryPairAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        var y = await EntraOnlyAdminAsync(world, "y");
        await RecoveryAccountAsync(world, healthy: true);
        var actor = await world.NewUserAsync("registration-actor");
        var r1 = (await world.Scratch.ScalarAsync<string>("SELECT Id FROM EntraInstanceRegistrations WHERE IsActive = 1;"))!;
        var r2 = "reg" + Guid.NewGuid().ToString("N")[..16];
        await world.Scratch.ExecuteAsync($"""
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, Enabled, IsActive, CreatedUtc, UpdatedUtc, AzureTenantId,
                 AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'{r2}', N'Other directory', N'https://login.microsoftonline.com/{OtherTenant}', N'client', 1, 1, 0,
                    SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{OtherTenant}', 0, 0);
            """);
        return new DirectoryPair(world, x, y, r1, r2, actor);
    }

    private static async Task<IActionResult> RunDirectoryChangeAsync(DirectoryPair p, DirectoryChange change, AppertureDbContext db)
    {
        var principal = new Principal(p.Actor);
        var controller = EntraRegistrationSqlHarness.Controller(
            p.Scratch,
            db,
            principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: Running(), resolver: EntraRegistrationSqlHarness.SecretResolver(db)),
            store: EntraRegistrationSqlHarness.SecretStore(db));
        if (change == DirectoryChange.Switch)
            return await controller.Activate(p.R2, CancellationToken.None);

        var item = await EntraRegistrationSqlHarness.GetAsync(p.Scratch, p.R1, principal);
        var body = EntraRegistrationSqlHarness.UpdateBody(item);
        body.Authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0";
        body.AzureTenantId = OtherTenant;
        return (await controller.Patch(p.R1, body, CancellationToken.None)).Result!;
    }

    private static async Task<bool> DirectoryChangedAsync(DirectoryPair p, DirectoryChange change) => change switch
    {
        DirectoryChange.Switch => await p.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{p.R2}';"),
        _ => await p.Scratch.ScalarAsync<string>($"SELECT AzureTenantId FROM EntraInstanceRegistrations WHERE Id = N'{p.R1}';") == OtherTenant,
    };

    private static string DirectoryTarget(DirectoryPair p, DirectoryChange change) => change == DirectoryChange.Switch ? p.R2 : p.R1;

    /// <summary>The highest audit row id so far: the active registration's id is the same in every case of the shared catalog, so its rows are read from here on.</summary>
    private static Task<long> AuditWatermarkAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<long>("SELECT CAST(ISNULL(MAX(Id), 0) AS bigint) FROM AuditLogs;");

    private static async Task<IReadOnlyList<string>> ChangeRefusalsAsync(AuditSqlScratch scratch, string registrationId, long since)
    {
        var rows = await scratch.RowsAsync(
            $"SELECT Details FROM AuditLogs WHERE Action = N'{AuditActionNames.TenantRegistrationChangeRefused}' AND TargetId = N'{registrationId}' AND Id > {since} ORDER BY Id;");
        return rows.Select(r =>
        {
            using var document = JsonDocument.Parse((string)r["Details"]!);
            return document.RootElement.GetProperty("change").GetString() + "/" + document.RootElement.GetProperty("reason").GetString();
        }).ToList();
    }

    private static string Outcome(IActionResult response) =>
        Succeeded(response) ? "applied" : Refused + ((int?)(response as ObjectResult)?.StatusCode ?? (response as StatusCodeResult)?.StatusCode) + ":" + CodeOf(response);

    public static IEnumerable<object[]> DirectoryChangePairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from change in Enum.GetValues<DirectoryChange>()
        from policyFirst in new[] { true, false }
        select new object[] { change, policyFirst, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(DirectoryChangePairs))]
    public async Task DA_the_policy_and_a_move_of_sign_in_to_another_directory_that_together_leave_nobody_commit_once(DirectoryChange change, bool policyFirst, bool concurrently, bool rcsi)
    {
        var p = await DirectoryPairAsync(rcsi);
        var since = await AuditWatermarkAsync(p.Scratch);

        Task<string> Policy(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? p.Scratch.NewDb() : p.Scratch.NewDb(pause);
            return "policy:" + await PolicyOver(p.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(p.X));
        });
        Task<string> Move(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? p.Scratch.NewDb() : p.Scratch.NewDb(pause);
            return Outcome(await RunDirectoryChangeAsync(p, change, db));
        });

        string firstOutcome, secondOutcome;
        if (concurrently)
        {
            var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
            var first = policyFirst ? Policy(pause) : Move(pause);
            await pause.Reached.WaitAsync(Patience);
            (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1, "the first writer holds the administrator-set gate");
            var second = policyFirst ? Move(null) : Policy(null);
            await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, second);
            (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the second writer waits for the application lock, not for a row");
            pause.Release();
            firstOutcome = await first.WaitAsync(Patience);
            secondOutcome = await second.WaitAsync(Patience);
        }
        else
        {
            firstOutcome = await (policyFirst ? Policy(null) : Move(null));
            secondOutcome = await (policyFirst ? Move(null) : Policy(null));
        }

        if (policyFirst)
        {
            firstOutcome.Should().Be("policy:" + EntraPrimaryLocalDisableOutcome.Disabled, "with healthy Recovery Access the policy is allowed on its own");
            secondOutcome.Should().Be(Refused + "409:last_viable_sign_in_method", "after the disable, both administrators depend on the directory the change would leave");
            (await p.World.CredentialDisabledAsync(p.X)).Should().BeTrue();
            (await DirectoryChangedAsync(p, change)).Should().BeFalse("the refused change was rolled back");
            (await ChangeRefusalsAsync(p.Scratch, DirectoryTarget(p, change), since)).Should().Equal((change == DirectoryChange.Switch ? "activate" : "update") + "/sign_in_lockout");
            (await SuccessRowsAsync(p.World, p.X)).Should().Be(1);
            (await ViabilityAsync(p.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.X, p.Y], "both still sign in through Microsoft");
        }
        else
        {
            firstOutcome.Should().Be("applied", "with X's Local credential remaining the move is allowed on its own");
            secondOutcome.Should().Be("policy:" + EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator,
                "after the move, X's Local credential is the only way in");
            (await DirectoryChangedAsync(p, change)).Should().BeTrue();
            (await p.World.CredentialDisabledAsync(p.X)).Should().BeFalse("the deferred disable was rolled back");
            (await DeferralRowsAsync(p.World, p.X)).Should().Be(1);
            (await SuccessRowsAsync(p.World, p.X)).Should().Be(0);
            (await ChangeRefusalsAsync(p.Scratch, DirectoryTarget(p, change), since)).Should().BeEmpty();
        }
    }

    // ------------------------------------------------------------------------------------------------ D-B: the same user twice

    /// <summary><c>X</c> signs in locally and through Microsoft; <c>Z</c> signs in locally, so disabling <c>X</c>'s Local credential is allowed.</summary>
    private async Task<(RoleAuthorityWorld World, string X)> SameUserAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        await LocalOnlyAdminAsync(world, "z");
        return (world, x);
    }

    public static IEnumerable<object[]> SameUserCallersOnBothModes() =>
        from rcsi in new[] { false, true }
        from secondThroughSignIn in new[] { false, true }
        select new object[] { secondThroughSignIn, rcsi };

    /// <summary>Directory discovery is not part of a sign-in; a call would be a defect of the case.</summary>
    private sealed class NoDirectoryDiscovery : IEntraDirectoryDiscoveryService
    {
        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A successful Entra sign-in through a proven link, as the host completes it: the link's proof is refreshed, then the policy is applied.</summary>
    private static async Task<EntraAssociationOutcome> CompleteSignInAsync(
        AuditSqlScratch scratch, AppertureDbContext db, string objectId, Microsoft.Extensions.Logging.ILogger<EntraIdentityAssociationService> log) =>
        await new EntraIdentityAssociationService(
                new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
                new SqlEntraInstanceRegistrationRepository(db),
                new NoDirectoryDiscovery(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TimeProvider.System,
                log,
                entraPrimaryPolicy: PolicyOver(scratch, db),
                auditedUnitOfWork: NewUnit(db, null),
                auditEvents: EventsOf(scratch))
            .CompleteSignInAsync(new EntraIdentityAssertion(objectId, Tenant, "sub"));

    [Theory]
    [MemberData(nameof(SameUserCallersOnBothModes))]
    public async Task DB_two_applications_for_the_same_user_disable_the_credential_once_and_the_second_finds_nothing_to_do(bool secondThroughSignIn, bool rcsi)
    {
        var (world, x) = await SameUserAsync(rcsi);
        // A sign-in carries a directory object id (a GUID), and the link must hold it.
        var objectId = Guid.NewGuid().ToString("D");
        await world.Scratch.ExecuteAsync($"UPDATE IdentityLinks SET ExternalId = N'{objectId}' WHERE UserId = N'{x}' AND Source = 0;");

        // The first application has disabled the credential and holds the gate, not committed.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(x));
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(world.Scratch)).Should().Be(-1);

        // The second: another application, or a successful Entra sign-in of the same user (which refreshes the link and then applies the policy).
        var log = new CapturingLogger<EntraIdentityAssociationService>();
        var second = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb();
            if (!secondThroughSignIn)
                return (object)await PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(x));
            return (object)(await CompleteSignInAsync(world.Scratch, db, objectId, log)).UserId!;
        });

        // Under row versioning the second's read without a lock sees the committed, enabled credential, so it opens its unit and queues on
        // the gate. Under locking READ COMMITTED that read waits on the first's uncommitted row. Either way it is held until the first commits.
        var deadline = DateTime.UtcNow.Add(Patience);
        while (true)
        {
            second.IsCompleted.Should().BeFalse("the second application must be held until the first commits");
            var onGate = await EntraRegistrationViabilityWp3SqlServerTests.ApplicationLockWaitersAsync(world.Scratch);
            var onRow = await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(world.Scratch);
            if (rcsi && onGate == 1)
            {
                onRow.Should().Be(0, "under row versioning it waits for the gate, not for a row");
                break;
            }

            if (!rcsi && onRow > 0)
            {
                onGate.Should().Be(0, "under locking READ COMMITTED its read waits on the first's row before it reaches the gate");
                break;
            }

            DateTime.UtcNow.Should().BeBefore(deadline);
            await Task.Delay(25);
        }

        pause.Release();
        (await first.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        var secondResult = await second.WaitAsync(Patience);
        if (secondThroughSignIn)
        {
            secondResult.Should().Be(new UserId(x), "the sign-in resolves");
            log.Entries.Should().NotContain(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning, "finding the credential already disabled is not a warning");
        }
        else
        {
            secondResult.Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled, "the second finds the credential already disabled");
        }

        (await world.CredentialDisabledAsync(x)).Should().BeTrue();
        (await SuccessRowsAsync(world, x)).Should().Be(1, "the credential was disabled once");
        (await DeferralRowsAsync(world, x)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task DB_the_same_two_applications_one_after_the_other_give_the_same_result(bool rcsi)
    {
        var (world, x) = await SameUserAsync(rcsi);

        (await ApplyAsync(world.Scratch, x)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await ApplyAsync(world.Scratch, x)).Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        (await SuccessRowsAsync(world, x)).Should().Be(1);
        (await DeferralRowsAsync(world, x)).Should().Be(0);
    }

    // ------------------------------------------------------------------------------------------------ the same user's Entra link removed

    public static IEnumerable<object[]> EntraLinkRemovalPairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from policyFirst in new[] { true, false }
        select new object[] { policyFirst, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(EntraLinkRemovalPairs))]
    public async Task EL_the_policy_and_the_removal_of_the_same_administrators_entra_link_never_both_commit(bool policyFirst, bool concurrently, bool rcsi)
    {
        // X is the one ordinary administrator, signing in both locally and through Microsoft; Recovery Access is healthy, so only I-VIABLE
        // decides. The policy alone leaves X the Microsoft link; the link removal alone leaves X the Local credential; together, nothing.
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        await RecoveryAccountAsync(world, healthy: true);
        var actor = await world.NewUserAsync("link-actor", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor}';");

        Task<string> Policy(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
            return "policy:" + await PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(x));
        });
        Task<string> Unlink(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
            try
            {
                await world.Service(db, actor, advisoryAllowsAll: true, deployment: Running(), strongAuthentication: new EntraRegistrationViabilityWp3SqlServerTests.AllowingStrongAuthentication())
                    .RemoveIdentityLinkAsync(new UserId(x), IdentitySource.Entra, "oid-" + x);
                return "applied";
            }
            catch (GovernanceConflictException refusal)
            {
                return Refused + refusal.Code;
            }
        });

        string firstOutcome, secondOutcome;
        if (concurrently)
        {
            var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
            var first = policyFirst ? Policy(pause) : Unlink(pause);
            await pause.Reached.WaitAsync(Patience);
            (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(world.Scratch)).Should().Be(-1);
            var second = policyFirst ? Unlink(null) : Policy(null);
            await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(world.Scratch, 1, second);
            (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(world.Scratch)).Should().Be(0);
            pause.Release();
            firstOutcome = await first.WaitAsync(Patience);
            secondOutcome = await second.WaitAsync(Patience);
        }
        else
        {
            firstOutcome = await (policyFirst ? Policy(null) : Unlink(null));
            secondOutcome = await (policyFirst ? Unlink(null) : Policy(null));
        }

        if (policyFirst)
        {
            firstOutcome.Should().Be("policy:" + EntraPrimaryLocalDisableOutcome.Disabled);
            secondOutcome.Should().Be(Refused + "last_viable_sign_in_method", "the Microsoft link is now X's only way in");
            (await world.LinkCountAsync(x, 0)).Should().Be(1, "the refused removal was rolled back");
            (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "E" + x)).Should().Be(1);
            (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "E" + x)).Should().Be(0);
        }
        else
        {
            firstOutcome.Should().Be("applied");
            secondOutcome.Should().Be("policy:" + EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator, "the Local credential is now X's only way in");
            (await world.CredentialDisabledAsync(x)).Should().BeFalse();
            (await DeferralRowsAsync(world, x)).Should().Be(1);
            (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "E" + x)).Should().Be(1);
        }

        (await ViabilityAsync(world.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([x], "X can still sign in");
    }

    // ------------------------------------------------------------------------------------------------ sequential twins of X1 and X2

    [Theory]
    [MemberData(nameof(OtherOpsOnBothModes))]
    public async Task X1_the_policy_then_the_administrator_change_one_after_the_other_give_the_same_result(OtherOp op, bool rcsi)
    {
        var p = await Wp2PairAsync(rcsi);

        (await ApplyAsync(p.Scratch, p.X)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        await using var db = p.Scratch.NewDb();
        (await ((Func<Task>)(() => RunOtherAsync(p, op, db))).Should().ThrowAsync<GovernanceConflictException>())
            .Which.Code.Should().Be("entra_independent_fallback_required");

        (await OtherAppliedAsync(p, op)).Should().BeFalse();
        (await SuccessRowsAsync(p.World, p.X)).Should().Be(1);
        (await ViabilityAsync(p.Scratch)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([p.Y]);
    }

    [Theory]
    [MemberData(nameof(OtherOpsOnBothModes))]
    public async Task X1_the_administrator_change_then_the_policy_one_after_the_other_give_the_same_result(OtherOp op, bool rcsi)
    {
        var p = await Wp2PairAsync(rcsi);

        await using (var db = p.Scratch.NewDb())
            await RunOtherAsync(p, op, db);
        (await ApplyAsync(p.Scratch, p.X)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);

        (await OtherAppliedAsync(p, op)).Should().BeTrue();
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeFalse();
        (await DeferralRowsAsync(p.World, p.X)).Should().Be(1);
        (await ViabilityAsync(p.Scratch)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([p.X]);
    }

    [Theory]
    [MemberData(nameof(RegistrationOpsOnBothModes))]
    public async Task X2_the_policy_then_the_registration_change_one_after_the_other_give_the_same_result(RegistrationOp op, bool rcsi)
    {
        var p = await RegistrationPairAsync(rcsi);

        (await ApplyAsync(p.Scratch, p.X)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        await using var db = p.Scratch.NewDb();
        var response = await RunRegistrationAsync(p, op, db);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await RegistrationAppliedAsync(p, op)).Should().BeFalse();
        (await ViabilityAsync(p.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.X, p.Y]);
    }

    [Theory]
    [MemberData(nameof(RegistrationOpsOnBothModes))]
    public async Task X2_the_registration_change_then_the_policy_one_after_the_other_give_the_same_result(RegistrationOp op, bool rcsi)
    {
        var p = await RegistrationPairAsync(rcsi);

        await using (var db = p.Scratch.NewDb())
            Succeeded(await RunRegistrationAsync(p, op, db)).Should().BeTrue();
        (await ApplyAsync(p.Scratch, p.X)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);

        (await RegistrationAppliedAsync(p, op)).Should().BeTrue();
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeFalse();
        (await DeferralRowsAsync(p.World, p.X)).Should().Be(1);
    }
}
