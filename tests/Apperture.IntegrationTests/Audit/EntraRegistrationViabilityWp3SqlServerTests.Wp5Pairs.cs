using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5 (A10.2b.1): the registration writers against each other and against the removal of an Entra link, which the WP3 pairs did
// not run, and the sequential twin of the WP3 administrator-first order. Each pair runs in both orders, on both isolation modes, concurrently
// (the first writer parked after its decision, holding the gate) and one after the other.
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests
{
    public enum DirectoryOp
    {
        /// <summary>Activate R2, which accepts the other directory (a switch).</summary>
        Switch,

        /// <summary>Edit R1, the active registration, to accept the other directory.</summary>
        ChangeTenant,

        /// <summary>Disable R2 (harmless while it is inactive).</summary>
        DisableSecond,

        /// <summary>Delete R2 (harmless while it is inactive).</summary>
        DeleteSecond,

        /// <summary>An administrator removes B's Entra link (harmless while B cannot sign in).</summary>
        RemoveEntraLinkOfB,
    }

    /// <summary>
    /// <c>A</c> signs in only through the active registration R1 (<see cref="Tenant"/>); <c>B</c> only through a proven link for
    /// <see cref="OtherTenant"/>, which the inactive R2 accepts; Recovery Access is healthy, so only I-VIABLE decides. Moving sign-in to the
    /// other directory leaves <c>B</c>; taking R2 or <c>B</c>'s link away leaves <c>A</c>; the two together leave nobody.
    /// </summary>
    private sealed record Directories(Scenario S, string B, string Wp2Actor);

    private async Task<Directories> DirectoriesAsync(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var b = await EntraOnlyAdminAsync(s.World, "b", OtherTenant);
        var actor = await s.World.NewUserAsync("wp2-actor", Admin);
        await s.World.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor}';");
        return new Directories(s, b, actor);
    }

    /// <summary>
    /// Runs one change on its own context and says what became of it: <c>applied</c>, <c>&lt;status&gt;:&lt;code&gt;</c>, or
    /// <c>refused:&lt;code&gt;</c>. An edit sends the registration as <paramref name="loaded"/> gives it (with its row version), as a client
    /// that loaded the page sends it; without one it loads the registration first.
    /// </summary>
    private static async Task<string> RunDirectoryOpAsync(
        Directories d, DirectoryOp op, PauseAt? pause, IReadOnlyDictionary<string, EntraTenantListItemResponse>? loaded = null)
    {
        await using var db = pause is null ? d.S.Scratch.NewDb() : d.S.Scratch.NewDb(pause);
        var s = d.S;
        async Task<EntraTenantListItemResponse> ItemOf(string id) => loaded?[id] ?? await ItemAsync(s, id);
        IActionResult response;
        switch (op)
        {
            case DirectoryOp.Switch:
                response = await Tenants(s, db).Activate(s.R2!, CancellationToken.None);
                break;
            case DirectoryOp.ChangeTenant:
            {
                var item = await ItemOf(s.R1);
                response = (await Tenants(s, db).Patch(
                    s.R1,
                    Update(item, b =>
                    {
                        b.Authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0";
                        b.AzureTenantId = OtherTenant;
                    }),
                    CancellationToken.None)).Result!;
                break;
            }

            case DirectoryOp.DisableSecond:
            {
                var item = await ItemOf(s.R2!);
                response = (await Tenants(s, db).Patch(s.R2!, Update(item, b => b.Enabled = false), CancellationToken.None)).Result!;
                break;
            }

            case DirectoryOp.DeleteSecond:
                response = await Tenants(s, db).Delete(s.R2!, CancellationToken.None);
                break;
            default:
                try
                {
                    await s.World.Service(db, d.Wp2Actor, advisoryAllowsAll: true, deployment: s.Reading, strongAuthentication: new AllowingStrongAuthentication())
                        .RemoveIdentityLinkAsync(new UserId(d.B), IdentitySource.Entra, "oid-" + d.B);
                    return "applied";
                }
                catch (GovernanceConflictException refusal)
                {
                    return "refused:" + refusal.Code;
                }
        }

        if (Succeeded(response))
            return "applied";
        var status = (response as ObjectResult)?.StatusCode ?? (response as StatusCodeResult)?.StatusCode;
        return $"{status}:{CodeOf(response)}";
    }

    public static IEnumerable<object[]> DirectoryPairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from pair in new[]
        {
            (DirectoryOp.Switch, DirectoryOp.DisableSecond), (DirectoryOp.DisableSecond, DirectoryOp.Switch),
            (DirectoryOp.Switch, DirectoryOp.DeleteSecond), (DirectoryOp.DeleteSecond, DirectoryOp.Switch),
            (DirectoryOp.Switch, DirectoryOp.RemoveEntraLinkOfB), (DirectoryOp.RemoveEntraLinkOfB, DirectoryOp.Switch),
            (DirectoryOp.ChangeTenant, DirectoryOp.RemoveEntraLinkOfB), (DirectoryOp.RemoveEntraLinkOfB, DirectoryOp.ChangeTenant),
        }
        select new object[] { pair.Item1, pair.Item2, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(DirectoryPairs))]
    public async Task W5_two_changes_that_together_leave_nobody_able_to_sign_in_never_both_commit(DirectoryOp first, DirectoryOp second, bool concurrently, bool rcsi)
    {
        var d = await DirectoriesAsync(rcsi);
        var scratch = d.S.Scratch;

        string firstOutcome, secondOutcome;
        if (concurrently)
        {
            // Both operators loaded the registrations before either acted (a read under locking READ COMMITTED would otherwise wait on
            // the first writer's uncommitted row, outside any unit).
            var loaded = new Dictionary<string, EntraTenantListItemResponse>
            {
                [d.S.R1] = await ItemAsync(d.S, d.S.R1),
                [d.S.R2!] = await ItemAsync(d.S, d.S.R2!),
            };
            var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
            var a = Task.Run(() => RunDirectoryOpAsync(d, first, pause, loaded));
            var reached = await Task.WhenAny(pause.Reached, a, Task.Delay(Patience));
            reached.Should().BeSameAs(pause.Reached, "the first writer is allowed on its own and is parked before its commit");
            (await ProbeGateAsync(scratch)).Should().Be(-1, "the first writer holds the administrator-set gate");

            var b = Task.Run(() => RunDirectoryOpAsync(d, second, null, loaded));
            if (!rcsi && first == DirectoryOp.Switch && second == DirectoryOp.DisableSecond)
            {
                // Under locking READ COMMITTED an edit's advisory existence read, made before its unit (the 404 check), waits on the row the
                // parked switch changed: an incidental row lock, reached before the gate. The decision is still made inside the unit, under the
                // gate, once the switch commits; the row wait only delays it.
                var deadline = DateTime.UtcNow.Add(Patience);
                while (await RowLockWaitersAsync(scratch) == 0)
                {
                    b.IsCompleted.Should().BeFalse("the edit must be held until the switch commits");
                    DateTime.UtcNow.Should().BeBefore(deadline);
                    await Task.Delay(25);
                }

                (await ApplicationLockWaitersAsync(scratch)).Should().Be(0, "it waits on the switched registration's row, before the gate");
            }
            else
            {
                await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, b);
                (await RowLockWaitersAsync(scratch)).Should().Be(0, "the second writer waits for the application lock, not for a row");
            }

            pause.Release();
            firstOutcome = await a.WaitAsync(Patience);
            secondOutcome = await b.WaitAsync(Patience);
        }
        else
        {
            firstOutcome = await RunDirectoryOpAsync(d, first, null);
            secondOutcome = await RunDirectoryOpAsync(d, second, null);
        }

        firstOutcome.Should().Be("applied", "each change is allowed on its own");
        var (expected, refusalTarget, refusalDetail) = (first, second) switch
        {
            // Raced, the edit of R2 carries the row version its operator loaded before the switch changed R2: it is refused as stale under
            // the gate (409, "modified elsewhere") before anything is decided, so nothing is written and no refusal row.
            (DirectoryOp.Switch, DirectoryOp.DisableSecond) when concurrently => ("409:", null, null),

            // Sign-in moved to the other directory, so R2 (now active) or B's link is B's only way in, and B the last who can sign in.
            (DirectoryOp.Switch, DirectoryOp.DisableSecond) => ("409:last_viable_sign_in_method", d.S.R2!, "update/sign_in_lockout"),
            (DirectoryOp.Switch, DirectoryOp.DeleteSecond) => ("409:last_viable_sign_in_method", d.S.R2!, "delete/sign_in_lockout"),
            (DirectoryOp.Switch or DirectoryOp.ChangeTenant, DirectoryOp.RemoveEntraLinkOfB) => ("refused:last_viable_sign_in_method", null, null),

            // R2 or B's link went first: the move would leave nobody, or has nothing to move to.
            (DirectoryOp.DisableSecond, DirectoryOp.Switch) => ("400:", d.S.R2!, "activate/registration_disabled"),
            (DirectoryOp.DeleteSecond, DirectoryOp.Switch) => ("404:", null, null),
            (DirectoryOp.RemoveEntraLinkOfB, DirectoryOp.Switch) => ("409:last_viable_sign_in_method", d.S.R2!, "activate/sign_in_lockout"),
            (DirectoryOp.RemoveEntraLinkOfB, DirectoryOp.ChangeTenant) => ("409:last_viable_sign_in_method", d.S.R1, "update/sign_in_lockout"),
            _ => throw new ArgumentOutOfRangeException(nameof(second)),
        };
        secondOutcome.Should().Be(expected, "the second change is decided on what the first committed");
        if (refusalTarget is not null)
            (await RefusalDetailsAsync(scratch, refusalTarget)).Should().Equal(refusalDetail);
        if (second == DirectoryOp.RemoveEntraLinkOfB)
        {
            (await d.S.World.LinkCountAsync(d.B, 0)).Should().Be(1, "the refused removal was rolled back");
            (await d.S.World.AuditRowsAsync("auth_admin.blocked.last_admin_link_removed", "E" + d.B)).Should().Be(1);
        }

        var state = await ViabilityAsync(scratch);
        foreach (var view in new[] { state.Now, state.Restart })
            view.ViableOrdinaryAdministrators.Should().ContainSingle("exactly one of the two changes committed, and someone can still sign in");
    }

    // ------------------------------------------------------------------------------------------------ the sequential twin of the
    // administrator-first order

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_the_administrator_change_then_the_registration_change_one_after_the_other_give_the_same_result(RegOp reg, Wp2Op wp2, bool rcsi)
    {
        var p = await PairAsync(rcsi);

        await using (var db = p.S.Scratch.NewDb())
            await RunWp2Async(p, wp2, db);
        await using var dbR = p.S.Scratch.NewDb();
        var response = await RunAsync(p.S, reg, dbR);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await Wp2AppliedAsync(p, wp2)).Should().BeTrue();
        (await RegistrationAppliedAsync(p.S, reg)).Should().BeFalse();
        (await RefusalDetailsAsync(p.S.Scratch, TargetOf(p.S, reg))).Should().Equal(ChangeOf(reg) + "/sign_in_lockout");
        (await ViabilityAsync(p.S.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.S.A]);
    }
}
