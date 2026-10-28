using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The first-administrator invariant under concurrency (IAM-10.2b M-P1): of two requests that both find an empty installation,
/// exactly one creates the administrator. The decision "no user exists yet" is made again where the user is written, and it is made
/// under the global administrator-set gate, so a second request cannot take the decision until the first has committed or rolled
/// back. Real SQL Server, both isolation modes, the overlap forced: the first request is parked inside its unit, holding everything
/// it holds, until the server shows the second request waiting.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class FirstAdministratorConcurrencySqlServerTests
{
    private const string Password = "quiet-maple-lantern-harbor";
    private const string PlatformAdmin = "role-platform-administrator";

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static FirstAdminBootstrapService Service(AppertureDbContext db, TimeSpan? gateBound = null) => new(
        new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
        new SqlRoleRepository(db),
        new SqlLocalCredentialRepository(db),
        new LocalPasswordHasher(),
        NewUnit(db, null),
        Viability(db, gateBound is { } bound ? new SqlPlatformAdministratorSetGate(db, bound) : null));

    private static async Task AssertNothingWasWrittenAsync(AuditSqlScratch scratch, int auditRowsBefore)
    {
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM IdentityLinks;")).Should().Be(0);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM LocalCredentials;")).Should().Be(0);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE RoleId = N'{PlatformAdmin}';")).Should().Be(0);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditRowsBefore);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_concurrent_first_administrator_requests_create_exactly_one_administrator(bool rcsi) =>
        await RaceAsync(rcsi, "firsta", "firstb");

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_concurrent_first_administrator_requests_for_the_same_sign_in_name_create_exactly_one_administrator(bool rcsi) =>
        await RaceAsync(rcsi, "firsta", "firsta");

    private static async Task RaceAsync(bool rcsi, string firstName, string secondName)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminRace_", rcsi);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        // The first request is parked after the decisive read inside its unit (the second user-count read of the request; the first
        // is the early answer made before the unit).
        var seen = 0;
        var pause = new PauseAt(c => c.CommandText.Contains("COUNT(*)", StringComparison.Ordinal) && Interlocked.Increment(ref seen) == 2, after: true);
        try
        {
            var first = Task.Run(async () =>
            {
                await using var db = scratch.NewDb(pause);
                return await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, firstName, Password);
            });
            await pause.Reached.WaitAsync(TimeSpan.FromSeconds(60));

            var second = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                return await Service(db).TryCreateFirstAdminAsync("First B", IdentitySource.Local, secondName, Password);
            });
            var waited = await WaitsOnTheGateAsync(scratch, second);
            var waitedOnARow = waited && await RowLockWaitersAsync(scratch) > 0;

            pause.Release();
            var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(60));
            var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(60));

            new[] { firstResult.Kind, secondResult.Kind }.Should().BeEquivalentTo(
                [FirstAdminBootstrapResultKind.Success, FirstAdminBootstrapResultKind.AlreadyExists],
                "exactly one request creates the first administrator, and the other sees the committed, initialised state");
            firstResult.Kind.Should().Be(FirstAdminBootstrapResultKind.Success, "the request that held the gate first wins");

            // One administrator, with its one link, its one credential, its one role assignment and its one success row; the loser
            // wrote nothing at all.
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(1);
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM IdentityLinks;")).Should().Be(1);
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM LocalCredentials;")).Should().Be(1);
            (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE RoleId = N'{PlatformAdmin}';")).Should().Be(1);
            (await scratch.ScalarAsync<string>("SELECT Id FROM Users;")).Should().Be(firstResult.UserId!.Value);
            (await scratch.ScalarAsync<string>("SELECT Username FROM Users;")).Should().Be(firstName);
            (await scratch.ScalarAsync<string>("SELECT ExternalId FROM IdentityLinks;")).Should().Be(firstName);
            (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{firstResult.UserId!.Value}';")).Should().Be(1, "the credential belongs to the winner");
            (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{firstResult.UserId!.Value}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore + 1, "one success row; the refused request changed nothing and records nothing in its unit");
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'bootstrap.first_admin.created';")).Should().Be(1);

            waited.Should().BeTrue("the second request must have been waiting for the administrator-set gate while the first held it");
            waitedOnARow.Should().BeFalse("the wait was on the application lock, not on a row");
            (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "both units ended, so nothing holds the gate any more");
        }
        finally
        {
            pause.Release();
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_gate_held_past_the_bound_is_contention_not_a_refusal_nothing_is_written_and_the_retry_succeeds(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminBusy_", rcsi);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var holder = await HoldTheGateAsync(scratch))
        {
            await using var db = scratch.NewDb();
            var busy = async () => await Service(db, TimeSpan.FromMilliseconds(500)).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", Password);

            (await busy.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which.Code.Should().Be(PlatformAdministratorSetBusyException.BusyCode);
            await AssertNothingWasWrittenAsync(scratch, auditBefore);
        }

        FirstAdminBootstrapResult retried;
        await using (var db = scratch.NewDb())
            retried = await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", Password);

        retried.Kind.Should().Be(FirstAdminBootstrapResultKind.Success, "contention was transient and left nothing behind");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_request_cancelled_while_it_waits_for_the_gate_writes_nothing_and_releases_everything(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminCancel_", rcsi);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");
        using var cancel = new CancellationTokenSource();

        await using (var holder = await HoldTheGateAsync(scratch))
        {
            var waiting = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                return await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", Password, cancel.Token);
            });
            (await WaitsOnTheGateAsync(scratch, waiting)).Should().BeTrue();

            cancel.Cancel();
            var cancelled = async () => await waiting.WaitAsync(TimeSpan.FromSeconds(60));
            await cancelled.Should().ThrowAsync<OperationCanceledException>("cancellation is the caller's decision, neither contention nor a refusal");
            await AssertNothingWasWrittenAsync(scratch, auditBefore);
            (await ApplicationLockWaitersAsync(scratch)).Should().Be(0, "the cancelled request no longer waits");
        }

        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "the cancelled request did not leave the gate held");
        FirstAdminBootstrapResult next;
        await using (var db = scratch.NewDb())
            next = await Service(db).TryCreateFirstAdminAsync("First B", IdentitySource.Local, "firstb", Password);
        next.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_request_for_an_installation_that_is_already_set_up_never_waits_for_the_gate(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminDone_", rcsi);
        await using (var db = scratch.NewDb())
            (await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", Password)).Kind.Should().Be(FirstAdminBootstrapResultKind.Success);

        // Another change to administrator access holds the gate: an anonymous caller must not queue behind it.
        await using var holder = await HoldTheGateAsync(scratch);
        FirstAdminBootstrapResult answer;
        await using (var db = scratch.NewDb())
            answer = await Service(db).TryCreateFirstAdminAsync("First B", IdentitySource.Local, "firstb", Password).WaitAsync(TimeSpan.FromSeconds(30));

        answer.Kind.Should().Be(FirstAdminBootstrapResultKind.AlreadyExists);
        (await ApplicationLockWaitersAsync(scratch)).Should().Be(0);
        (await ProbeGateAsync(scratch)).Should().Be(-1, "the gate is still held by the other change: the request answered without it");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refusal_decided_under_the_gate_releases_it_and_changes_nothing(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminRefuse_", rcsi);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        FirstAdminBootstrapResult refused;
        await using (var db = scratch.NewDb())
            refused = await Service(db).TryCreateFirstAdminAsync("Recovery", IdentitySource.Local, "breakglass", Password);

        refused.Kind.Should().Be(FirstAdminBootstrapResultKind.DuplicateIdentityLink, "the reserved recovery name is refused as before");
        await AssertNothingWasWrittenAsync(scratch, auditBefore);
        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "the unit ended, so the gate was released with its transaction");

        FirstAdminBootstrapResult next;
        await using (var db = scratch.NewDb())
            next = await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", Password);
        next.Kind.Should().Be(FirstAdminBootstrapResultKind.Success, "the sequential contract is unchanged");
    }
}
