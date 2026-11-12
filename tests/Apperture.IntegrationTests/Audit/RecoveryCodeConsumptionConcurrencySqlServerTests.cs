using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A recovery code admits one sign-in, against real SQL Server on both isolation modes. Two sign-ins present the same code at the same
/// time: each reads the unused codes without a lock and finds the code; each is parked just after that read, so both have decided before
/// either writes; then both are released. Exactly one sign-in may succeed, the code is consumed once, and one consumption is recorded.
/// The assessment of the independent review's open item on recovery-code acceptance under concurrency. Fails, never skips, without SQL
/// Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RecoveryCodeConsumptionConcurrencySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>The sign-in's read of the user's recovery codes (the statement every consumption decides on).</summary>
    private const string CodeListRead = "FROM [UserMfaRecoveryCodes] AS [u]";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public RecoveryCodeConsumptionConcurrencySqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The sign-in finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The sign-in did not reach the pause.");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_sign_ins_with_one_recovery_code_that_both_read_it_unused_before_either_wrote_succeed_at_most_once(bool rcsi)
    {
        var world = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi);
        var code = world.EnrollmentCodes[0];
        var unusedBefore = await UnusedAsync(world);

        var firstPause = new PauseAt(c => c.CommandText.Contains(CodeListRead, StringComparison.Ordinal), after: true);
        var secondPause = new PauseAt(c => c.CommandText.Contains(CodeListRead, StringComparison.Ordinal), after: true);
        var first = SignInAsync(world, code, firstPause);
        await ReachedAsync(firstPause, first);
        var second = SignInAsync(world, code, secondPause);
        await ReachedAsync(secondPause, second);

        // Both sign-ins have read the code as unused. Release them together.
        firstPause.Release();
        secondPause.Release();
        var results = new[] { await first.WaitAsync(Patience), await second.WaitAsync(Patience) };

        var consumedRows = await world.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed);
        var unusedAfter = await UnusedAsync(world);
        _output.WriteLine($"rcsi={rcsi}: sign-in results [{string.Join(", ", results)}]; consumption rows {consumedRows}; unused codes {unusedBefore} -> {unusedAfter}; {world.Scratch.ExecutionMarker}");

        results.Count(r => r).Should().Be(1, "one recovery code admits one sign-in");
        consumedRows.Should().Be(1, "one consumption is recorded");
        unusedAfter.Should().Be(unusedBefore - 1, "the code is consumed once and no other code is touched");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_recovery_code_consumed_by_another_sign_in_while_this_one_waited_for_its_row_is_refused(bool rcsi)
    {
        // The other order: the first sign-in has written its consumption (uncommitted) when the second, which read the code as unused,
        // reaches its write and waits for the row. Once the first commits, the second must find the code consumed and fail.
        var world = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi);
        var code = world.EnrollmentCodes[1];
        var unusedBefore = await UnusedAsync(world);

        var secondRead = new PauseAt(c => c.CommandText.Contains(CodeListRead, StringComparison.Ordinal), after: true);
        var second = SignInAsync(world, code, secondRead);
        await ReachedAsync(secondRead, second);

        var firstCommit = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = SignInAsync(world, code, firstCommit);
        await ReachedAsync(firstCommit, first);

        secondRead.Release();
        await world.World.WaitUntilBlockedOnAsync("UserMfaRecoveryCodes", second);
        firstCommit.Release();

        var results = new[] { await first.WaitAsync(Patience), await second.WaitAsync(Patience) };
        _output.WriteLine($"rcsi={rcsi}: first={results[0]} second={results[1]}; {world.Scratch.ExecutionMarker}");

        results.Should().Equal([true, false], "the sign-in that wrote first consumed the code; the other one found it consumed");
        (await world.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed)).Should().Be(1);
        (await UnusedAsync(world)).Should().Be(unusedBefore - 1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_recovery_code_signs_in_once_and_then_never_again(bool rcsi)
    {
        var world = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi);
        var code = world.EnrollmentCodes[2];

        (await SignInAsync(world, code, null)).Should().BeTrue();
        (await SignInAsync(world, code, null)).Should().BeFalse("a consumed code changes nothing and signs in no one");
        (await SignInAsync(world, world.EnrollmentCodes[3], null)).Should().BeTrue("another unused code still works");
        (await world.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed)).Should().Be(2);
    }

    private static Task<bool> SignInAsync(MfaCandidateWorld world, string code, PauseAt? pause) => Task.Run(async () =>
    {
        await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
        return await world.Mfa(db).ConsumeRecoveryCodeAsync(world.UserId, code);
    });

    private static Task<int> UnusedAsync(MfaCandidateWorld world) =>
        world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{world.UserId.Value}' AND ConsumedAtUtc IS NULL;");
}
