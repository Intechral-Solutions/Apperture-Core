using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The persistence of a non-destructive authenticator replacement against real SQL Server on both isolation modes: a candidate is
/// staged beside the active authenticator, verified, and switched in one atomic step. Every refusal is asserted twice: by its typed
/// outcome, and by the active authenticator and recovery codes being byte-for-byte what they were. Fails, never skips, without SQL
/// Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;

    public MfaReplacementCandidateSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private Task<MfaCandidateWorld> WorldAsync(bool rcsi, bool enrolled = true) => MfaCandidateWorld.CreateAsync(_catalogs, rcsi, enrolled);

    // ---- Staging: identity, ownership, expiry by the database's clock ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Staging_creates_a_staged_candidate_owned_by_the_user_and_bound_to_the_active_credential_without_touching_it(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var activeBefore = await world.ActiveAsync();
        var credentialBefore = await world.ActiveCredentialIdAsync();
        var (plain, protectedSecret) = await world.NewSecretAsync();

        var candidate = await world.StageAsync(lifetime: TimeSpan.FromMinutes(10), protectedSecret: protectedSecret);

        candidate.UserId.Should().Be(world.UserId);
        candidate.Status.Should().Be(NativeMfaCandidateStatus.Staged);
        candidate.Version.Should().Be(1);
        candidate.StagedAgainstCredentialId.Should().Be(credentialBefore);
        candidate.CandidateId.Should().NotBeNullOrWhiteSpace();
        // Compared by digest: a failed array assertion prints the arrays, and one of these is a plaintext secret.
        MfaCandidateWorld.HashOf(candidate.ProtectedSecret).Should().Be(MfaCandidateWorld.HashOf(protectedSecret), "the candidate holds exactly the protected secret it was given");
        MfaCandidateWorld.HashOf(candidate.ProtectedSecret).Should().NotBe(MfaCandidateWorld.HashOf(plain), "the secret is stored protected");
        candidate.MatchedStep.Should().BeNull();
        candidate.VerifiedAtUtc.Should().BeNull();
        candidate.IsExpired.Should().BeFalse();
        (candidate.ExpiresAtUtc - candidate.CreatedAtUtc).Should().Be(TimeSpan.FromMinutes(10), "the expiry is the lifetime after the database's own clock");
        (await world.ActiveAsync()).Should().Be(activeBefore, "staging changes nothing of the active authenticator or its codes");
        (await world.LiveCandidateCountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_candidate_belongs_to_one_user_and_reads_as_missing_for_everyone_else(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var candidate = await world.StageAsync();
        var otherActive = await world.ActiveAsync(other);

        (await world.GetAsync(candidate.CandidateId, other)).Should().BeNull("another user cannot read it");
        (await world.VerifyAsync(candidate.CandidateId, candidate.Version, user: other)).Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);
        var request = world.Request(candidate.CandidateId, candidate.Version, user: other);
        (await world.SwitchAsync(request)).Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);

        (await world.GetAsync(candidate.CandidateId)).Should().NotBeNull();
        (await world.ActiveAsync(other)).Should().Be(otherActive, "the other user's authenticator was not touched");
        (await world.CandidateRowsAsync()).Should().ContainSingle().Which["Status"].Should().Be(0, "the candidate is still staged");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Verification_checks_the_owner_itself_not_only_the_credential_the_candidate_was_staged_against(bool rcsi)
    {
        // Credential identities are unique, so the staged-against check alone already refuses another user's candidate. Two users are given the
        // same identity here so that only the ownership condition of the statement stands between the second user and the first user's candidate.
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var candidate = await world.StageAsync();
        var shared = await world.ActiveCredentialIdAsync();
        await world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET CredentialId = N'{shared}' WHERE UserId = N'{other.Value}';");

        var result = await world.VerifyAsync(candidate.CandidateId, candidate.Version, user: other);

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Staged, "another user cannot verify the candidate");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Staging_needs_an_enrolled_authenticator_and_writes_nothing_without_one(bool rcsi)
    {
        var none = await WorldAsync(rcsi, enrolled: false);
        (await none.TryStageAsync()).Outcome.Should().Be(NativeMfaCandidateOutcome.NoActiveEnrollment);
        (await none.CandidateRowCountAsync()).Should().Be(0);

        // A pending (not yet enrolled) credential is not something to replace either.
        await using (var db = none.Scratch.NewDb())
            await new SqlNativeTotpCredentialRepository(db).UpsertPendingAsync(none.UserId, [1, 2, 3], "k", DateTimeOffset.UtcNow);
        (await none.TryStageAsync()).Outcome.Should().Be(NativeMfaCandidateOutcome.NoActiveEnrollment);
        (await none.CandidateRowCountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 90000)]
    [InlineData(true, 90000)]
    public async Task Staging_refuses_a_lifetime_outside_one_second_to_one_day(bool rcsi, int seconds)
    {
        var world = await WorldAsync(rcsi);

        var act = async () => await world.TryStageAsync(lifetime: TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await world.CandidateRowCountAsync()).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_candidate_expires_by_the_database_clock_at_verification_and_at_the_switch(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var active = await world.ActiveAsync();

        // Expired before it was verified.
        var first = await world.StageAsync();
        await world.ExpireAsync(first.CandidateId);
        (await world.GetAsync(first.CandidateId))!.IsExpired.Should().BeTrue("the read reports the database's judgement");
        (await world.VerifyAsync(first.CandidateId, first.Version)).Outcome.Should().Be(NativeMfaCandidateOutcome.Expired);
        (await world.CandidateRowsAsync()).Single()["Status"].Should().Be(0, "a refused verification did not change the candidate");

        // Verified in time, expired before the switch.
        var (second, version) = await world.StageAndVerifyAsync();
        await world.ExpireAsync(second.CandidateId);
        (await world.SwitchAsync(world.Request(second.CandidateId, version))).Outcome.Should().Be(NativeMfaCandidateOutcome.Expired);

        (await world.ActiveAsync()).Should().Be(active, "an expired candidate never changes the active authenticator");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_short_lived_candidate_really_expires_when_the_database_clock_passes_its_expiry(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync(lifetime: TimeSpan.FromSeconds(1));
        var active = await world.ActiveAsync();

        // The lifetime is the only input: nothing but the database's clock moves the candidate to expired.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!(await world.GetAsync(candidate.CandidateId))!.IsExpired)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "a one-second candidate expires within seconds");
            await Task.Delay(250);
        }

        (await world.VerifyAsync(candidate.CandidateId, candidate.Version)).Outcome.Should().Be(NativeMfaCandidateOutcome.Expired);
        (await world.ActiveAsync()).Should().Be(active);
    }

    // ---- Superseding ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Staging_again_supersedes_the_live_candidate_which_can_then_never_be_verified_or_switched(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (first, firstVersion) = await world.StageAndVerifyAsync();
        var active = await world.ActiveAsync();

        var second = await world.StageAsync();

        second.CandidateId.Should().NotBe(first.CandidateId);
        var rows = await world.CandidateRowsAsync();
        rows.Should().HaveCount(2);
        rows.Single(r => (string)r["CandidateId"]! == first.CandidateId)["Status"].Should().Be((int)NativeMfaCandidateStatus.Superseded);
        rows.Single(r => (string)r["CandidateId"]! == first.CandidateId)["SecretBytes"].Should().Be(0L, "a superseded candidate keeps no secret");
        (await world.LiveCandidateCountAsync()).Should().Be(1, "at most one live candidate per user");
        (await world.GetAsync(first.CandidateId))!.ProtectedSecret.Should().BeEmpty();

        (await world.VerifyAsync(first.CandidateId, firstVersion)).Outcome.Should().Be(NativeMfaCandidateOutcome.Superseded);
        (await world.SwitchAsync(world.Request(first.CandidateId, firstVersion))).Outcome.Should().Be(NativeMfaCandidateOutcome.Superseded);
        (await world.ActiveAsync()).Should().Be(active, "a superseded candidate never changes the active authenticator");
        (await world.GetAsync(second.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Staged, "the newer candidate is unaffected");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_next_staging_purges_history_but_never_the_live_candidate_or_another_users_rows(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var othersCandidate = await world.StageAsync(user: other);
        var first = await world.StageAsync();
        var second = await world.StageAsync();   // first becomes history

        var third = await world.StageAsync();    // the purge removes first; second becomes history

        (await world.GetAsync(first.CandidateId)).Should().BeNull("history older than the previous candidate is purged");
        (await world.GetAsync(second.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Superseded);
        (await world.GetAsync(third.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Staged);
        (await world.GetAsync(othersCandidate.CandidateId, other))!.Status.Should().Be(NativeMfaCandidateStatus.Staged, "another user's candidate is never superseded or purged");
        (await world.VerifyAsync(first.CandidateId, first.Version)).Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);
    }

    // ---- Verification: compare-and-set on the version ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Verification_applies_once_records_the_matched_step_and_returns_the_next_version(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var active = await world.ActiveAsync();
        var candidate = await world.StageAsync();
        var step = MfaCandidateWorld.CurrentStep();

        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step);

        verified.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        verified.Version.Should().Be(candidate.Version + 1);
        var stored = (await world.GetAsync(candidate.CandidateId))!;
        stored.Status.Should().Be(NativeMfaCandidateStatus.Verified);
        stored.Version.Should().Be(candidate.Version + 1);
        stored.MatchedStep.Should().Be(step);
        stored.VerifiedAtUtc.Should().NotBeNull();
        MfaCandidateWorld.HashOf(stored.ProtectedSecret).Should().Be(MfaCandidateWorld.HashOf(candidate.ProtectedSecret));
        (await world.ActiveAsync()).Should().Be(active, "verifying a candidate changes nothing of the active authenticator");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_wrong_version_changes_nothing_at_verification_and_at_the_switch(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        var active = await world.ActiveAsync();

        (await world.VerifyAsync(candidate.CandidateId, candidate.Version + 5)).Outcome.Should().Be(NativeMfaCandidateOutcome.VersionMismatch);
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Staged);
        (await world.GetAsync(candidate.CandidateId))!.Version.Should().Be(candidate.Version);

        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version);
        var staleVersion = candidate.Version;   // the version before verification: a holder of the old state
        (await world.SwitchAsync(world.Request(candidate.CandidateId, staleVersion))).Outcome.Should().Be(NativeMfaCandidateOutcome.VersionMismatch);
        (await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value + 1))).Outcome.Should().Be(NativeMfaCandidateOutcome.VersionMismatch);

        (await world.ActiveAsync()).Should().Be(active);
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Verified, "the candidate is still verified at its version");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_candidate_is_verified_only_once(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        var first = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: 111);

        (await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: 222)).Outcome.Should().Be(NativeMfaCandidateOutcome.VersionMismatch, "the stale holder lost");
        (await world.VerifyAsync(candidate.CandidateId, first.Version!.Value, step: 222)).Outcome.Should().Be(NativeMfaCandidateOutcome.AlreadyVerified);

        (await world.GetAsync(candidate.CandidateId))!.MatchedStep.Should().Be(111, "the first matched step stands");
    }

    // ---- The switch ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_switch_replaces_the_secret_and_the_recovery_codes_consumes_the_candidate_and_gives_the_credential_a_new_identity(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (plain, protectedSecret) = await world.NewSecretAsync();
        // The enrolment stored the current step as last used; the candidate is proven at a later step so the switch must change the stored one.
        var matchedStep = MfaCandidateWorld.CurrentStep() + 5;
        var candidate = await world.StageAsync(protectedSecret: protectedSecret);
        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, matchedStep);
        verified.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        var version = verified.Version!.Value;
        var oldCredential = await world.ActiveCredentialIdAsync();
        var oldSecretHash = await world.ActiveSecretHashAsync();
        var oldCodes = await world.CodeIdsAsync();
        var request = world.Request(candidate.CandidateId, version);

        var result = await world.SwitchAsync(request);

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        result.NewCredentialId.Should().NotBeNullOrWhiteSpace().And.NotBe(oldCredential);
        (await world.ActiveCredentialIdAsync()).Should().Be(result.NewCredentialId);
        (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(protectedSecret)).And.NotBe(oldSecretHash);
        (await world.Scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';")).Should().BeTrue();
        (await world.Scratch.ScalarAsync<long>($"SELECT LastUsedStep FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';"))
            .Should().Be(matchedStep, "the matched step is now the last used step, so the verifying code cannot be replayed");
        var newCodes = string.Join(",", request.RecoveryCodes.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal));
        (await world.CodeIdsAsync()).Should().Be(newCodes).And.NotBe(oldCodes, "the recovery codes were replaced with the new set");
        var rows = await world.CandidateRowsAsync();
        rows.Should().ContainSingle();
        rows[0]["Status"].Should().Be((int)NativeMfaCandidateStatus.Consumed);
        rows[0]["SecretBytes"].Should().Be(0L, "a consumed candidate keeps no secret");
        (await world.LiveCandidateCountAsync()).Should().Be(0);

        // Signing in: the new secret is the one in force, and the verifying code is not accepted a second time.
        await using var db = world.Scratch.NewDb();
        var mfa = world.Mfa(db);
        (await mfa.VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(plain, DateTimeOffset.UtcNow.ToUnixTimeSeconds())))
            .Should().BeFalse("the step that proved possession is already used");
    }

    public enum Refusal
    {
        NotVerified,
        VersionMismatch,
        Expired,
        Superseded,
        Consumed,
        NotFound,
        OtherUsersCandidate,
        ActiveEnrollmentChanged,
        NoActiveEnrollment,
    }

    public static IEnumerable<object[]> EveryRefusalInBothModes() =>
        from rcsi in new[] { false, true }
        from refusal in Enum.GetValues<Refusal>()
        select new object[] { rcsi, refusal };

    [Theory]
    [MemberData(nameof(EveryRefusalInBothModes))]
    public async Task Every_refused_switch_reports_its_reason_and_leaves_the_active_authenticator_and_codes_untouched(bool rcsi, Refusal refusal)
    {
        var world = await WorldAsync(rcsi);
        NativeMfaSwitchRequest request;
        NativeMfaCandidateOutcome expected;
        MfaCandidateWorld.ActiveState? beforeOverride = null;

        switch (refusal)
        {
            case Refusal.NotVerified:
            {
                var candidate = await world.StageAsync();
                request = world.Request(candidate.CandidateId, candidate.Version);
                expected = NativeMfaCandidateOutcome.NotVerified;
                break;
            }
            case Refusal.VersionMismatch:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                request = world.Request(candidate.CandidateId, version - 1);
                expected = NativeMfaCandidateOutcome.VersionMismatch;
                break;
            }
            case Refusal.Expired:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                await world.ExpireAsync(candidate.CandidateId);
                request = world.Request(candidate.CandidateId, version);
                expected = NativeMfaCandidateOutcome.Expired;
                break;
            }
            case Refusal.Superseded:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                await world.StageAsync();
                request = world.Request(candidate.CandidateId, version);
                expected = NativeMfaCandidateOutcome.Superseded;
                break;
            }
            case Refusal.Consumed:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                (await world.SwitchAsync(world.Request(candidate.CandidateId, version, "first"))).IsApplied.Should().BeTrue();
                request = world.Request(candidate.CandidateId, version, "second");
                expected = NativeMfaCandidateOutcome.Consumed;
                break;
            }
            case Refusal.NotFound:
            {
                request = world.Request("no-such-candidate", 1);
                expected = NativeMfaCandidateOutcome.NotFound;
                break;
            }
            case Refusal.OtherUsersCandidate:
            {
                var other = await world.OtherEnrolledUserAsync();
                var (candidate, version) = await world.StageAndVerifyAsync();
                request = world.Request(candidate.CandidateId, version, user: other);
                expected = NativeMfaCandidateOutcome.NotFound;
                beforeOverride = await world.ActiveAsync(other);
                break;
            }
            case Refusal.ActiveEnrollmentChanged:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                // The credential is rewritten behind the candidate's back (nothing in the product does this without also removing the candidate).
                await world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET CredentialId = N'changed-behind' WHERE UserId = N'{world.UserId.Value}';");
                request = world.Request(candidate.CandidateId, version);
                expected = NativeMfaCandidateOutcome.ActiveEnrollmentChanged;
                break;
            }
            case Refusal.NoActiveEnrollment:
            {
                var (candidate, version) = await world.StageAndVerifyAsync();
                await world.Scratch.ExecuteAsync($"DELETE FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';");
                request = world.Request(candidate.CandidateId, version);
                expected = NativeMfaCandidateOutcome.NoActiveEnrollment;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        var affected = refusal == Refusal.OtherUsersCandidate ? request.UserId : world.UserId;
        var before = beforeOverride ?? await world.ActiveAsync(affected);
        var candidatesBefore = (await world.CandidateRowsAsync()).Select(r => $"{r["CandidateId"]}/{r["Status"]}/{r["Version"]}").ToList();

        var result = await world.SwitchAsync(request);

        result.Outcome.Should().Be(expected);
        result.NewCredentialId.Should().BeNull();
        (await world.ActiveAsync(affected)).Should().Be(before, "a refused switch changes neither the secret, its identity, its step nor the recovery codes");
        (await world.CandidateRowsAsync()).Select(r => $"{r["CandidateId"]}/{r["Status"]}/{r["Version"]}").Should().Equal(candidatesBefore, "a refused switch consumes nothing");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_consumed_candidate_cannot_be_switched_again_and_the_second_attempt_changes_nothing(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var firstRequest = world.Request(candidate.CandidateId, version, "first");
        (await world.SwitchAsync(firstRequest)).IsApplied.Should().BeTrue();
        var afterFirst = await world.ActiveAsync();

        var again = await world.SwitchAsync(world.Request(candidate.CandidateId, version, "second"));
        var verifyAgain = await world.VerifyAsync(candidate.CandidateId, version);

        again.Outcome.Should().Be(NativeMfaCandidateOutcome.Consumed);
        verifyAgain.Outcome.Should().Be(NativeMfaCandidateOutcome.Consumed);
        (await world.ActiveAsync()).Should().Be(afterFirst, "the replayed switch did not replace the codes the first one issued");
        (await world.CodeIdsAsync()).Should().Be(string.Join(",", firstRequest.RecoveryCodes.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_switch_refuses_recovery_codes_of_another_user_or_none_before_touching_anything(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var (candidate, version) = await world.StageAndVerifyAsync();
        var active = await world.ActiveAsync();

        var foreign = world.Request(candidate.CandidateId, version) with { RecoveryCodes = world.Request(candidate.CandidateId, version, user: other).RecoveryCodes };
        var empty = world.Request(candidate.CandidateId, version) with { RecoveryCodes = [] };

        (await ((Func<Task>)(() => world.SwitchAsync(foreign))).Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("belongs to the user");
        await ((Func<Task>)(() => world.SwitchAsync(empty))).Should().ThrowAsync<ArgumentException>();

        (await world.ActiveAsync()).Should().Be(active);
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Verified);
    }

    // ---- Joining the caller's unit ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_inside_the_audited_unit_commits_with_its_audit_row_and_rolls_back_with_the_unit(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var active = await world.ActiveAsync();
        var request = world.Request(candidate.CandidateId, version);

        // The unit fails after the switch: the switch's writes go with it, and the candidate is again exactly as it was.
        await using (var db = world.Scratch.NewDb())
        {
            var repository = new SqlNativeMfaReplacementCandidateRepository(db);
            var failing = async () => await NewUnit(db, null).ExecuteAsync<bool>(
                new AuditedOperation(AuditActionNames.MfaReplaced, AuditActor.EmergencyCli),
                async (transaction, ct) =>
                {
                    (await repository.SwitchAsync(request, ct)).IsApplied.Should().BeTrue();
                    await transaction.StageAsync(AuditEvents.MfaReplaced(world.UserId, ConfigurationEvidence.Attested), ct);
                    throw new InvalidOperationException("later step failed");
                },
                (_, _) => Task.FromResult(false),
                CancellationToken.None);
            await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("later step failed");
        }

        (await world.ActiveAsync()).Should().Be(active, "the failed unit left the active authenticator and codes as they were");
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Verified);
        (await world.AuditRowsAsync(AuditActionNames.MfaReplaced)).Should().Be(0);

        // The same request then commits with its audit row.
        await using (var db = world.Scratch.NewDb())
        {
            var repository = new SqlNativeMfaReplacementCandidateRepository(db);
            var result = await NewUnit(db, null).ExecuteAsync(
                new AuditedOperation(AuditActionNames.MfaReplaced, AuditActor.EmergencyCli),
                async (transaction, ct) =>
                {
                    var switched = await repository.SwitchAsync(request, ct);
                    await transaction.StageAsync(AuditEvents.MfaReplaced(world.UserId, ConfigurationEvidence.Attested), ct);
                    return switched;
                },
                (_, _) => Task.FromResult(new NativeMfaSwitchResult(NativeMfaCandidateOutcome.Applied, null)),
                CancellationToken.None);
            result.IsApplied.Should().BeTrue();
        }

        (await world.AuditRowsAsync(AuditActionNames.MfaReplaced)).Should().Be(1);
        (await world.ActiveAsync()).Should().NotBe(active);
        var details = (await world.World.AuditRowDetailsAsync(AuditActionNames.MfaReplaced, world.UserId.Value)).Single();
        details["Outcome"].Should().Be("succeeded");
        ((string)details["Details"]!).Should().Contain("\"configurationEvidence\":\"attested\"").And.Contain(world.UserId.Value);
        ((string)details["Details"]!).Should().NotContain(request.CandidateId, "the candidate is not recorded");
    }

    // ---- Invalidation by the other writers of the credential set ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_the_authenticator_or_replacing_the_recovery_codes_removes_the_staged_candidate(bool rcsi)
    {
        var world = await WorldAsync(rcsi);

        // Regenerating the recovery codes: the codes a candidate would replace are no longer the ones the operator saw.
        var (first, firstVersion) = await world.StageAndVerifyAsync();
        await using (var db = world.Scratch.NewDb())
            await world.Mfa(db).RegenerateRecoveryCodesAsync(world.UserId, AuditActor.EmergencyCli);
        (await world.CandidateRowCountAsync()).Should().Be(0);
        (await world.SwitchAsync(world.Request(first.CandidateId, firstVersion))).Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);

        // A reset removes the authenticator, its codes and the candidate staged against it.
        var (second, secondVersion) = await world.StageAndVerifyAsync();
        await using (var db = world.Scratch.NewDb())
            await world.Mfa(db).ResetAsync(world.UserId, AuditActor.EmergencyCli);
        (await world.CandidateRowCountAsync()).Should().Be(0);
        (await world.SwitchAsync(world.Request(second.CandidateId, secondVersion))).Outcome.Should().Be(NativeMfaCandidateOutcome.NoActiveEnrollment);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Discarding_removes_every_candidate_of_the_user_and_nothing_else(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        await world.StageAsync(user: other);
        await world.StageAsync();
        await world.StageAsync();   // one live and one superseded
        var active = await world.ActiveAsync();

        int removed;
        await using (var db = world.Scratch.NewDb())
            removed = await new SqlNativeMfaReplacementCandidateRepository(db).DiscardAsync(world.UserId);

        removed.Should().Be(2);
        (await world.CandidateRowCountAsync()).Should().Be(0);
        (await world.CandidateRowCountAsync(other)).Should().Be(1, "another user's candidate stays");
        (await world.ActiveAsync()).Should().Be(active);
    }
}
