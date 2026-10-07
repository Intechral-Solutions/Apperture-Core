using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
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
    public async Task First_contact_email_requires_recent_account_verification()
    {
        var harness = Harness.LocalUser(accountVerification: StubRecentAccountVerification.Unsatisfied());

        var act = () => harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null).AsTask();

        await act.Should().ThrowAsync<RecentAccountVerificationException>()
            .Where(ex => ex.Code == RecentAccountVerificationErrorCodes.Required);
        harness.Users.Get(harness.User.Id.Value).PendingEmailAddress.Should().BeNull();
    }

    [Fact]
    public async Task First_contact_email_succeeds_after_recent_account_verification()
    {
        var harness = Harness.LocalUser(accountVerification: StubRecentAccountVerification.Satisfied());

        var requested = await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);

        requested.Contact.Status.Should().Be("pending");
        harness.Users.Get(harness.User.Id.Value).PendingEmailAddress.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Resend_verification_requires_recent_account_verification()
    {
        var harness = Harness.LocalUser(accountVerification: StubRecentAccountVerification.Satisfied());
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var gated = Harness.Reuse(harness, StubRecentAccountVerification.Unsatisfied());

        var act = () => gated.Contact.ResendAsync(harness.User.Id).AsTask();

        await act.Should().ThrowAsync<RecentAccountVerificationException>()
            .Where(ex => ex.Code == RecentAccountVerificationErrorCodes.Required);
    }

    [Fact]
    public async Task Token_verification_does_not_require_recent_account_verification()
    {
        var harness = Harness.LocalUser(accountVerification: StubRecentAccountVerification.Satisfied());
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        var token = harness.LastToken();
        var ungated = Harness.Reuse(harness, StubRecentAccountVerification.Unsatisfied());

        await ungated.Contact.VerifyAsync(token);

        harness.Users.Get(harness.User.Id.Value).HasVerifiedContactEmail.Should().BeTrue();
    }

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
        harness.Audit.Actions.Contains(AccountSecurityAuditActionNames.EmailVerified).Should().BeTrue();
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
        harness.Audit.Actions.Contains(AccountSecurityAuditActionNames.EmailChanged).Should().BeTrue();
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
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow)
        { Username = "other" };
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
            IsBreakGlassEligible: false)
        { Username = "entra.user" };
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
        harness.Audit.Events.Should().NotContain(e => e.Event.Fields.Values.Any(v => v != null && v.ToString()!.Contains(plaintext, StringComparison.Ordinal)));
        harness.Audit.Events.Should().NotContain(e => e.Event.Fields.Keys.Any(AuditDetailSchema.IsSensitiveFieldName));

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
        harness.Audit.Actions.Contains(AccountSecurityAuditActionNames.InvitationResent).Should().BeTrue();
        harness.Audit.Actions.Contains(AccountSecurityAuditActionNames.InvitationAccepted).Should().BeTrue();
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
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow)
        { Username = EmergencyAccessIdentity.LocalUsername };
        var entraOnly = new User(
            new UserId("usr-entra-only"),
            "Entra only",
            [new IdentityLink(IdentitySource.Entra, "oid-2", DateTimeOffset.UtcNow, MailSnapshot: "entra@example.com")],
            [],
            IsBreakGlassEligible: false,
            EmailAddress: "entra@example.com",
            EmailVerifiedAtUtc: DateTimeOffset.UtcNow)
        { Username = "entra.only" };
        var unverified = new User(
            new UserId("usr-unverified"),
            "Unverified",
            [new IdentityLink(IdentitySource.Local, "unverified", DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: false,
            PendingEmailAddress: "pending@example.com")
        { Username = "unverified" };
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
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername };
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

    // ---- AUD-1.2: who each row names, and what it never contains ----

    [Fact]
    public async Task An_invitation_is_the_current_principals_change_against_the_invited_user_and_its_send_has_its_own_row()
    {
        var harness = Harness.LocalUser(withCredential: false);

        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");

        var token = harness.Tokens.All.Should().ContainSingle().Subject;
        harness.Audit.Actions.Should().Equal(AuditActionNames.InvitationSent, AuditActionNames.SecurityMailDeliveryAttempted);
        var (sent, sentActor) = harness.Audit.Events[0];
        sentActor.Should().Be(AuditActor.CurrentPrincipal, "the administrator invites; the invited user is the target (§15.3)");
        sent.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, harness.User.Id.Value));
        sent.Fields["requestId"].Should().Be(token.Id);
        var (delivery, deliveryActor) = harness.Audit.Events[1];
        deliveryActor.Should().Be(AuditActor.CurrentPrincipal);
        delivery.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        delivery.Fields["purpose"].Should().Be("setup_invitation");
        harness.Audit.Events.Should().NotContain(e => e.Event.Fields.Values.Any(v => v != null && v.ToString()!.Contains('@')),
            "the contact address is never recorded");
    }

    [Fact]
    public async Task A_resend_is_recorded_as_resent_and_a_failed_send_never_undoes_it()
    {
        var harness = Harness.LocalUser(withCredential: false);
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        harness.Audit.Clear();
        harness.Mail.Succeed = false;

        var result = await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");

        result.DeliverySucceeded.Should().BeFalse();
        harness.Audit.Actions.Should().Equal(AuditActionNames.InvitationResent, AuditActionNames.SecurityMailDeliveryAttempted);
        var delivery = harness.Audit.Last;
        delivery.OutcomeCode.Should().Be(AuditOutcomeCodes.Failed);
        delivery.Fields["failure"].Should().Be("send_failed");
        harness.Tokens.All.Should().ContainSingle(t => t.IsActive(harness.Clock.GetUtcNow()), "the reissued link stays committed");
    }

    [Fact]
    public async Task Completing_setup_is_recorded_as_the_user_the_link_was_issued_to()
    {
        var harness = Harness.LocalUser(withCredential: false);
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        var requestId = harness.Tokens.All.Single().Id;
        harness.Audit.Clear();

        await harness.Invitations.CompletePasswordSetupAsync(harness.LastToken(), "chosen-harbor-passphrase");

        harness.Audit.Actions.Should().Equal(
            AuditActionNames.PasswordSetupCompleted, AuditActionNames.InvitationAccepted, AuditActionNames.EmailVerified);
        harness.Audit.Events.Should().OnlyContain(e => e.Actor == AuditActor.AuthenticatedUser(harness.User.Id));
        harness.Audit.Single(AuditActionNames.InvitationAccepted).Fields["requestId"].Should().Be(requestId);
        harness.Audit.Single(AuditActionNames.EmailVerified).Fields["via"].Should().Be("setup_invitation");
    }

    [Fact]
    public async Task A_refused_setup_completion_leaves_the_link_usable_and_records_nothing()
    {
        var harness = Harness.LocalUser(withCredential: false);
        await harness.Invitations.SendSetupInvitationAsync(harness.User.Id, "invitee@example.com");
        var link = harness.LastToken();
        harness.Audit.Clear();

        var refused = () => harness.Invitations.CompletePasswordSetupAsync(link, "localuser-harbor-passphrase").AsTask();

        await refused.Should().ThrowAsync<AccountSecurityException>().Where(ex => ex.Code == AccountSecurityErrorCodes.PasswordRequired);
        harness.Audit.Events.Should().BeEmpty();
        harness.Tokens.All.Should().ContainSingle(t => t.IsActive(harness.Clock.GetUtcNow()), "the refusal came before the consumption");
        await harness.Invitations.CompletePasswordSetupAsync(link, "chosen-harbor-passphrase");
    }

    [Fact]
    public async Task Asking_for_the_already_verified_address_changes_nothing_records_nothing_and_answers_as_before()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());
        harness.Audit.Clear();
        harness.Mail.Sent.Clear();
        var tokens = harness.Tokens.All.Count;

        var result = await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", "unique-local-passphrase");

        result.DeliverySucceeded.Should().BeTrue();
        result.Contact.Status.Should().Be("verified");
        harness.Audit.Events.Should().BeEmpty("the request changed nothing (D7)");
        harness.Mail.Sent.Should().BeEmpty();
        harness.Tokens.All.Should().HaveCount(tokens);
    }

    [Fact]
    public async Task Contact_requests_are_the_signed_in_users_and_a_link_is_used_as_the_user_it_was_issued_to()
    {
        var harness = Harness.LocalUser();

        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.ResendAsync(harness.User.Id);
        await harness.Contact.VerifyAsync(harness.LastToken());

        harness.Audit.Actions.Should().Equal(
            AuditActionNames.EmailVerificationRequested, AuditActionNames.SecurityMailDeliveryAttempted,
            AuditActionNames.EmailVerificationRequested, AuditActionNames.SecurityMailDeliveryAttempted,
            AuditActionNames.EmailVerified);
        harness.Audit.Events.Take(4).Should().OnlyContain(e => e.Actor == AuditActor.CurrentPrincipal);
        harness.Audit.Events[0].Event.Fields["resent"].Should().Be(false);
        harness.Audit.Events[2].Event.Fields["resent"].Should().Be(true);
        harness.Audit.Events[4].Actor.Should().Be(AuditActor.AuthenticatedUser(harness.User.Id));
        harness.Audit.Events[4].Event.Fields["via"].Should().Be("verification_link");
    }

    [Fact]
    public async Task A_reset_request_is_anonymous_and_one_matching_no_account_records_only_that_it_was_suppressed()
    {
        var harness = Harness.LocalUser();
        await harness.Contact.RequestAsync(harness.User.Id, "user@example.com", null);
        await harness.Contact.VerifyAsync(harness.LastToken());
        harness.Audit.Clear();

        await harness.Forgotten.RequestAsync("nobody@example.com");
        await harness.Forgotten.RequestAsync("localuser");
        await harness.Forgotten.CompleteAsync(harness.LastToken(), "chosen-harbor-passphrase");

        harness.Audit.Actions.Should().Equal(
            AuditActionNames.PasswordResetRequestSuppressed,
            AuditActionNames.PasswordResetRequested, AuditActionNames.SecurityMailDeliveryAttempted,
            AuditActionNames.PasswordResetCompleted);
        var (suppressed, suppressedActor) = harness.Audit.Events[0];
        suppressedActor.Should().Be(AuditActor.Anonymous);
        suppressed.Target.Should().BeNull();
        suppressed.Fields.Should().BeEmpty("neither the identifier nor any account is recorded");
        harness.Audit.Events[1].Actor.Should().Be(AuditActor.Anonymous, "the requester is not authenticated (§15.3)");
        harness.Audit.Events[1].Event.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, harness.User.Id.Value));
        harness.Audit.Events[2].Actor.Should().Be(AuditActor.Anonymous);
        harness.Audit.Events[3].Actor.Should().Be(AuditActor.AuthenticatedUser(harness.User.Id));
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
        public required AuditRecording Audit { get; init; }
        public required MutableTimeProvider Clock { get; init; }
        public required LocalPasswordHasher Hasher { get; init; }
        public required AccountContactService Contact { get; init; }
        public required AccountInvitationService Invitations { get; init; }
        public required ForgottenPasswordService Forgotten { get; init; }

        public string LastToken() => ExtractToken(Mail.Sent[^1].Body);

        public static Harness LocalUser(
            string password = "unique-local-passphrase",
            bool withCredential = true,
            IReadOnlyList<User>? extraUsers = null,
            IRecentAccountVerificationGate? accountVerification = null)
        {
            var user = new User(
                new UserId("usr-local"),
                "Local user",
                [new IdentityLink(IdentitySource.Local, "localuser", DateTimeOffset.UtcNow)],
                [],
                IsBreakGlassEligible: false)
            { Username = "localuser" };
            return ForUser(user, password, withCredential, extraUsers, accountVerification);
        }

        public static Harness ForUser(
            User user,
            string password = "unique-local-passphrase",
            bool withCredential = true,
            IReadOnlyList<User>? extraUsers = null,
            IRecentAccountVerificationGate? accountVerification = null)
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
            var audit = new AuditRecording();
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
                Contact = new AccountContactService(users, credentials, hasher, tokenService, mail, links, accountVerification: accountVerification, auditedUnitOfWork: audit, auditEvents: audit),
                Invitations = new AccountInvitationService(users, credentials, hasher, tokenService, tokens, mail, links, clock, audit, audit),
                Forgotten = new ForgottenPasswordService(users, credentials, hasher, tokenService, mail, links, audit, audit)
            };
        }

        public static Harness Reuse(Harness source, IRecentAccountVerificationGate accountVerification)
        {
            var tokenService = new AccountActionTokenService(source.Tokens, source.Clock);
            var links = new AccountSecurityLinkBuilder(new ConfigurationBuilder().Build());
            return new Harness
            {
                User = source.User,
                Users = source.Users,
                Credentials = source.Credentials,
                Tokens = source.Tokens,
                Mail = source.Mail,
                Audit = source.Audit,
                Clock = source.Clock,
                Hasher = source.Hasher,
                Contact = new AccountContactService(
                    source.Users,
                    source.Credentials,
                    source.Hasher,
                    tokenService,
                    source.Mail,
                    links,
                    accountVerification: accountVerification,
                    auditedUnitOfWork: source.Audit,
                    auditEvents: source.Audit),
                Invitations = source.Invitations,
                Forgotten = source.Forgotten
            };
        }
    }

    private sealed class StubRecentAccountVerification : IRecentAccountVerificationGate
    {
        private readonly RecentAccountVerification _current;

        private StubRecentAccountVerification(RecentAccountVerification current) => _current = current;

        public static StubRecentAccountVerification Satisfied() =>
            new(new RecentAccountVerification(
                RecentAccountVerificationMechanisms.LocalPassword,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(14),
                IsSatisfied: true));

        public static StubRecentAccountVerification Unsatisfied() => new(RecentAccountVerification.None);

        public RecentAccountVerification GetCurrent() => _current;

        public void Require()
        {
            if (!_current.IsSatisfied)
            {
                throw new RecentAccountVerificationException(
                    RecentAccountVerificationErrorCodes.Required,
                    "Verify your identity to manage these account settings.");
            }
        }
    }
}
