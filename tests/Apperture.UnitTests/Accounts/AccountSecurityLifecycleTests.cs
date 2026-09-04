using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.Accounts;

public sealed class AccountSecurityLifecycleTests
{
    [Fact]
    public async Task Unverified_address_stays_unverified_until_token_is_consumed()
    {
        var harness = Harness.LocalUser();

        var requested = await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);

        requested.Contact.Status.Should().Be("pending");
        requested.Contact.EmailAddress.Should().BeNull();
        harness.Users.Get(harness.User.Id.Value).HasVerifiedContactEmail.Should().BeFalse();

        await harness.Contact.VerifyAsync(harness.LastToken());

        var user = harness.Users.Get(harness.User.Id.Value);
        user.HasVerifiedContactEmail.Should().BeTrue();
        user.EmailAddress.Should().Be("user@example.com");
        user.PendingEmailAddress.Should().BeNull();
        harness.Audit.HasAction(AccountSecurityAuditActionNames.EmailVerified).Should().BeTrue();
    }

    [Fact]
    public async Task Expired_and_used_verification_tokens_fail()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var first = harness.LastToken();

        harness.Clock.UtcNow = harness.Clock.UtcNow.AddHours(25);
        var expired = () => harness.Contact.VerifyAsync(first).AsTask();
        await expired.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.TokenInvalid);

        harness.Clock.UtcNow = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var valid = harness.LastToken();
        await harness.Contact.VerifyAsync(valid);
        var replay = () => harness.Contact.VerifyAsync(valid).AsTask();
        await replay.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task Newer_verification_token_invalidates_the_older_one()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var older = harness.LastToken();
        await harness.Contact.ResendAsync(harness.User.Id);
        var newer = harness.LastToken();

        var stale = () => harness.Contact.VerifyAsync(older).AsTask();
        await stale.Should().ThrowAsync<AccountSecurityException>();
        await harness.Contact.VerifyAsync(newer);
        harness.Users.Get(harness.User.Id.Value).HasVerifiedContactEmail.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_email_keeps_the_current_verified_address_until_the_new_one_is_verified()
    {
        var harness = Harness.LocalUser(password: "unique-local-passphrase");
        await harness.Contact.RequestAsync(harness.User.Id, "old@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());

        await harness.Contact.RequestAsync(harness.User.Id, "new@example.com", "unique-local-passphrase");

        var user = harness.Users.Get(harness.User.Id.Value);
        user.EmailAddress.Should().Be("old@example.com");
        user.HasVerifiedContactEmail.Should().BeTrue();
        user.PendingEmailAddress.Should().Be("new@example.com");

        await harness.Contact.VerifyAsync(harness.LastToken());
        user = harness.Users.Get(harness.User.Id.Value);
        user.EmailAddress.Should().Be("new@example.com");
        user.PendingEmailAddress.Should().BeNull();
        harness.Audit.HasAction(AccountSecurityAuditActionNames.EmailChanged).Should().BeTrue();
    }

    [Fact]
    public async Task Email_never_creates_or_matches_an_identity_link()
    {
        var other = new User(
            new UserId("usr-other"),
            "Other",
            [new IdentityLink(IdentitySource.Local, "other", DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: false,
            EmailAddress: "shared@example.com",
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow);
        var harness = Harness.LocalUser(extraUsers: [other]);

        var act = () => harness.Contact.RequestAsync(harness.User.Id, "shared@example.com", null).AsTask();
        await act.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.EmailInUse);

        harness.Users.Get(harness.User.Id.Value).IdentityLinks.Should().NotContain(l =>
            string.Equals(l.ExternalId, "shared@example.com", StringComparison.OrdinalIgnoreCase));
        other.IdentityLinks.Should().ContainSingle(l => l.ExternalId == "other");
    }

    [Fact]
    public async Task Entra_mail_snapshot_is_suggested_but_not_verified()
    {
        var user = new User(
            new UserId("usr-entra"),
            "Entra user",
            [new IdentityLink(
                IdentitySource.Entra,
                "oid-1",
                DateTimeOffset.UtcNow,
                TenantId: "tid-1",
                MailSnapshot: "directory@example.com",
                UserPrincipalNameSnapshot: "upn@example.com")],
            [],
            IsBreakGlassEligible: false);
        var harness = Harness.ForUser(user);

        var snapshot = await harness.Contact.GetAsync(user.Id);
        snapshot.SuggestedEntraEmail.Should().Be("directory@example.com");
        snapshot.Status.Should().Be("none");
        snapshot.EmailAddress.Should().BeNull();
        snapshot.HasEnabledLocalPassword.Should().BeFalse();
        user.HasVerifiedContactEmail.Should().BeFalse();
    }

    [Fact]
    public async Task Enabled_local_password_is_reported_only_when_local_sign_in_is_usable()
    {
        var harness = Harness.LocalUser(password: "unique-local-passphrase");
        var enabled = await harness.Contact.GetAsync(harness.User.Id);
        enabled.HasEnabledLocalPassword.Should().BeTrue();

        await harness.Credentials.SetDisabledAsync(harness.User.Id, true);
        var disabled = await harness.Contact.GetAsync(harness.User.Id);
        disabled.HasEnabledLocalPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Expired_or_rejected_invitation_can_be_resent_without_consuming_the_prior_token()
    {
        var harness = Harness.LocalUser(withCredential: false);
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        var first = harness.LastToken();

        var tooShort = () => harness.Invitations.CompletePasswordSetupAsync(first, "x").AsTask();
        await tooShort.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.PasswordRequired);
        var stillPending = await harness.Invitations.GetStatusAsync(harness.User.Id);
        stillPending!.Pending.Should().BeTrue();

        harness.Clock.UtcNow = harness.Clock.UtcNow.AddHours(73);
        var expired = await harness.Invitations.GetStatusAsync(harness.User.Id);
        expired!.Expired.Should().BeTrue();
        expired.Pending.Should().BeFalse();

        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        await harness.Invitations.CompletePasswordSetupAsync(harness.LastToken(), "chosen-harbor-passphrase");
        harness.Credentials.Rows.Should().ContainKey(harness.User.Id.Value);
    }

    [Fact]
    public async Task Plaintext_token_is_not_persisted_and_wrong_purpose_cannot_be_reused()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var plaintext = harness.LastToken();

        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
        harness.Tokens.All.Should().OnlyContain(t => t.TokenHash != plaintext);
        harness.Tokens.All.Should().OnlyContain(t => t.TokenHash == expectedHash);
        harness.Audit.DetailsContain(plaintext).Should().BeFalse();
        harness.Audit.Records.Any(r => r.Details is not null && r.Details.Contains("token=", StringComparison.Ordinal)).Should().BeFalse();

        var reset = () => harness.Forgotten.CompleteAsync(plaintext, "NewPassword1!").AsTask();
        await reset.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task Invitation_lets_the_user_choose_a_password_once_and_verifies_contact_email()
    {
        var harness = Harness.LocalUser(withCredential: false);

        var sent = await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        sent.DeliverySucceeded.Should().BeTrue();
        harness.Credentials.Rows.Should().BeEmpty();

        await harness.Invitations.CompletePasswordSetupAsync(harness.LastToken(), "chosen-harbor-passphrase");

        var cred = harness.Credentials.Rows[harness.User.Id.Value];
        harness.Hasher.VerifyPassword("chosen-harbor-passphrase", cred.PasswordHash, cred.Salt).Should().BeTrue();
        cred.IsDisabled.Should().BeFalse();
        harness.Users.Get(harness.User.Id.Value).HasVerifiedContactEmail.Should().BeTrue();
        harness.Users.Get(harness.User.Id.Value).EmailAddress.Should().Be("invitee@example.com");

        var consumed = ExtractToken(harness.Mail.Sent[0].Body);
        var reused = () => harness.Invitations.CompletePasswordSetupAsync(consumed, "Other#Password1").AsTask();
        await reused.Should().ThrowAsync<AccountSecurityException>();
    }

    [Fact]
    public async Task Invitation_resend_invalidates_the_prior_token()
    {
        var harness = Harness.LocalUser(withCredential: false);
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        var first = harness.LastToken();
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        var second = harness.LastToken();

        var stale = () => harness.Invitations.CompletePasswordSetupAsync(first, "chosen-harbor-passphrase").AsTask();
        await stale.Should().ThrowAsync<AccountSecurityException>();
        await harness.Invitations.CompletePasswordSetupAsync(second, "chosen-harbor-passphrase");
        harness.Audit.HasAction(AccountSecurityAuditActionNames.InvitationResent).Should().BeTrue();
        harness.Audit.HasAction(AccountSecurityAuditActionNames.InvitationAccepted).Should().BeTrue();
    }

    [Fact]
    public async Task Password_reset_does_not_enable_a_disabled_local_credential()
    {
        var harness = Harness.LocalUser(password: "old-harbor-passphrase");
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());
        await harness.Credentials.SetDisabledAsync(harness.User.Id, true);
        harness.Mail.Sent.Clear();

        await harness.Forgotten.RequestAsync("user@example.com");
        harness.Mail.Sent.Should().BeEmpty();

        var cred = harness.Credentials.Rows[harness.User.Id.Value];
        cred.IsDisabled.Should().BeTrue();
        harness.Hasher.VerifyPassword("old-harbor-passphrase", cred.PasswordHash, cred.Salt).Should().BeTrue();
        harness.Tokens.All.Should().NotContain(t => t.Purpose == AccountActionTokenPurpose.PasswordReset && t.IsActive(harness.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task Forgot_password_is_generic_for_unknown_unverified_entra_only_and_recovery_accounts()
    {
        var recovery = new User(
            new UserId("usr-recovery"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: true,
            EmailAddress: "recovery@example.com",
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow);
        var entraOnly = new User(
            new UserId("usr-entra-only"),
            "Entra only",
            [new IdentityLink(IdentitySource.Entra, "oid-2", DateTimeOffset.UtcNow, MailSnapshot: "entra@example.com")],
            [],
            IsBreakGlassEligible: false,
            EmailAddress: "entra@example.com",
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow);
        var unverified = new User(
            new UserId("usr-unverified"),
            "Unverified",
            [new IdentityLink(IdentitySource.Local, "unverified", DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: false,
            PendingEmailAddress: "pending@example.com");
        var harness = Harness.LocalUser(extraUsers: [recovery, entraOnly, unverified]);
        await harness.Credentials.SetPasswordAsync(recovery.Id, "h", "s", "x");
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());
        harness.Mail.Sent.Clear();

        await harness.Forgotten.RequestAsync("nobody@example.com");
        await harness.Forgotten.RequestAsync("breakglass");
        await harness.Forgotten.RequestAsync("entra@example.com");
        await harness.Forgotten.RequestAsync("unverified");
        await harness.Forgotten.RequestAsync("user@example.com");

        harness.Mail.Sent.Should().ContainSingle();
        harness.Mail.Sent[0].Recipient.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Forgot_password_is_eligible_for_enabled_local_credential_with_verified_email()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());
        harness.Mail.Sent.Clear();

        await harness.Forgotten.RequestAsync("localuser");

        harness.Mail.Sent.Should().ContainSingle();
        harness.Tokens.All.Should().Contain(t => t.Purpose == AccountActionTokenPurpose.PasswordReset && t.IsActive(harness.Clock.GetUtcNow()));
        await harness.Forgotten.CompleteAsync(harness.LastToken(), "chosen-harbor-passphrase");
        harness.Hasher.VerifyPassword("chosen-harbor-passphrase", harness.Credentials.Rows[harness.User.Id.Value].PasswordHash, harness.Credentials.Rows[harness.User.Id.Value].Salt)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Recovery_access_is_excluded_from_contact_and_invitation_flows()
    {
        var recovery = new User(
            new UserId("usr-recovery"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: true);
        var harness = Harness.ForUser(recovery);

        var contact = () => harness.Contact.RequestAsync(recovery.Id, "recovery@example.com", null).AsTask();
        await contact.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.SystemManaged);

        var invite = () => harness.Invitations.SendSetupInvitationAsync(recovery.Id, "recovery@example.com").AsTask();
        await invite.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.SystemManaged);
    }

    [Fact]
    public void Contact_email_normalization_rejects_identity_like_garbage()
    {
        ContactEmailAddress.Normalize("not-an-email").Should().BeNull();
        ContactEmailAddress.Normalize("a@b@c.com").Should().BeNull();
        ContactEmailAddress.Normalize("User@Example.COM").Should().Be("user@example.com");
    }

    private static string ExtractToken(string body)
    {
        var marker = "token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var rest = body[(start + marker.Length)..];
        var end = rest.IndexOfAny(['\r', '\n', ' ', '&']);
        return end < 0 ? rest : rest[..end];
    }

    private sealed class Harness
    {
        public required User User { get; init; }
        public required InMemoryUserRepository Users { get; init; }
        public required InMemoryLocalCredentialRepository Credentials { get; init; }
        public required InMemoryAccountActionTokenRepository Tokens { get; init; }
        public required RecordingAccountSecurityMailSender Mail { get; init; }
        public required RecordingAuditService Audit { get; init; }
        public required MutableTimeProvider Clock { get; init; }
        public required LocalPasswordHasher Hasher { get; init; }
        public required AccountContactService Contact { get; init; }
        public required AccountInvitationService Invitations { get; init; }
        public required ForgottenPasswordService Forgotten { get; init; }

        public string LastToken() => ExtractToken(Mail.Sent[^1].Body);

        public static Harness LocalUser(
            string password = "unique-local-passphrase",
            bool withCredential = true,
            IReadOnlyList<User>? extraUsers = null)
        {
            var user = new User(
                new UserId("usr-local"),
                "Local user",
                [new IdentityLink(IdentitySource.Local, "localuser", DateTimeOffset.UtcNow)],
                [],
                IsBreakGlassEligible: false);
            return ForUser(user, password, withCredential, extraUsers);
        }

        public static Harness ForUser(
            User user,
            string password = "unique-local-passphrase",
            bool withCredential = true,
            IReadOnlyList<User>? extraUsers = null)
        {
            var users = new InMemoryUserRepository([user, .. extraUsers ?? []]);
            var credentials = new InMemoryLocalCredentialRepository();
            var hasher = new LocalPasswordHasher();
            if (withCredential && user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            {
                var (hash, salt) = hasher.HashPassword(password);
                credentials.SetPasswordAsync(user.Id, hash, salt, hasher.AlgorithmName).AsTask().GetAwaiter().GetResult();
            }

            var tokens = new InMemoryAccountActionTokenRepository();
            var mail = new RecordingAccountSecurityMailSender();
            var audit = new RecordingAuditService();
            var clock = new MutableTimeProvider();
            var tokenService = new AccountActionTokenService(tokens, clock);
            var links = new AccountSecurityLinkBuilder(new ConfigurationBuilder().Build());
            return new Harness
            {
                User = user,
                Users = users,
                Credentials = credentials,
                Tokens = tokens,
                Mail = mail,
                Audit = audit,
                Clock = clock,
                Hasher = hasher,
                Contact = new AccountContactService(users, credentials, hasher, tokenService, mail, links, audit),
                Invitations = new AccountInvitationService(users, credentials, hasher, tokenService, tokens, mail, links, audit, clock),
                Forgotten = new ForgottenPasswordService(users, credentials, hasher, tokenService, mail, links, audit)
            };
        }
    }
}
