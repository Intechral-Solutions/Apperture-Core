using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The first-administrator invariant under concurrency: of two requests that both find an empty installation, exactly one creates the
/// administrator. <b>This case is skipped on purpose and is not validated by WP4B-A.</b> The early read added in WP4B-A answers a
/// request for a set-up installation without entering the audited unit, but it is only a reason to answer early; what stops a second
/// administrator is the decision made again where the user is written, and that decision is not yet serialized: the unit's operation
/// lock is per operation, so two requests with different names both pass the count inside their own units and both commit (observed
/// on locking READ COMMITTED and on READ_COMMITTED_SNAPSHOT: two administrators). The writer takes the global administrator-set gate
/// and rechecks the count under it in the package that integrates the bootstrap writers; that package removes the skip, and this case
/// is the test it must pass on both isolation modes.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class FirstAdministratorConcurrencySqlServerTests
{
    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    [Theory(Skip = "Depends on the bootstrap writer taking the administrator-set gate (bootstrap writer integration); not validated by WP4B-A.")]
    [MemberData(nameof(BothModes))]
    public async Task Two_concurrent_first_administrator_requests_create_exactly_one_administrator(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAdminRace_", rcsi);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0);

        // The first request is parked after its read inside the unit (the second user-count read of the request).
        var seen = 0;
        var pause = new PauseAt(c => c.CommandText.Contains("COUNT(*)", StringComparison.Ordinal) && Interlocked.Increment(ref seen) == 2, after: true);

        FirstAdminBootstrapService Service(AppertureDbContext db) => new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            NewUnit(db, null));

        var first = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await Service(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", "quiet-maple-lantern-harbor");
        });
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(60));

        FirstAdminBootstrapResult second;
        await using (var db = scratch.NewDb())
            second = await Service(db).TryCreateFirstAdminAsync("First B", IdentitySource.Local, "firstb", "quiet-maple-lantern-harbor");
        pause.Release();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(60));

        new[] { firstResult.Kind, second.Kind }.Should().BeEquivalentTo(
            [FirstAdminBootstrapResultKind.Success, FirstAdminBootstrapResultKind.AlreadyExists],
            "exactly one request creates the first administrator");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM UserRoles WHERE RoleId = N'role-platform-administrator';")).Should().Be(1);
    }
}
