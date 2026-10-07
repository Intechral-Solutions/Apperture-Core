using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.2 account services and runtime enrollment against SQL Server (audit hardening design gate §13, §22; record D8, D10):
/// a one-time link, the state it belongs to and its row commit together or not at all; the link is mailed once after commit,
/// never by a rerun, with its own W3 row; a send that fails or throws never reports the committed change as undone; token
/// flows record the link's user, and the forgotten-password request is anonymous. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AccountServicesAuditSqlServerTests : IAsyncLifetime
{
    private const int SetupPurpose = 2;

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AccountServicesAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud12Accounts_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Setup invitations ----

    [Fact]
    public async Task An_invitation_commits_the_address_the_link_and_its_row_together_then_mails_once_with_its_own_row()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();

        AccountInvitationResult result;
        await using (var db = _db.NewDb())
            result = await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");

        result.DeliverySucceeded.Should().BeTrue();
        var plaintext = mail.SingleLink();
        var token = await SingleTokenAsync(invitee);
        token.Hash.Should().Be(Hash(plaintext), "the link mailed is the link committed");
        (await _db.ScalarAsync<string>($"SELECT PendingEmailAddress FROM Users WHERE Id = N'{invitee}';")).Should().Be("invitee@example.com");

        var rows = await RowsAsync("user", invitee);
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.InvitationSent, AuditActionNames.SecurityMailDeliveryAttempted);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == admin, "the administrator invites; the invited user is the target (§15.3)");
        rows[0]["Basis"].Should().Be("change");
        Field(rows[0], "requestId").Should().Be(token.Id);
        rows[1]["Basis"].Should().Be("external_attempt");
        rows[1]["OutcomeCode"].Should().Be("succeeded");
        Field(rows[1], "requestId").Should().Be(token.Id);
        Field(rows[1], "purpose").Should().Be("setup_invitation");
        rows.Should().OnlyContain(r => !((string)r["Details"]!).Contains(plaintext) && !((string)r["Details"]!).Contains("invitee@example.com"));
    }

    [Fact]
    public async Task An_invitation_rerun_after_a_rolled_back_commit_issues_one_link_and_mails_it_once()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(rolledBack, attempts))
            await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");

        attempts.Started.Should().Be(2, "the first commit was confirmed rolled back, so the attempt reran");
        mail.Sent.Should().ContainSingle("the send follows the one commit, never an attempt");
        (await SingleTokenAsync(invitee)).Hash.Should().Be(Hash(mail.SingleLink()));
        (await RowsAsync("user", invitee)).Select(r => r["Action"])
            .Should().Equal(AuditActionNames.InvitationSent, AuditActionNames.SecurityMailDeliveryAttempted);
    }

    [Fact]
    public async Task An_invitation_whose_acknowledgement_is_lost_mails_the_committed_link_once()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        AccountInvitationResult result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        result.DeliverySucceeded.Should().BeTrue();
        (await SingleTokenAsync(invitee)).Hash.Should().Be(Hash(mail.SingleLink()), "the link held after verification is the committed one");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_no_link_address_or_mail_is_left_behind()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com").AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{invitee}';")).Should().Be(0);
        (await _db.ScalarAsync<string?>($"SELECT PendingEmailAddress FROM Users WHERE Id = N'{invitee}';")).Should().BeNull();
        mail.Sent.Should().BeEmpty("nothing is sent for a change that did not commit");
        (await RowsAsync("user", invitee)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_send_reports_the_failure_and_leaves_the_committed_invitation_in_place()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail { Succeed = false };

        AccountInvitationResult result;
        await using (var db = _db.NewDb())
            result = await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");

        result.DeliverySucceeded.Should().BeFalse();
        (await SingleTokenAsync(invitee)).Active.Should().BeTrue("a failed send never undoes or contradicts the committed invitation");
        var delivery = (await RowsAsync("user", invitee)).Should().Contain(r => (string)r["Action"]! == AuditActionNames.SecurityMailDeliveryAttempted).Which;
        delivery["OutcomeCode"].Should().Be("failed");
        Field(delivery, "failure").Should().Be("send_failed");
        ((string)delivery["Details"]!).Should().NotContain("smtp", "the transport's own text is never recorded");
    }

    [Fact]
    public async Task A_send_that_throws_is_recorded_as_unknown_and_never_fails_the_committed_request()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail { Throw = true };

        AccountInvitationResult result;
        await using (var db = _db.NewDb())
            result = await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");

        result.DeliverySucceeded.Should().BeFalse();
        result.DeliveryFailure.Should().Be("account.security_mail.delivery_unknown");
        (await SingleTokenAsync(invitee)).Active.Should().BeTrue();
        var delivery = (await RowsAsync("user", invitee)).Single(r => (string)r["Action"]! == AuditActionNames.SecurityMailDeliveryAttempted);
        delivery["OutcomeCode"].Should().Be("unknown", "the message may or may not have left");
    }

    [Fact]
    public async Task Completing_setup_is_recorded_as_the_invited_user_with_the_invitation_it_used()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();
        await using (var db = _db.NewDb())
            await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");
        var token = await SingleTokenAsync(invitee);

        await using (var db = _db.NewDb())
            await Invitations(db, principal: null, mail).CompletePasswordSetupAsync(mail.SingleLink(), "chosen-harbor-passphrase");

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{invitee}';")).Should().Be(1);
        var completion = (await RowsAsync("user", invitee)).Skip(2).ToList();
        completion.Select(r => r["Action"]).Should().Equal(
            AuditActionNames.PasswordSetupCompleted, AuditActionNames.InvitationAccepted, AuditActionNames.EmailVerified);
        completion.Should().OnlyContain(r => (string)r["Actor"]! == invitee && (string)r["ActorKind"]! == "user");
        Field(completion[1], "requestId").Should().Be(token.Id);
        Field(completion[2], "via").Should().Be("setup_invitation");
        completion.Should().OnlyContain(r => !((string)r["Details"]!).Contains("chosen-harbor-passphrase"));
    }

    // ---- User creation with an invitation ----

    [Fact]
    public async Task A_user_created_with_an_invitation_commits_with_it_and_a_refused_invitation_creates_no_user()
    {
        var (admin, _) = await AddUserAsync("admin");
        var mail = new RecordingMail();
        var username = NewUsername("created");

        UserId created;
        await using (var db = _db.NewDb())
            created = await CreatingService(db, new Principal(admin), mail).CreateUserAsync(
                "Created Person", IdentitySource.Local, username, contactEmail: "created@example.com", sendSetupInvitation: true);

        (await RowsAsync("user", created.Value)).Select(r => r["Action"]).Should().Equal(
            AuditActionNames.UserCreated, AuditActionNames.InvitationSent, AuditActionNames.SecurityMailDeliveryAttempted);
        (await SingleTokenAsync(created.Value)).Hash.Should().Be(Hash(mail.SingleLink()));

        // An address another account has verified refuses the invitation, and with it the whole creation.
        var (owner, _) = await AddUserAsync("owner");
        await _db.ExecuteAsync($"UPDATE Users SET EmailAddress = N'taken@example.com', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{owner}';");
        var refusedName = NewUsername("refused");
        await using (var db = _db.NewDb())
        {
            var act = () => CreatingService(db, new Principal(admin), mail).CreateUserAsync(
                "Refused Person", IdentitySource.Local, refusedName, contactEmail: "taken@example.com", sendSetupInvitation: true).AsTask();
            (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.EmailInUse);
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM Users WHERE Username = N'{refusedName}';")).Should().Be(0, "no user is left without its invitation");
        mail.Sent.Should().ContainSingle();
    }

    // ---- Forgotten password ----

    [Fact]
    public async Task A_reset_request_is_anonymous_and_one_that_matches_no_account_records_neither_identifier_nor_account()
    {
        var (user, username) = await AddUserAsync("forgetful");
        await _db.ExecuteAsync($"UPDATE Users SET EmailAddress = N'forgetful@example.com', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{user}';");
        await using (var db = _db.NewDb())
            await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(user), "hash", "salt", "pbkdf2");
        var mail = new RecordingMail();

        await using (var db = _db.NewDb())
            await Forgotten(db, mail).RequestAsync("nobody-here@example.com");
        await using (var db = _db.NewDb())
            await Forgotten(db, mail).RequestAsync(username);

        var suppressed = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.PasswordResetRequestSuppressed}';"))
            .Should().ContainSingle().Which;
        suppressed["Actor"].Should().Be("anonymous");
        suppressed["TargetType"].Should().BeNull();
        suppressed["Basis"].Should().Be("attempt");
        ((string)suppressed["Details"]!).Should().NotContain("nobody-here");

        var rows = await RowsAsync("user", user);
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.PasswordResetRequested, AuditActionNames.SecurityMailDeliveryAttempted);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == "anonymous" && (string)r["ActorKind"]! == "anonymous",
            "the requester is not authenticated (corrects §15.3)");
        mail.Sent.Should().ContainSingle().Which.Recipient.Should().Be("forgetful@example.com");
    }

    // ---- Profile ----

    [Fact]
    public async Task A_display_name_change_commits_with_its_row_and_setting_the_same_name_records_nothing()
    {
        var (user, _) = await AddUserAsync("profile");
        var principal = new Principal(user);

        await using (var db = _db.NewDb())
            await Profile(db, principal).UpdateDisplayNameAsync(new UserId(user), "Renamed Person");
        await using (var db = _db.NewDb())
            await Profile(db, principal).UpdateDisplayNameAsync(new UserId(user), "Renamed Person");

        var row = (await RowsAsync("user", user)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.DisplayNameUpdated);
        row["Actor"].Should().Be(user);
        Field(row, "newDisplayName").Should().Be("Renamed Person");
    }

    // ---- Runtime enrollment ----

    [Fact]
    public async Task An_enrollment_invitation_is_the_administrators_and_its_use_is_anonymous()
    {
        var (admin, _) = await AddUserAsync("admin");
        var environment = new FixedEnvironment("local");

        Apperture.Core.Security.Trust.EnrollmentInvitation invitation;
        await using (var db = _db.NewDb())
            invitation = await new SqlRuntimeEnrollmentService(db, environment, NewUnit(db, new Principal(admin))).CreateInvitationAsync("rt-audit");
        await using (var db = _db.NewDb())
            (await new SqlRuntimeEnrollmentService(db, environment, NewUnit(db, null)).EnrollAsync(
                invitation.Token,
                new Apperture.Core.Security.Trust.RuntimeIdentityRegistration("Runtime", "PublicKey", "pk", null))).Success.Should().BeTrue();

        var rows = await RowsAsync("runtime_instance", Apperture.Core.Domain.Governance.ScopeKeyConstants.RuntimeInstance("local", "rt-audit"));
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.RuntimeEnrollmentInvitationCreated, AuditActionNames.RuntimeEnrollmentCompleted);
        rows[0]["Actor"].Should().Be(admin);
        rows[1]["Actor"].Should().Be("anonymous");
        rows.Should().OnlyContain(r => !((string)r["Details"]!).Contains(invitation.Token), "the enrollment token is never recorded");
    }

    // ---- M6 per service (gate §22 AUD-1.2): contact email, password reset, profile, setup completion and enrollment ----

    [Fact]
    public async Task A_contact_request_whose_acknowledgement_is_lost_mails_the_committed_link_once_and_an_audit_failure_leaves_nothing()
    {
        var (user, _) = await AddUserAsync("contact");
        var principal = new Principal(user);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var failedMail = new RecordingMail();
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Contact(db, principal, failedMail).RequestAsync(new UserId(user), "contact@example.com", null).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        failedMail.Sent.Should().BeEmpty();
        (await _db.ScalarAsync<string?>($"SELECT PendingEmailAddress FROM Users WHERE Id = N'{user}';")).Should().BeNull();
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{user}';")).Should().Be(0);

        var mail = new RecordingMail();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        AccountContactMutationResult result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await Contact(db, principal, mail).RequestAsync(new UserId(user), "contact@example.com", null);

        attempts.Started.Should().Be(1);
        result.DeliverySucceeded.Should().BeTrue();
        var hash = await _db.ScalarAsync<string>($"SELECT TokenHash FROM AccountActionTokens WHERE UserId = N'{user}';");
        hash.Should().Be(Hash(mail.SingleLink()), "the link held after verification is the committed one, sent once");
    }

    [Fact]
    public async Task A_reset_request_whose_acknowledgement_is_lost_mails_once_and_an_audit_failure_issues_no_link()
    {
        var (user, username) = await AddUserAsync("resetter");
        await _db.ExecuteAsync($"UPDATE Users SET EmailAddress = N'resetter@example.com', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{user}';");
        await using (var db = _db.NewDb())
            await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(user), "hash", "salt", "pbkdf2");

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var failedMail = new RecordingMail();
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Forgotten(db, failedMail).RequestAsync(username).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        failedMail.Sent.Should().BeEmpty();
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AccountActionTokens WHERE UserId = N'{user}';")).Should().Be(0);

        var mail = new RecordingMail();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
            await Forgotten(db, mail).RequestAsync(username);

        attempts.Started.Should().Be(1);
        (await _db.ScalarAsync<string>($"SELECT TokenHash FROM AccountActionTokens WHERE UserId = N'{user}';")).Should().Be(Hash(mail.SingleLink()));
    }

    [Fact]
    public async Task A_reset_link_used_by_another_request_after_this_one_read_it_is_refused_inside_the_transaction()
    {
        // AUD-1.2 review: the flow reads the link before its unit (to name the actor) and again inside it. The second read must
        // come from the database, not from the context's tracked copy of the first, or a link consumed in between is used twice.
        var (user, username) = await AddUserAsync("reused");
        await _db.ExecuteAsync($"UPDATE Users SET EmailAddress = N'reused@example.com', EmailVerifiedAtUtc = SYSDATETIMEOFFSET() WHERE Id = N'{user}';");
        await using (var db = _db.NewDb())
            await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(user), "original-hash", "salt", "pbkdf2");
        var mail = new RecordingMail();
        await using (var db = _db.NewDb())
            await Forgotten(db, mail).RequestAsync(username);
        var link = mail.SingleLink();

        // Another request completes with the same link after this one's first read, before its transaction starts.
        var usedElsewhere = new BeforeFirstTransaction(() => _db.ExecuteAsync(
            $"UPDATE AccountActionTokens SET ConsumedAtUtc = SYSUTCDATETIME() WHERE UserId = N'{user}' AND ConsumedAtUtc IS NULL;"));
        await using (var db = _db.NewDb(usedElsewhere))
        {
            var act = () => Forgotten(db, new RecordingMail()).CompleteAsync(link, "quiet-maple-lantern-harbor").AsTask();
            (await act.Should().ThrowAsync<AccountSecurityException>()).Which.Code.Should().Be(AccountSecurityErrorCodes.TokenInvalid);
        }

        usedElsewhere.Ran.Should().BeTrue();
        (await _db.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{user}';")).Should().Be("original-hash");
        (await RowsAsync("user", user)).Should().NotContain(r => (string)r["Action"]! == AuditActionNames.PasswordResetCompleted);
    }

    [Fact]
    public async Task Setting_an_email_preference_to_its_current_value_writes_neither_the_preference_nor_a_row()
    {
        // D7, D16 through the real operator service and the SQL unit: a change commits its preference row and its audit row;
        // the same value again writes no preference row and records nothing.
        const string Definition = "platform.schedule.expected_run_missing";
        var (user, _) = await AddUserAsync("prefs");
        var principal = new Principal(user);
        await using (var db = _db.NewDb())
            await Profile(db, principal).SetNotificationEmailPreferenceAsync(new UserId(user), Definition, false);
        var written = await _db.ScalarAsync<DateTimeOffset>(
            $"SELECT UpdatedAtUtc FROM NotificationUserPreferences WHERE UserId = N'{user}' AND NotificationDefinitionId = N'{Definition}' AND Channel = N'email';");
        var rowsAfterChange = (await _db.RowsAsync($"SELECT Id FROM AuditLogs WHERE Actor = N'{user}';")).Count;
        rowsAfterChange.Should().Be(1, "the change commits with its row");

        await using (var db = _db.NewDb())
            (await Profile(db, principal).SetNotificationEmailPreferenceAsync(new UserId(user), Definition, false)).Email.Enabled.Should().BeFalse();

        (await _db.RowsAsync($"SELECT Id FROM AuditLogs WHERE Actor = N'{user}';")).Should().HaveCount(rowsAfterChange, "nothing changed, so nothing is recorded");
        (await _db.ScalarAsync<DateTimeOffset>(
                $"SELECT UpdatedAtUtc FROM NotificationUserPreferences WHERE UserId = N'{user}' AND NotificationDefinitionId = N'{Definition}' AND Channel = N'email';"))
            .Should().Be(written, "an equal preference row is not written again");
    }

    /// <summary>Runs an action from another session once, just before this context starts its first transaction.</summary>
    private sealed class BeforeFirstTransaction(Func<Task> action) : DbTransactionInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                await action();
            }

            return result;
        }
    }

    [Fact]
    public async Task A_display_name_whose_audit_fails_is_unchanged_and_one_whose_acknowledgement_is_lost_is_recorded_once()
    {
        var (user, _) = await AddUserAsync("named");
        var principal = new Principal(user);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Profile(db, principal).UpdateDisplayNameAsync(new UserId(user), "Failed Name").AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        (await _db.ScalarAsync<string>($"SELECT DisplayName FROM Users WHERE Id = N'{user}';")).Should().NotBe("Failed Name");

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        AccountProfileSnapshot snapshot;
        await using (var db = _db.NewDb(lost, attempts))
            snapshot = await Profile(db, principal).UpdateDisplayNameAsync(new UserId(user), "Kept Name");

        attempts.Started.Should().Be(1);
        snapshot.DisplayName.Should().Be("Kept Name");
        (await RowsAsync("user", user)).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.DisplayNameUpdated);
    }

    [Fact]
    public async Task A_setup_completion_whose_acknowledgement_is_lost_is_reported_done_with_its_rows_once()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (invitee, _) = await AddUserAsync("invitee");
        var mail = new RecordingMail();
        await using (var db = _db.NewDb())
            await Invitations(db, new Principal(admin), mail).SendSetupInvitationAsync(new UserId(invitee), "invitee@example.com");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
            await Invitations(db, principal: null, mail).CompletePasswordSetupAsync(mail.SingleLink(), "chosen-harbor-passphrase");

        attempts.Started.Should().Be(1, "a consumed link would otherwise make a rerun look invalid");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{invitee}';")).Should().Be(1);
        (await RowsAsync("user", invitee)).Count(r => (string)r["Action"]! == AuditActionNames.PasswordSetupCompleted).Should().Be(1);
    }

    [Fact]
    public async Task An_enrollment_whose_audit_fails_registers_nothing_and_one_whose_acknowledgement_is_lost_is_reported_done()
    {
        var (admin, _) = await AddUserAsync("admin");
        var environment = new FixedEnvironment("local");
        Apperture.Core.Security.Trust.EnrollmentInvitation invitation;
        await using (var db = _db.NewDb())
            invitation = await new SqlRuntimeEnrollmentService(db, environment, NewUnit(db, new Principal(admin))).CreateInvitationAsync("rt-m6");
        var registration = new Apperture.Core.Security.Trust.RuntimeIdentityRegistration("Runtime", "PublicKey", "pk", null);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => new SqlRuntimeEnrollmentService(db, environment, NewUnit(db, null)).EnrollAsync(invitation.Token, registration).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM RuntimeIdentities WHERE Id = N'rt-m6';")).Should().Be(0);
        (await _db.ScalarAsync<bool>("SELECT Used FROM EnrollmentTokens WHERE RuntimeId = N'rt-m6';")).Should().BeFalse();

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        Apperture.Core.Security.Trust.EnrollmentResult result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await new SqlRuntimeEnrollmentService(db, environment, NewUnit(db, null)).EnrollAsync(invitation.Token, registration);

        attempts.Started.Should().Be(1);
        result.Success.Should().BeTrue("a verified commit is success, not 'token already used'");
        result.RuntimeId.Should().Be("rt-m6");
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private SqlAuditEventWriter Events(IPrincipalContext? principal) =>
        new(_db.Options(), principal, HarnessCatalog, null, null, null);

    private static AccountSecurityLinkBuilder Links() => new(new ConfigurationBuilder().Build());

    private AccountInvitationService Invitations(AppertureDbContext db, IPrincipalContext? principal, RecordingMail mail)
    {
        var tokenRows = new SqlAccountActionTokenRepository(db);
        return new AccountInvitationService(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(tokenRows, TimeProvider.System),
            tokenRows,
            mail,
            Links(),
            TimeProvider.System,
            NewUnit(db, principal),
            Events(principal));
    }

    private UserManagementService CreatingService(AppertureDbContext db, IPrincipalContext principal, RecordingMail mail)
    {
        var unit = NewUnit(db, principal);
        return RenameService(
            db,
            principal,
            unit,
            Events(principal),
            invitations: Invitations(db, principal, mail),
            actionTokens: new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System));
    }

    private ForgottenPasswordService Forgotten(AppertureDbContext db, RecordingMail mail) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System),
            mail,
            Links(),
            NewUnit(db, null),
            Events(null));

    private AccountContactService Contact(AppertureDbContext db, IPrincipalContext principal, RecordingMail mail) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System),
            mail,
            Links(),
            auditedUnitOfWork: NewUnit(db, principal),
            auditEvents: Events(principal));

    private AccountProfileService Profile(AppertureDbContext db, IPrincipalContext principal) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlUserPreferencesRepository(db),
            new SqlUserProfileImageStore(db),
            new SkiaProfileImageNormalizer(),
            new Apperture.ControlPlane.Security.OperatorNotificationSubscriptionService(
                new SqlNotificationDefinitionStore(db), new SqlNotificationSubscriptionStore(db), new SqlNotificationUserPreferenceStore(db)),
            new UnusedToastPreferences(),
            NullLogger<AccountProfileService>.Instance,
            NewUnit(db, principal),
            Events(principal));

    private async Task<(string Id, string Hash, bool Active)> SingleTokenAsync(string userId)
    {
        var row = (await _db.RowsAsync($"SELECT Id, TokenHash, ConsumedAtUtc, RevokedAtUtc FROM AccountActionTokens WHERE UserId = N'{userId}' AND Purpose = {SetupPurpose};"))
            .Should().ContainSingle().Which;
        return ((string)row["Id"]!, (string)row["TokenHash"]!, row["ConsumedAtUtc"] is null && row["RevokedAtUtc"] is null);
    }

    private Task<List<Dictionary<string, object?>>> RowsAsync(string targetType, string targetId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'{targetType}' AND TargetId = N'{targetId}' ORDER BY Id;");

    private static string? Field(Dictionary<string, object?> row, string name)
    {
        using var json = JsonDocument.Parse((string)row["Details"]!);
        return json.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static string Hash(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();

    private sealed class RecordingMail : IAccountSecurityMailSender
    {
        public List<(string Recipient, string Body)> Sent { get; } = [];
        public bool Succeed { get; set; } = true;
        public bool Throw { get; set; }

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
            if (Throw)
                throw new InvalidOperationException("Injected: the transport threw after the message may have left.");
            return ValueTask.FromResult(new AccountSecurityMailResult(Succeed, Succeed ? null : "smtp relay refused the message"));
        }
    }

    private sealed class FixedEnvironment(string environmentId) : Apperture.Shared.Kernel.IEnvironmentContext
    {
        public string EnvironmentId { get; } = environmentId;
    }

    /// <summary>Toast preferences are not exercised here.</summary>
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
