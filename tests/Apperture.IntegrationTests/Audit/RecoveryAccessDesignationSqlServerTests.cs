using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
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
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// WP4B-A, F-1 and the stale generic writers, against real SQL Server on both isolation modes. The Recovery Access designation has one
/// writer (the conditional setter); a copy of a user read earlier must neither set nor clear it, and must not be applied to an account
/// that was designated or revoked since the copy was read. Every case forces the interleaving: the generic writer is parked at the
/// statement that reads the designation under its row lock (before it, or just after it), the designation changes in a session of its
/// own, and the writer resumes. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RecoveryAccessDesignationSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>The statement every guarded writer reads the stored designation with, under the user's row lock.</summary>
    private const string LockedUserRead = "FROM [Users] WITH (";

    private readonly AuditIsolationCatalogs _catalogs;

    public RecoveryAccessDesignationSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with its locks.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>Waits until the parked operation reaches its pause; an operation that fails or finishes first is reported as that, not as a timeout.</summary>
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

    // ------------------------------------------------------------------------------------------------ the world

    private async Task<(RoleAuthorityWorld World, string Id)> OrdinaryAsync(bool rcsi, string stem = "ordinary")
    {
        var scratch = _catalogs.For(rcsi);

        // One account is designated at a time (a filtered unique index), and the catalog is shared by the cases of this class.
        await scratch.ExecuteAsync("UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;");
        var world = new RoleAuthorityWorld(scratch);
        return (world, await world.NewUserAsync(stem));
    }

    private static SqlUserRepository Users(AppertureDbContext db) => new(db, NullLogger<SqlUserRepository>.Instance);

    /// <summary>The designation changing in a session of its own: the conditional setter, committed.</summary>
    private static async Task<bool> SetDesignationAsync(AuditSqlScratch scratch, string userId, bool expected, bool designated)
    {
        await using var db = scratch.NewDb();
        return await Users(db).TrySetDesignationAsync(new UserId(userId), expected, designated);
    }

    private static Task<bool> StoredDesignationAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{userId}';");

    private static Task<int> AuditRowsOfAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId = N'{userId}';");

    private static PauseAt BeforeLockedRead() => PauseAt.BeforeStatement(LockedUserRead);

    private static PauseAt AfterLockedRead() => new(c => c.CommandText.Contains(LockedUserRead, StringComparison.Ordinal), after: true);

    private static IPrincipalContext PrincipalOf(string userId) => new Principal(userId);

    private static SqlAuditEventWriter Events(AuditSqlScratch scratch, IPrincipalContext? principal) =>
        new(scratch.Options(), principal, HarnessCatalog, null, null, null);

    private static AccountSecurityLinkBuilder Links() => new(new ConfigurationBuilder().Build());

    private static AccountProfileService Profile(AuditSqlScratch scratch, AppertureDbContext db, string actor)
    {
        var principal = PrincipalOf(actor);
        return new AccountProfileService(
            Users(db),
            new SqlUserPreferencesRepository(db),
            new SqlUserProfileImageStore(db),
            new SkiaProfileImageNormalizer(),
            new OperatorNotificationSubscriptionService(
                new SqlNotificationDefinitionStore(db), new SqlNotificationSubscriptionStore(db), new SqlNotificationUserPreferenceStore(db)),
            new UnusedToastPreferences(),
            NullLogger<AccountProfileService>.Instance,
            NewUnit(db, principal),
            Events(scratch, principal));
    }

    private static AccountContactService Contact(AuditSqlScratch scratch, AppertureDbContext db, string actor, RecordingMail mail)
    {
        var principal = PrincipalOf(actor);
        return new AccountContactService(
            Users(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System),
            mail,
            Links(),
            auditedUnitOfWork: NewUnit(db, principal),
            auditEvents: Events(scratch, principal));
    }

    private static AccountInvitationService Invitations(AuditSqlScratch scratch, AppertureDbContext db, string actor, RecordingMail mail)
    {
        var principal = PrincipalOf(actor);
        var tokenRows = new SqlAccountActionTokenRepository(db);
        return new AccountInvitationService(
            Users(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(tokenRows, TimeProvider.System),
            tokenRows,
            mail,
            Links(),
            TimeProvider.System,
            NewUnit(db, principal),
            Events(scratch, principal));
    }

    private static ForgottenPasswordService Forgotten(AuditSqlScratch scratch, AppertureDbContext db, RecordingMail mail) =>
        new(
            Users(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System),
            mail,
            Links(),
            NewUnit(db, null),
            Events(scratch, null));

    private static EmergencyAccessService Emergency(AppertureDbContext db) =>
        new(
            Users(db),
            new SqlRoleRepository(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new NativeMfaService(
                new SqlNativeTotpCredentialRepository(db),
                new SqlNativeMfaRecoveryCodeRepository(db),
                new LocalPasswordHasher(),
                new ManagementHost.IntegrationTestSecretProtector(),
                TimeProvider.System,
                NullLogger<NativeMfaService>.Instance,
                NewUnit(db, null)),
            TimeProvider.System,
            NullLogger<EmergencyAccessService>.Instance,
            NewUnit(db, null));

    // ================================================================================================ the setter

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_conditional_setter_changes_the_row_only_while_it_holds_the_expected_value(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);

        (await SetDesignationAsync(world.Scratch, id, expected: true, designated: false)).Should().BeFalse("the stored value is not the expected one");
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeFalse();

        (await SetDesignationAsync(world.Scratch, id, expected: false, designated: true)).Should().BeTrue();
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();

        (await SetDesignationAsync(world.Scratch, id, expected: false, designated: true)).Should().BeFalse("a second designation expecting the old value finds the new one and changes nothing");
        (await SetDesignationAsync(world.Scratch, id, expected: true, designated: false)).Should().BeTrue();
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeFalse();
        (await SetDesignationAsync(world.Scratch, "no-such-user", expected: false, designated: true)).Should().BeFalse("a missing user is not a change");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_filtered_unique_index_still_allows_only_one_designated_account(bool rcsi)
    {
        var (world, first) = await OrdinaryAsync(rcsi, "first");
        var second = await world.NewUserAsync("second");
        (await SetDesignationAsync(world.Scratch, first, expected: false, designated: true)).Should().BeTrue();

        var act = () => SetDesignationAsync(world.Scratch, second, expected: false, designated: true);

        var ex = await act.Should().ThrowAsync<Exception>();
        SqlUniqueKeyViolation.IsOnIndex(ex.Which, "UX_Users_EmergencyAccess").Should().BeTrue("the index is the arbiter of one designated account, and it is unchanged");
        (await StoredDesignationAsync(world.Scratch, second)).Should().BeFalse();
        (await StoredDesignationAsync(world.Scratch, first)).Should().BeTrue();
    }

    // ================================================================================================ the generic update

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_generic_update_never_writes_the_designation_of_the_copy_it_holds(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        await using var db = world.Scratch.NewDb();
        var designatedCopy = (await Users(db).GetByIdAsync(new UserId(id)))!;
        designatedCopy.IsBreakGlassEligible.Should().BeTrue();

        // The copy matches the stored value, so the update proceeds as it always has; it carries no designation of its own.
        await Users(db).UpdateAsync(designatedCopy with { DisplayTimeZoneId = "Europe/London" });

        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();
        (await world.Scratch.ScalarAsync<string>($"SELECT DisplayTimeZoneId FROM Users WHERE Id = N'{id}';")).Should().Be("Europe/London");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_display_name_change_read_before_a_designation_is_refused_with_the_established_refusal_and_the_designation_stays(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var pause = BeforeLockedRead();
        var change = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Profile(world.Scratch, db, id).UpdateDisplayNameAsync(new UserId(id), "Renamed Person");
        });
        await ReachedAsync(pause, change);

        (await SetDesignationAsync(world.Scratch, id, expected: false, designated: true)).Should().BeTrue("the designation commits while the change holds a copy that says 'ordinary'");
        pause.Release();

        var act = async () => await change.WaitAsync(Patience);
        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        ex.Which.Message.Should().Be("Recovery Access is system-managed. Display name and avatar cannot be changed here.");
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue("the stale copy did not clear the designation");
        (await world.Scratch.ScalarAsync<string>($"SELECT DisplayName FROM Users WHERE Id = N'{id}';")).Should().Be(id, "the name is unchanged");
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore, "no row for a change that was not made");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_time_zone_change_read_before_a_revocation_is_refused_as_stale_and_the_designation_is_not_restored(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        var pause = BeforeLockedRead();
        var change = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Profile(world.Scratch, db, id).UpdatePreferencesAsync(new UserId(id), new AccountPreferencesUpdate("System", "System", "Europe/London"));
        });
        await ReachedAsync(pause, change);

        (await SetDesignationAsync(world.Scratch, id, expected: true, designated: false)).Should().BeTrue("the revocation commits while the change holds a copy that says 'designated'");
        pause.Release();

        var act = async () => await change.WaitAsync(Patience);
        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.StaleUpdate);
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeFalse("a copy read before the revocation did not restore the designation");
        (await world.Scratch.ScalarAsync<string?>($"SELECT DisplayTimeZoneId FROM Users WHERE Id = N'{id}';")).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_waits_for_a_generic_update_that_holds_the_row_and_both_then_apply(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var pause = AfterLockedRead();
        var change = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Profile(world.Scratch, db, id).UpdateDisplayNameAsync(new UserId(id), "Renamed Person");
        });
        await ReachedAsync(pause, change);

        var designate = Task.Run(() => SetDesignationAsync(world.Scratch, id, expected: false, designated: true));
        await world.WaitUntilBlockedOnAsync("Users", designate);
        designate.IsCompleted.Should().BeFalse("the update holds the user's row: the designation serializes behind it, so the update is never applied to an account that became designated under it");

        pause.Release();
        await change.WaitAsync(Patience);
        (await designate.WaitAsync(Patience)).Should().BeTrue();
        (await world.Scratch.ScalarAsync<string>($"SELECT DisplayName FROM Users WHERE Id = N'{id}';")).Should().Be("Renamed Person");
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_ordinary_profile_update_of_an_undesignated_account_is_unchanged(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);

        await using (var db = world.Scratch.NewDb())
            await Profile(world.Scratch, db, id).UpdateDisplayNameAsync(new UserId(id), "Renamed Person");

        (await world.Scratch.ScalarAsync<string>($"SELECT DisplayName FROM Users WHERE Id = N'{id}';")).Should().Be("Renamed Person");
        (await world.AuditRowsAsync(AuditActionNames.DisplayNameUpdated, id)).Should().Be(1);
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeFalse();
    }

    // ================================================================================================ contact and invitation

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_contact_request_read_before_a_designation_is_refused_and_leaves_no_address_link_or_row(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var mail = new RecordingMail();
        var pause = BeforeLockedRead();
        var change = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Contact(world.Scratch, db, id, mail).RequestAsync(new UserId(id), $"{id}@example.com", null);
        });
        await ReachedAsync(pause, change);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await change.WaitAsync(Patience);
        var ex = await act.Should().ThrowAsync<AccountSecurityException>();
        ex.Which.Code.Should().Be(AccountSecurityErrorCodes.SystemManaged);
        ex.Which.Message.Should().Be("Recovery Access does not use contact email.");
        (await world.Scratch.ScalarAsync<string?>($"SELECT PendingEmailAddress FROM Users WHERE Id = N'{id}';")).Should().BeNull();
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}';")).Should().Be(0);
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore);
        mail.Sent.Should().BeEmpty();
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_contact_verification_read_before_a_designation_is_refused_and_the_consumed_link_is_rolled_back_with_it(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var mail = new RecordingMail();
        await using (var db = world.Scratch.NewDb())
            await Contact(world.Scratch, db, id, mail).RequestAsync(new UserId(id), $"{id}@example.com", null);
        var link = mail.SingleLink();
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var pause = BeforeLockedRead();
        var verify = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await Contact(world.Scratch, db, id, new RecordingMail()).VerifyAsync(link);
        });
        await ReachedAsync(pause, verify);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await verify.WaitAsync(Patience);
        (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.SystemManaged);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL;"))
            .Should().Be(1, "the link was consumed inside the unit that was refused, so it was rolled back and is usable again");
        (await world.Scratch.ScalarAsync<string?>($"SELECT EmailAddress FROM Users WHERE Id = N'{id}';")).Should().BeNull();
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore, "no EmailVerified row for a change that was not made");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_invitation_read_before_a_designation_is_refused_and_sends_nothing(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var mail = new RecordingMail();
        var pause = BeforeLockedRead();
        var invite = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Invitations(world.Scratch, db, id, mail).SendSetupInvitationAsync(new UserId(id), $"{id}@invitee.example.com");
        });
        await ReachedAsync(pause, invite);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await invite.WaitAsync(Patience);
        var ex = await act.Should().ThrowAsync<AccountSecurityException>();
        ex.Which.Code.Should().Be(AccountSecurityErrorCodes.SystemManaged);
        ex.Which.Message.Should().Be("Recovery Access cannot receive an onboarding invitation.");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}';")).Should().Be(0);
        (await world.Scratch.ScalarAsync<string?>($"SELECT PendingEmailAddress FROM Users WHERE Id = N'{id}';")).Should().BeNull();
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore);
        mail.Sent.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_invitation_completion_read_before_a_designation_sets_no_password_and_keeps_the_link(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var mail = new RecordingMail();
        await using (var db = world.Scratch.NewDb())
            await Invitations(world.Scratch, db, id, mail).SendSetupInvitationAsync(new UserId(id), $"{id}@invitee.example.com");
        var link = mail.SingleLink();
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var pause = BeforeLockedRead();
        var complete = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await Invitations(world.Scratch, db, id, new RecordingMail()).CompletePasswordSetupAsync(link, "chosen-harbor-passphrase");
        });
        await ReachedAsync(pause, complete);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await complete.WaitAsync(Patience);
        (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.SystemManaged);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{id}';")).Should().Be(0, "the password was not set on the designated account");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL;"))
            .Should().Be(1, "the consumption was rolled back with the refused unit");
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_invitation_completion_that_writes_no_address_is_still_refused_for_an_account_designated_before_the_password_write(bool rcsi)
    {
        // The invited address was verified by somebody else after the invitation was sent, so completing it writes the password and
        // no address: the recheck before the password is then the only thing between the invitation and the designated account.
        var (world, id) = await OrdinaryAsync(rcsi);
        var other = await world.NewUserAsync("holder");
        var mail = new RecordingMail();
        var address = $"{id}@invitee.example.com";
        await using (var db = world.Scratch.NewDb())
            await Invitations(world.Scratch, db, id, mail).SendSetupInvitationAsync(new UserId(id), address);
        var link = mail.SingleLink();
        await world.Scratch.ExecuteAsync($"UPDATE Users SET EmailAddress = N'{address}', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{other}';");
        var pause = BeforeLockedRead();
        var complete = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await Invitations(world.Scratch, db, id, new RecordingMail()).CompletePasswordSetupAsync(link, "chosen-harbor-passphrase");
        });
        await ReachedAsync(pause, complete);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await complete.WaitAsync(Patience);
        (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.SystemManaged);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{id}';")).Should().Be(0, "no password was set on the designated account");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL;")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_password_reset_completion_read_before_a_designation_changes_no_password_and_keeps_the_link(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        await world.AddCredentialAsync(id);
        await world.Scratch.ExecuteAsync($"UPDATE Users SET EmailAddress = N'{id}@example.com', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{id}';");
        var mail = new RecordingMail();
        await using (var db = world.Scratch.NewDb())
            await Forgotten(world.Scratch, db, mail).RequestAsync($"{id}@example.com");
        var link = mail.SingleLink();
        var hashBefore = await world.Scratch.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{id}';");
        var rowsBefore = await AuditRowsOfAsync(world.Scratch, id);
        var pause = BeforeLockedRead();
        var complete = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await Forgotten(world.Scratch, db, new RecordingMail()).CompleteAsync(link, "new-harbor-passphrase");
        });
        await ReachedAsync(pause, complete);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await complete.WaitAsync(Patience);
        (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.TokenInvalid);
        (await world.Scratch.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{id}';")).Should().Be(hashBefore);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{id}' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL;")).Should().Be(1);
        (await AuditRowsOfAsync(world.Scratch, id)).Should().Be(rowsBefore, "no PasswordResetCompleted row for a change that was not made");
    }

    // ================================================================================================ rename

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_rename_read_before_a_designation_is_refused_and_changes_neither_the_name_the_link_nor_the_ledger(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var usernameBefore = await world.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{id}';");
        var pause = BeforeLockedRead();
        var rename = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await new UsernameService(Users(db)).RenameWithResultAsync(new UserId(id), NewUsername("renamed"));
        });
        await ReachedAsync(pause, rename);
        await SetDesignationAsync(world.Scratch, id, expected: false, designated: true);
        pause.Release();

        var act = async () => await rename.WaitAsync(Patience);
        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        ex.Which.Message.Should().Be("Recovery Access is system-managed. Its username cannot be changed through ordinary user administration.");
        (await world.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{id}';")).Should().Be(usernameBefore);
        (await world.Scratch.ScalarAsync<string>($"SELECT ExternalId FROM IdentityLinks WHERE UserId = N'{id}' AND Source = 1;")).Should().Be(usernameBefore);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UsernameLedger WHERE UserId = N'{id}';")).Should().Be(1, "no ledger row for a rename that was not made");
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_waits_for_a_rename_that_holds_the_row_and_then_both_apply(bool rcsi)
    {
        var (world, id) = await OrdinaryAsync(rcsi);
        var target = NewUsername("renamed");
        var pause = AfterLockedRead();
        var rename = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await new UsernameService(Users(db)).RenameWithResultAsync(new UserId(id), target);
        });
        await ReachedAsync(pause, rename);

        var designate = Task.Run(() => SetDesignationAsync(world.Scratch, id, expected: false, designated: true));
        await world.WaitUntilBlockedOnAsync("Users", designate);

        pause.Release();
        (await rename.WaitAsync(Patience)).Outcome.Should().Be(UsernameChangeOutcome.Assigned);
        (await designate.WaitAsync(Patience)).Should().BeTrue();
        (await world.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{id}';")).Should().Be(target);
        (await StoredDesignationAsync(world.Scratch, id)).Should().BeTrue();
    }

    // ================================================================================================ designate against revoke

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_and_a_revocation_that_overlap_never_leave_two_designated_accounts_and_only_committed_changes_have_a_row(bool rcsi)
    {
        var (world, current) = await OrdinaryAsync(rcsi, "current");
        var admin = BootstrapConstants.PlatformAdminRoleId;
        var next = await world.NewUserAsync("next", admin);
        await world.AssignRawAsync(current, admin);
        await world.AddCredentialAsync(current);
        await world.DesignateRecoveryAccessAsync(current);
        var designatedBefore = await world.AuditRowsAsync(AuditActionNames.RecoveryAccessDesignated, next);
        var revokedBefore = await world.AuditRowsAsync(AuditActionNames.RecoveryAccessRevoked, current);

        // The designation of 'next' runs everything but its commit: it has demoted 'current' and designated 'next'.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var designate = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await Emergency(db).DesignateAsync(new UserId(next), new ActorId(EmergencyAccessService.CliActorId));
        });
        await ReachedAsync(pause, designate);

        var revoke = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb();
            await Emergency(db).RevokeAsync(confirmFallback: true, new ActorId(EmergencyAccessService.CliActorId));
        });
        await world.WaitUntilBlockedOnAsync("Users", revoke);

        pause.Release();
        await designate.WaitAsync(Patience);
        Exception? revokeFailure = null;
        try
        {
            await revoke.WaitAsync(Patience);
        }
        catch (Exception ex)
        {
            revokeFailure = ex;
        }

        var designated = await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE IsBreakGlassEligible = 1;");
        designated.Should().BeLessThanOrEqualTo(1, "the filtered unique index and the conditional setter keep one designated account");
        (await world.AuditRowsAsync(AuditActionNames.RecoveryAccessDesignated, next)).Should().Be(designatedBefore + 1, "the designation committed");
        var revokedRows = await world.AuditRowsAsync(AuditActionNames.RecoveryAccessRevoked, current)
            + await world.AuditRowsAsync(AuditActionNames.RecoveryAccessRevoked, next);

        if (revokeFailure is null)
        {
            // The revocation waited, then saw the designation that committed first and revoked it: serial order, one row each.
            designated.Should().Be(0);
            revokedRows.Should().Be(revokedBefore + 1);
        }
        else
        {
            // The revocation was decided on the account that has since been demoted: its conditional write found a different stored
            // value and the unit failed as an invariant failure. It changed nothing and has no row.
            revokeFailure.Should().BeOfType<AuditInvariantException>();
            designated.Should().Be(1);
            (await StoredDesignationAsync(world.Scratch, next)).Should().BeTrue();
            revokedRows.Should().Be(revokedBefore, "no row for a revocation that did not commit");
        }
    }

    // ================================================================================================ helpers

    private sealed class RecordingMail : IAccountSecurityMailSender
    {
        public List<(string Recipient, string Body)> Sent { get; } = [];

        public string SingleLink()
        {
            var body = Sent.Should().ContainSingle().Which.Body;
            var start = body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
            var end = body.IndexOfAny(['\r', '\n', ' ', '&'], start);
            return Uri.UnescapeDataString(end < 0 ? body[start..] : body[start..end]);
        }

        public ValueTask<AccountSecurityMailResult> SendAsync(string recipient, string subject, string plainTextBody, CancellationToken cancellationToken = default)
        {
            Sent.Add((recipient, plainTextBody));
            return ValueTask.FromResult(new AccountSecurityMailResult(true, null));
        }
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
