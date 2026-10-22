using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// One preferences request (appearance, clock format and display time zone) is all or nothing, against real SQL Server on both
/// isolation modes. The appearance and clock format live in the preferences row and the time zone on the user row; the request used
/// to commit the first and then attempt the second, so a refusal of the second (the account's designation changed since it was
/// read) left the first committed. The time zone is now written first and the two share one transaction. The interleavings are
/// forced at statement level: the request is parked at the user row's locked read, the designation changes in a session of its own,
/// and the request resumes. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PreferencesAtomicitySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private const string LockedUserRead = "FROM [Users] WITH (";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public PreferencesAtomicitySqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static readonly AccountPreferencesUpdate Wanted = new("Dark", "Hour24", "Europe/London");

    // ------------------------------------------------------------------------------------------------ the cases

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_ordinary_update_writes_the_appearance_the_clock_format_and_the_time_zone_and_no_audit_row(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);
        var rowsBefore = await AuditRowsAsync(scratch, id);

        await using var db = scratch.NewDb();
        var saved = await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), Wanted);

        saved.Should().Be(new AccountPreferencesSnapshot(AppearancePreference.Dark, TimeFormatPreference.Hour24, "Europe/London"));
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Dark", "Hour24"));
        (await StoredTimeZoneAsync(scratch, id)).Should().Be("Europe/London");
        (await AuditRowsAsync(scratch, id)).Should().Be(rowsBefore, "presentation preferences are deliberately not audited");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_update_that_changes_no_time_zone_still_writes_the_preferences(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);

        await using var db = scratch.NewDb();
        await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), new AccountPreferencesUpdate("Dark", "Hour24", null));

        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Dark", "Hour24"));
        (await StoredTimeZoneAsync(scratch, id)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_request_read_before_a_designation_is_refused_as_stale_and_none_of_it_is_written(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);
        var rowsBefore = await AuditRowsAsync(scratch, id);
        var pause = PauseAt.BeforeStatement(LockedUserRead);
        var request = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), Wanted);
        });
        await ReachedAsync(pause, request);

        (await SetDesignationAsync(scratch, id, expected: false, designated: true)).Should().BeTrue();
        pause.Release();

        var act = async () => await request.WaitAsync(Patience);
        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.StaleUpdate);
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Light", "Hour12"), "the appearance and clock format of the refused request were not written");
        (await StoredTimeZoneAsync(scratch, id)).Should().BeNull();
        (await IsDesignatedAsync(scratch, id)).Should().BeTrue("the designation is not undone");
        (await AuditRowsAsync(scratch, id)).Should().Be(rowsBefore);

        // The same request, repeated, now reaches an account that is designated: a time zone is allowed for it, so it succeeds.
        await using var again = scratch.NewDb();
        await Profile(scratch, again, id).UpdatePreferencesAsync(new UserId(id), Wanted);
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Dark", "Hour24"));
        (await StoredTimeZoneAsync(scratch, id)).Should().Be("Europe/London");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_request_read_before_a_revocation_is_refused_as_stale_and_none_of_it_is_written(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);
        await SetDesignationAsync(scratch, id, expected: false, designated: true);
        var pause = PauseAt.BeforeStatement(LockedUserRead);
        var request = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), Wanted);
        });
        await ReachedAsync(pause, request);

        (await SetDesignationAsync(scratch, id, expected: true, designated: false)).Should().BeTrue();
        pause.Release();

        var act = async () => await request.WaitAsync(Patience);
        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.StaleUpdate);
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Light", "Hour12"));
        (await StoredTimeZoneAsync(scratch, id)).Should().BeNull();
        (await IsDesignatedAsync(scratch, id)).Should().BeFalse("a copy read before the revocation did not restore the designation");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_failure_to_write_the_preferences_undoes_the_time_zone(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);
        var failure = new FailingCommand(sql =>
            sql.Contains("UPDATE [UserPreferences]", StringComparison.Ordinal) || sql.Contains("INSERT INTO [UserPreferences]", StringComparison.Ordinal));

        await using var db = scratch.NewDb(failure);
        var act = async () => await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), Wanted);
        await act.Should().ThrowAsync<Exception>("the database refused the write, wrapped by the framework");

        failure.Failures.Should().BeGreaterThan(0);
        (await StoredTimeZoneAsync(scratch, id)).Should().BeNull("the time zone was written first and is undone with the preferences");
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Light", "Hour12"));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_lost_commit_acknowledgement_leaves_both_writes_committed_together(bool rcsi)
    {
        var (scratch, id) = await SavedPreferencesAsync(rcsi);
        var lost = new LostCommitAcknowledgement();

        await using (var db = scratch.NewDb(lost))
        {
            var act = async () => await Profile(scratch, db, id).UpdatePreferencesAsync(new UserId(id), Wanted);
            await act.Should().ThrowAsync<Exception>("the caller is told the commit's outcome was not acknowledged");
        }

        lost.Commits.Should().BeGreaterThan(0);
        (await StoredPreferencesAsync(scratch, id)).Should().Be(("Dark", "Hour24"), "the commit reached the server: both writes are there");
        (await StoredTimeZoneAsync(scratch, id)).Should().Be("Europe/London", "never one without the other");
    }

    // ---------------------------------------------------------------------------------------------------- helpers

    private async Task<(AuditSqlScratch Scratch, string Id)> SavedPreferencesAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync("UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;");
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("prefs"));
        await using var db = scratch.NewDb();
        await new SqlUserPreferencesRepository(db).UpsertAsync(new UserId(id), new UserPreferences(AppearancePreference.Light, TimeFormatPreference.Hour12));
        return (scratch, id);
    }

    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The operation finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The operation did not reach the pause.");
    }

    private static async Task<bool> SetDesignationAsync(AuditSqlScratch scratch, string userId, bool expected, bool designated)
    {
        await using var db = scratch.NewDb();
        return await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).TrySetDesignationAsync(new UserId(userId), expected, designated);
    }

    private static Task<bool> IsDesignatedAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{userId}';");

    private static Task<string?> StoredTimeZoneAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<string?>($"SELECT DisplayTimeZoneId FROM Users WHERE Id = N'{userId}';");

    private static async Task<(string Appearance, string TimeFormat)> StoredPreferencesAsync(AuditSqlScratch scratch, string userId)
    {
        var rows = await scratch.RowsAsync($"SELECT Appearance, TimeFormat FROM UserPreferences WHERE UserId = N'{userId}';");
        var row = rows.Should().ContainSingle().Which;
        return ((string)row["Appearance"]!, (string)row["TimeFormat"]!);
    }

    private static Task<int> AuditRowsAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId = N'{userId}';");

    private static AccountProfileService Profile(AuditSqlScratch scratch, AppertureDbContext db, string actor)
    {
        var principal = new Principal(actor);
        return new AccountProfileService(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlUserPreferencesRepository(db),
            new SqlUserProfileImageStore(db),
            new SkiaProfileImageNormalizer(),
            new OperatorNotificationSubscriptionService(
                new SqlNotificationDefinitionStore(db), new SqlNotificationSubscriptionStore(db), new SqlNotificationUserPreferenceStore(db)),
            new UnusedToastPreferences(),
            NullLogger<AccountProfileService>.Instance,
            NewUnit(db, principal),
            new SqlAuditEventWriter(scratch.Options(), principal, HarnessCatalog, null, null, null));
    }

    private sealed class UnusedToastPreferences : Apperture.Core.Application.Notifications.Subscriptions.IOperatorNotificationToastPreferenceService
    {
        public ValueTask<IReadOnlyList<Apperture.Core.Application.Notifications.Subscriptions.OperatorToastPreference>> ListToastPreferencesAsync(string userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Apperture.Core.Application.Notifications.Subscriptions.OperatorToastPreference>>([]);

        public ValueTask<Apperture.Core.Application.Notifications.Subscriptions.OperatorToastPreference> SetToastEnabledAsync(string userId, string definitionId, bool toastEnabled, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<Apperture.Core.Application.Notifications.Subscriptions.OperatorPreferenceUpdate<Apperture.Core.Application.Notifications.Subscriptions.OperatorToastPreference>> UpdateToastEnabledAsync(string userId, string definitionId, bool toastEnabled, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
