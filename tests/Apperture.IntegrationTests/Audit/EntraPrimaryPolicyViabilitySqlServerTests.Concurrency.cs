using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP4A, A10.2b.9 and A10.2b.10: the Entra-primary policy against real interleavings. Every case forces the overlap: the
// first writer is parked inside its unit, with everything it holds (the operation lock, the global gate and its row locks), after its
// change and before its commit, until the second is shown to be waiting; the wait is shown to be on the application lock and on no
// row. A control with the gate removed shows what the same interleaving does without it.
public sealed partial class EntraPrimaryPolicyViabilitySqlServerTests
{
    private sealed record Pair(RoleAuthorityWorld World, string X, string Y)
    {
        public AuditSqlScratch Scratch => World.Scratch;
    }

    /// <summary>Two ordinary administrators, each with a Local credential and a proven Entra link, and nobody to fall back on.</summary>
    private async Task<Pair> TwoBothWaysAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        var y = await BothWaysAdminAsync(world, "y");
        return new Pair(world, x, y);
    }

    public static IEnumerable<object[]> BothTargetOrdersOnBothModes() =>
        from rcsi in new[] { false, true }
        from firstIsX in new[] { true, false }
        select new object[] { firstIsX, rcsi };

    // ---------------------------------------------------------------------------------------------- the required race

    [Theory]
    [MemberData(nameof(BothTargetOrdersOnBothModes))]
    public async Task A9_two_applications_for_two_administrators_disable_exactly_one_and_defer_the_other(bool firstIsX, bool rcsi)
    {
        var p = await TwoBothWaysAsync(rcsi);
        var (first, second) = firstIsX ? (p.X, p.Y) : (p.Y, p.X);

        // The first application runs everything but the commit: it holds the gate and its row locks and has read the state its change leaves.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var firstTask = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            return await PolicyOver(p.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(first));
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1, "the first application holds the administrator-set gate");
        (await EntraRegistrationViabilityWp3SqlServerTests.GrantedApplicationLocksAsync(p.Scratch)).Should().BeGreaterThanOrEqualTo(2, "its operation lock and the gate");

        var secondTask = Task.Run(() => ApplyAsync(p.Scratch, second));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, secondTask);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the second application waits for the application lock, not for a row: it has read and locked nothing");

        pause.Release();
        (await firstTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await secondTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator, "after the first commit the second is the last administrator who signs in without Microsoft");

        (await p.World.CredentialDisabledAsync(first)).Should().BeTrue();
        (await p.World.CredentialDisabledAsync(second)).Should().BeFalse("exactly one Local credential is disabled");
        (await SuccessRowsAsync(p.World, first)).Should().Be(1);
        (await SuccessRowsAsync(p.World, second)).Should().Be(0);
        (await DeferralRowsAsync(p.World, second)).Should().Be(1, "the W3 attempt row of the deferred application");
        (await DeferralRowsAsync(p.World, first)).Should().Be(0);
        var state = await ViabilityAsync(p.Scratch);
        state.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([second], "an ordinary administrator who signs in without Microsoft remains");
        state.Restart.EntraIndependentAdministrators.Should().BeEquivalentTo([second]);
    }

    [Theory]
    [MemberData(nameof(BothTargetOrdersOnBothModes))]
    public async Task A9_the_same_two_applied_one_after_the_other_give_the_same_result(bool firstIsX, bool rcsi)
    {
        var p = await TwoBothWaysAsync(rcsi);
        var (first, second) = firstIsX ? (p.X, p.Y) : (p.Y, p.X);

        (await ApplyAsync(p.Scratch, first)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await ApplyAsync(p.Scratch, second)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);

        (await p.World.CredentialDisabledAsync(first)).Should().BeTrue();
        (await p.World.CredentialDisabledAsync(second)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_with_the_gate_removed_the_same_interleaving_is_not_serialized_and_under_row_versioning_disables_both(bool rcsi)
    {
        var p = await TwoBothWaysAsync(rcsi);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var firstTask = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            return await PolicyOver(p.Scratch, db, gate: new AdministratorViabilityWp2SqlServerTests.NoGate()).ApplyAfterEntraProvenAsync(new UserId(p.X));
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().BeGreaterThanOrEqualTo(0, "this application never took the gate");

        var secondTask = Task.Run(() => ApplyAsync(p.Scratch, p.Y, gate: new AdministratorViabilityWp2SqlServerTests.NoGate()));

        if (rcsi)
        {
            // Under row versioning nothing else stands in the second application's way: it reads the committed past, decides, and commits.
            (await secondTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
            pause.Release();
            (await firstTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);

            (await p.World.CredentialDisabledAsync(p.X)).Should().BeTrue();
            (await p.World.CredentialDisabledAsync(p.Y)).Should().BeTrue("both were allowed: this is the hole the gate closes");
            (await ViabilityAsync(p.Scratch)).Now.EntraIndependentAdministrators.Should().BeEmpty("without the gate nobody is left who signs in without Microsoft");
        }
        else
        {
            // Under locking READ COMMITTED the second application's reads are held up by the first one's uncommitted row: protection by a
            // row lock, which is incidental (it depends on what the writers happen to touch) and is not the gate.
            var deadline = DateTime.UtcNow.Add(Patience);
            while (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch) == 0)
            {
                secondTask.IsCompleted.Should().BeFalse("the second application must wait on a row");
                DateTime.UtcNow.Should().BeBefore(deadline);
                await Task.Delay(25);
            }

            (await EntraRegistrationViabilityWp3SqlServerTests.ApplicationLockWaitersAsync(p.Scratch)).Should().Be(0, "it waits on a row, not on the administrator-set gate");
            pause.Release();
            (await firstTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
            (await secondTask.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        }
    }

    // ------------------------------------------------------------------------- a WP2 administrator writer against the policy

    public enum OtherOp
    {
        RemoveRole,
        DisableCredential,
        RemoveLocalLink,
    }

    /// <summary>
    /// <c>X</c> signs in locally and through Microsoft; <c>Y</c> signs in only locally. Disabling <c>X</c>'s Local credential leaves
    /// <c>Y</c>; any change that stops <c>Y</c> signing in locally leaves <c>X</c>; both together leave nobody who signs in without
    /// Microsoft, while Recovery Access cannot stand in.
    /// </summary>
    private sealed record Wp2Pair(RoleAuthorityWorld World, string X, string Y, string Wp2Actor)
    {
        public AuditSqlScratch Scratch => World.Scratch;
    }

    private async Task<Wp2Pair> Wp2PairAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        var y = await LocalOnlyAdminAsync(world, "y");

        // A second link (pending, so not a way in) lets the Local link go without emptying the user.
        await world.AddEntraLinkAsync(y, Tenant, proven: false);

        // The administrator writer's actor holds the role (removing it takes authority over it) but cannot sign in, so it counts for nothing.
        var actor = await world.NewUserAsync("wp2-actor", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor}';");
        return new Wp2Pair(world, x, y, actor);
    }

    private static async Task RunOtherAsync(Wp2Pair p, OtherOp op, AppertureDbContext db)
    {
        var service = p.World.Service(
            db,
            p.Wp2Actor,
            advisoryAllowsAll: true,
            deployment: Running(),
            strongAuthentication: new EntraRegistrationViabilityWp3SqlServerTests.AllowingStrongAuthentication());
        switch (op)
        {
            case OtherOp.RemoveRole:
                await service.RemoveRoleAsync(new UserId(p.Y), new RoleId(Admin));
                break;
            case OtherOp.DisableCredential:
                await service.SetLocalCredentialDisabledAsync(new UserId(p.Y), disabled: true);
                break;
            default:
                await service.RemoveIdentityLinkAsync(
                    new UserId(p.Y), IdentitySource.Local, await p.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{p.Y}';"));
                break;
        }
    }

    private static async Task<bool> OtherAppliedAsync(Wp2Pair p, OtherOp op) => op switch
    {
        OtherOp.RemoveRole => !await p.World.AssignmentExistsAsync(p.Y, Admin),
        OtherOp.DisableCredential => await p.World.CredentialDisabledAsync(p.Y),
        _ => await p.World.LinkCountAsync(p.Y, 1) == 0,
    };

    public static IEnumerable<object[]> OtherOpsOnBothModes() =>
        from rcsi in new[] { false, true }
        from op in Enum.GetValues<OtherOp>()
        select new object[] { op, rcsi };

    [Theory]
    [MemberData(nameof(OtherOpsOnBothModes))]
    public async Task X1_the_policy_then_an_administrator_change_that_together_leave_nobody_without_microsoft_commit_once_and_refuse_once(OtherOp op, bool rcsi)
    {
        var p = await Wp2PairAsync(rcsi);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            return await PolicyOver(p.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(p.X));
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1);

        await using var dbB = p.Scratch.NewDb();
        var second = Task.Run(() => RunOtherAsync(p, op, dbB));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, second);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the administrator writer waits for the application lock, not for a row");

        pause.Release();
        (await first.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        var refusal = (await ((Func<Task>)(() => second.WaitAsync(Patience))).Should().ThrowAsync<GovernanceConflictException>()).Which;

        refusal.Code.Should().Be("entra_independent_fallback_required");
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeTrue("the policy committed first");
        (await OtherAppliedAsync(p, op)).Should().BeFalse("the administrator change was refused after the policy and rolled back");
        (await SuccessRowsAsync(p.World, p.X)).Should().Be(1);
        (await ViabilityAsync(p.Scratch)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([p.Y]);
    }

    [Theory]
    [MemberData(nameof(OtherOpsOnBothModes))]
    public async Task X1_an_administrator_change_then_the_policy_that_together_leave_nobody_without_microsoft_commit_once_and_defer_once(OtherOp op, bool rcsi)
    {
        var p = await Wp2PairAsync(rcsi);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            await RunOtherAsync(p, op, db);
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1);

        var second = Task.Run(() => ApplyAsync(p.Scratch, p.X));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, second);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the policy waits for the application lock, not for a row");

        pause.Release();
        await first.WaitAsync(Patience);

        (await second.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        (await OtherAppliedAsync(p, op)).Should().BeTrue("the administrator change committed first");
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeFalse("the policy deferred and rolled its change back");
        (await DeferralRowsAsync(p.World, p.X)).Should().Be(1);
        (await SuccessRowsAsync(p.World, p.X)).Should().Be(0);
        (await ViabilityAsync(p.Scratch)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([p.X]);
    }

    // ------------------------------------------------------------------------ a WP3 registration writer against the policy

    public enum RegistrationOp
    {
        Delete,
        Disable,
    }

    /// <summary>
    /// <c>X</c> signs in locally and through Microsoft; <c>Y</c> signs in only through Microsoft; Recovery Access is healthy, so the
    /// fallback invariant holds whatever happens. Disabling <c>X</c>'s Local credential leaves both on Microsoft; removing the
    /// registration leaves <c>X</c> with Local; both together leave nobody able to sign in at all.
    /// </summary>
    private sealed record RegistrationPair(RoleAuthorityWorld World, string X, string Y, string RegistrationId, string Actor)
    {
        public AuditSqlScratch Scratch => World.Scratch;
    }

    private async Task<RegistrationPair> RegistrationPairAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var x = await BothWaysAdminAsync(world, "x");
        var y = await EntraOnlyAdminAsync(world, "y");
        await RecoveryAccountAsync(world, healthy: true);
        var actor = await world.NewUserAsync("registration-actor");
        var registrationId = (await world.Scratch.ScalarAsync<string>("SELECT Id FROM EntraInstanceRegistrations WHERE IsActive = 1;"))!;
        return new RegistrationPair(world, x, y, registrationId, actor);
    }

    private static async Task<IActionResult> RunRegistrationAsync(RegistrationPair p, RegistrationOp op, AppertureDbContext db)
    {
        var principal = new Principal(p.Actor);
        var controller = EntraRegistrationSqlHarness.Controller(
            p.Scratch,
            db,
            principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: Running(), resolver: EntraRegistrationSqlHarness.SecretResolver(db)),
            store: EntraRegistrationSqlHarness.SecretStore(db));
        if (op == RegistrationOp.Delete)
            return await controller.Delete(p.RegistrationId, CancellationToken.None);

        var item = await EntraRegistrationSqlHarness.GetAsync(p.Scratch, p.RegistrationId, principal);
        var body = EntraRegistrationSqlHarness.UpdateBody(item);
        body.Enabled = false;
        return (await controller.Patch(p.RegistrationId, body, CancellationToken.None)).Result!;
    }

    private static async Task<bool> RegistrationAppliedAsync(RegistrationPair p, RegistrationOp op) => op switch
    {
        RegistrationOp.Delete => await p.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{p.RegistrationId}';") == 0,
        _ => !await p.Scratch.ScalarAsync<bool>($"SELECT Enabled FROM EntraInstanceRegistrations WHERE Id = N'{p.RegistrationId}';"),
    };

    private static string? CodeOf(IActionResult result)
    {
        if (result is not ObjectResult { Value: { } value })
            return null;
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value));
        return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static IEnumerable<object[]> RegistrationOpsOnBothModes() =>
        from rcsi in new[] { false, true }
        from op in Enum.GetValues<RegistrationOp>()
        select new object[] { op, rcsi };

    [Theory]
    [MemberData(nameof(RegistrationOpsOnBothModes))]
    public async Task X2_the_policy_then_a_registration_change_that_together_leave_nobody_able_to_sign_in_commit_once_and_refuse_once(RegistrationOp op, bool rcsi)
    {
        var p = await RegistrationPairAsync(rcsi);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            return await PolicyOver(p.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(p.X));
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1);

        await using var dbR = p.Scratch.NewDb();
        var second = Task.Run(() => RunRegistrationAsync(p, op, dbR));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, second);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the registration writer waits for the application lock, not for a row");

        pause.Release();
        (await first.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "with healthy Recovery Access the policy is allowed on its own");
        var response = await second.WaitAsync(Patience);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeTrue("the policy committed first");
        (await RegistrationAppliedAsync(p, op)).Should().BeFalse("the registration change was refused after the policy and rolled back");
        var state = await ViabilityAsync(p.Scratch);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.X, p.Y], "both still sign in through Microsoft");
    }

    [Theory]
    [MemberData(nameof(RegistrationOpsOnBothModes))]
    public async Task X2_a_registration_change_then_the_policy_that_together_leave_nobody_able_to_sign_in_commit_once_and_defer_once(RegistrationOp op, bool rcsi)
    {
        var p = await RegistrationPairAsync(rcsi);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = p.Scratch.NewDb(pause);
            var response = await RunRegistrationAsync(p, op, db);
            Succeeded(response).Should().BeTrue($"the first writer is allowed on its own: {CodeOf(response)}");
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(p.Scratch)).Should().Be(-1);

        var second = Task.Run(() => ApplyAsync(p.Scratch, p.X));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(p.Scratch, 1, second);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(p.Scratch)).Should().Be(0, "the policy waits for the application lock, not for a row");

        pause.Release();
        await first.WaitAsync(Patience);

        (await second.WaitAsync(Patience)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator, "without the registration X's Local credential is the only way in");
        (await RegistrationAppliedAsync(p, op)).Should().BeTrue("the registration change committed first");
        (await p.World.CredentialDisabledAsync(p.X)).Should().BeFalse();
        (await DeferralRowsAsync(p.World, p.X)).Should().Be(1);
        (await SuccessRowsAsync(p.World, p.X)).Should().Be(0);
        (await ViabilityAsync(p.Scratch, AdministratorViabilitySqlServerTests.Reading(null))).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.X]);
    }

    private static bool Succeeded(IActionResult result) => result is OkObjectResult or NoContentResult or CreatedAtActionResult;
}
