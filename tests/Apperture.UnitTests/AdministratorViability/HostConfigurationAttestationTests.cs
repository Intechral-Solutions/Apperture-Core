using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The Management Host's configuration attestation as the emergency command line reads it: what a fresh reading is, every way
/// evidence can fail to be one (missing, stale, stopped, for another database, unknown, unreadable, in conflict, future-dated), the
/// exact edge of the freshness window, what is and is never written, and what the evaluation then does with it. Time is a value in
/// every test: the classifier reads no clock, so no test sleeps.
/// </summary>
public sealed class HostConfigurationAttestationTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = HostConfigurationAttestationTiming.FreshnessWindow;
    private const string Fingerprint = "DB-A";

    private static AttestedEntraOptions UsableEntra() =>
        new(true, "https://login.microsoftonline.com/" + Tenant, "client", HasClientSecret: true, Tenant, "/signin-oidc", ["openid", "profile"], null, false);

    private static AttestedAuthenticationPayload Established(
        bool localNow = true,
        bool localRestart = true,
        EntraOptionsBuildQuality? quality = EntraOptionsBuildQuality.Complete,
        RunningHandlerState handler = RunningHandlerState.Bound) =>
        new(
            localNow,
            localRestart,
            UsableEntra(),
            new AttestedEntraNow(
                UsableEntra(),
                quality,
                new AttestedHandler(handler, "https://login.microsoftonline.com/" + Tenant + "/v2.0", "client", true, quality)));

    private static HostConfigurationAttestationRow Row(
        string host = "host-1",
        TimeSpan? age = null,
        DateTime? stopped = null,
        string fingerprint = Fingerprint,
        int version = AttestedAuthenticationPayload.SchemaVersion,
        string source = HostConfigurationAttestationSource.ManagementHost,
        string? payload = null) =>
        new(
            host,
            source,
            version,
            "process-" + host,
            new DateTimeOffset(Now.AddHours(-1), TimeSpan.Zero),
            Now.AddHours(-1),
            Now - (age ?? TimeSpan.FromSeconds(10)),
            stopped,
            fingerprint,
            payload ?? Established().Serialize());

    private static HostConfigurationAttestationSnapshot Snapshot(params HostConfigurationAttestationRow[] rows) =>
        new(rows, Now, Fingerprint);

    private static AttestationAssessment Assess(params HostConfigurationAttestationRow[] rows) =>
        HostConfigurationAttestationClassifier.Assess(Snapshot(rows), Window);

    // ---------------------------------------------------------------- the contract's numbers

    [Fact]
    public void The_refresh_interval_is_sixty_seconds_and_the_window_is_three_intervals()
    {
        HostConfigurationAttestationTiming.RefreshInterval.Should().Be(TimeSpan.FromSeconds(60));
        HostConfigurationAttestationTiming.FreshnessWindow.Should().Be(TimeSpan.FromSeconds(180));
        HostConfigurationAttestationTiming.FreshnessWindow.Should().Be(HostConfigurationAttestationTiming.RefreshInterval * 3);
    }

    // ---------------------------------------------------------------- freshness, to the tick

    [Fact]
    public void A_reading_refreshed_just_now_is_attested()
    {
        var result = Assess(Row(age: TimeSpan.Zero));

        result.Kind.Should().Be(AttestationAssessmentKind.Fresh);
        result.IsAttested.Should().BeTrue();
        result.Evidence.Should().Be(ConfigurationEvidence.Attested);
        result.Reading.Should().NotBeNull();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void A_reading_exactly_as_old_as_the_window_is_still_fresh()
    {
        Assess(Row(age: Window)).Kind.Should().Be(AttestationAssessmentKind.Fresh);
    }

    [Fact]
    public void A_reading_one_tick_inside_the_window_is_fresh()
    {
        Assess(Row(age: Window - TimeSpan.FromTicks(1))).Kind.Should().Be(AttestationAssessmentKind.Fresh);
    }

    [Fact]
    public void A_reading_one_tick_older_than_the_window_is_stale()
    {
        var result = Assess(Row(age: Window + TimeSpan.FromTicks(1)));

        result.Kind.Should().Be(AttestationAssessmentKind.Stale);
        result.Reading.Should().BeNull();
        result.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
    }

    [Theory]
    [InlineData(181)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void An_old_reading_is_stale_however_old(int seconds)
    {
        Assess(Row(age: TimeSpan.FromSeconds(seconds))).Kind.Should().Be(AttestationAssessmentKind.Stale);
    }

    [Fact]
    public void The_window_is_a_parameter_of_the_judgement_not_of_the_row()
    {
        var row = Row(age: TimeSpan.FromSeconds(100));
        HostConfigurationAttestationClassifier.Assess(Snapshot(row), TimeSpan.FromSeconds(100)).IsAttested.Should().BeTrue();
        HostConfigurationAttestationClassifier.Assess(Snapshot(row), TimeSpan.FromSeconds(100) - TimeSpan.FromTicks(1)).IsAttested.Should().BeFalse();
    }

    [Fact]
    public void A_reading_dated_after_the_database_clock_is_never_believed()
    {
        var result = Assess(Row(age: -TimeSpan.FromTicks(1)));

        result.Kind.Should().Be(AttestationAssessmentKind.ClockInconsistent);
        result.IsAttested.Should().BeFalse();
    }

    [Fact]
    public void A_future_dated_row_is_not_believed_even_beside_a_good_one()
    {
        Assess(Row("host-1"), Row("host-2", age: -TimeSpan.FromMinutes(5))).Kind.Should().Be(AttestationAssessmentKind.ClockInconsistent);
    }

    // ---------------------------------------------------------------- absent, stopped, copied, unknown, unreadable

    [Fact]
    public void No_attestation_is_missing_evidence_not_a_configuration()
    {
        var result = Assess();

        result.Kind.Should().Be(AttestationAssessmentKind.Missing);
        result.Reading.Should().BeNull();
        result.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
    }

    [Fact]
    public void A_graceful_stop_closes_the_window_at_once()
    {
        var result = Assess(Row(age: TimeSpan.FromSeconds(1), stopped: Now.AddSeconds(-1)));

        result.Kind.Should().Be(AttestationAssessmentKind.Stopped);
        result.Reading.Should().BeNull();
    }

    [Fact]
    public void A_stopped_row_is_not_resurrected_by_being_fresh()
    {
        Assess(Row(age: TimeSpan.Zero, stopped: Now)).IsAttested.Should().BeFalse();
    }

    [Fact]
    public void A_crash_leaves_the_window_open_for_at_most_the_window()
    {
        // The process died without a stop mark: the row stays believable until it ages out, and no longer.
        Assess(Row(age: Window)).IsAttested.Should().BeTrue();
        Assess(Row(age: Window + TimeSpan.FromTicks(1))).IsAttested.Should().BeFalse();
    }

    [Fact]
    public void A_reading_written_for_another_database_is_not_evidence_about_this_one()
    {
        var result = Assess(Row(fingerprint: "DB-OTHER"));

        result.Kind.Should().Be(AttestationAssessmentKind.DatabaseMismatch);
        result.Reading.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void A_schema_version_the_reader_does_not_know_is_not_read(int version)
    {
        Assess(Row(version: version)).Kind.Should().Be(AttestationAssessmentKind.UnknownSchema);
    }

    [Fact]
    public void A_source_the_reader_does_not_know_is_not_read()
    {
        Assess(Row(source: "someone_else")).Kind.Should().Be(AttestationAssessmentKind.UnknownSchema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"localNow\":true,\"localRestart\":true}")]
    public void A_document_that_is_not_the_known_shape_is_unreadable(string payload)
    {
        Assess(Row(payload: payload)).Kind.Should().Be(AttestationAssessmentKind.Unreadable);
    }

    [Fact]
    public void A_document_with_a_member_the_reader_does_not_know_is_unreadable()
    {
        var json = Established().Serialize().Replace("\"localNow\"", "\"somethingNew\":1,\"localNow\"", StringComparison.Ordinal);
        Assess(Row(payload: json)).Kind.Should().Be(AttestationAssessmentKind.Unreadable);
    }

    [Fact]
    public void A_document_with_an_unknown_state_name_or_a_numeric_state_is_unreadable()
    {
        var json = Established().Serialize();
        Assess(Row(payload: json.Replace("\"bound\"", "\"usable\"", StringComparison.Ordinal))).Kind.Should().Be(AttestationAssessmentKind.Unreadable);
        Assess(Row(payload: json.Replace("\"bound\"", "2", StringComparison.Ordinal))).Kind.Should().Be(AttestationAssessmentKind.Unreadable);
    }

    [Fact]
    public void A_document_larger_than_the_bound_is_unreadable()
    {
        var padding = new string(' ', AttestedAuthenticationPayload.MaxLength);
        Assess(Row(payload: Established().Serialize() + padding)).Kind.Should().Be(AttestationAssessmentKind.Unreadable);
    }

    [Fact]
    public void A_document_with_a_missing_part_is_unreadable_rather_than_defaulted()
    {
        var json = "{\"localNow\":true,\"localRestart\":true,\"entraBase\":{\"enabled\":true,\"authority\":\"a\",\"clientId\":\"c\",\"hasClientSecret\":true,\"callbackPath\":\"/x\"},\"entraNow\":null}";
        AttestedAuthenticationPayload.TryParse(json, out var payload).Should().BeFalse("scopes are absent, and absent is not an empty list");
        payload.Should().BeNull();
    }

    // ---------------------------------------------------------------- one host

    [Fact]
    public void Two_live_hosts_are_a_conflict_not_a_merge()
    {
        var result = Assess(Row("host-1"), Row("host-2", age: TimeSpan.FromSeconds(30)));

        result.Kind.Should().Be(AttestationAssessmentKind.Conflict);
        result.Reading.Should().BeNull();
    }

    [Fact]
    public void Two_live_hosts_that_agree_are_still_a_conflict()
    {
        Assess(Row("host-1"), Row("host-2")).IsAttested.Should().BeFalse("RC1 supports one Management Host; agreement is not the test");
    }

    [Fact]
    public void A_stale_row_of_another_host_does_not_conflict_with_the_live_one()
    {
        var result = Assess(Row("old-host", age: Window + TimeSpan.FromSeconds(1)), Row("host-1"));

        result.Kind.Should().Be(AttestationAssessmentKind.Fresh);
    }

    [Fact]
    public void A_stopped_row_of_another_host_does_not_conflict_with_the_live_one()
    {
        var result = Assess(Row("old-host", age: TimeSpan.FromSeconds(1), stopped: Now), Row("host-1"));

        result.Kind.Should().Be(AttestationAssessmentKind.Fresh);
    }

    [Fact]
    public void A_different_host_id_is_a_conflict_until_the_old_row_expires()
    {
        // The single host moved: the old row is still inside its window, the new one is live.
        Assess(Row("old-host", age: Window), Row("new-host", age: TimeSpan.Zero)).Kind.Should().Be(AttestationAssessmentKind.Conflict);
        Assess(Row("old-host", age: Window + TimeSpan.FromTicks(1)), Row("new-host", age: TimeSpan.Zero)).Kind.Should().Be(AttestationAssessmentKind.Fresh);
    }

    [Fact]
    public void An_unreadable_live_row_beside_a_readable_one_is_a_conflict()
    {
        Assess(Row("host-1"), Row("host-2", payload: "garbage")).Kind.Should().Be(AttestationAssessmentKind.Conflict);
    }

    [Fact]
    public void The_stopped_or_stale_verdict_follows_the_newest_record()
    {
        Assess(Row("a", age: TimeSpan.FromMinutes(10)), Row("b", age: TimeSpan.FromMinutes(5), stopped: Now.AddMinutes(-5)))
            .Kind.Should().Be(AttestationAssessmentKind.Stopped);
        Assess(Row("a", age: TimeSpan.FromMinutes(5)), Row("b", age: TimeSpan.FromMinutes(10), stopped: Now.AddMinutes(-10)))
            .Kind.Should().Be(AttestationAssessmentKind.Stale);
    }

    // ---------------------------------------------------------------- the reason an operator reads

    [Fact]
    public void Every_unavailable_reason_is_product_wording_that_names_no_host_database_or_internal_label()
    {
        var all = new[]
        {
            Assess(),
            Assess(Row(age: Window + TimeSpan.FromSeconds(1))),
            Assess(Row(stopped: Now)),
            Assess(Row(fingerprint: "DB-OTHER")),
            Assess(Row(version: 9)),
            Assess(Row(payload: "x")),
            Assess(Row("a"), Row("b")),
            Assess(Row(age: -TimeSpan.FromSeconds(1))),
        };

        all.Select(a => a.Kind).Distinct().Should().HaveCount(all.Length, "each failure is told apart");
        foreach (var result in all)
        {
            result.Reason.Should().NotBeNullOrWhiteSpace();
            result.Reason.Should().NotContain("host-1").And.NotContain("DB-A").And.NotContain("DB-OTHER").And.NotContain(Tenant);
            result.Reason.Should().NotMatchRegex("WP4B|IAM-|D-6|R-1|ruling", "operator wording carries no planning labels");
        }
    }

    // ---------------------------------------------------------------- what is attested

    [Fact]
    public void The_serialised_document_cannot_carry_a_secret_value()
    {
        var options = new EntraAuthOptions
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/" + Tenant,
            ClientId = "client",
            ClientSecret = "super-secret-value-123",
        };

        var json = AttestedAuthenticationPayload.NotEstablished(true, true, options).Serialize();

        json.Should().NotContain("super-secret-value-123");
        json.Should().Contain("\"hasClientSecret\":true");
        typeof(AttestedEntraOptions).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Secret") && n != "HasClientSecret");
    }

    [Fact]
    public void A_secret_the_host_holds_is_rebuilt_as_a_marker_and_an_absent_one_stays_absent()
    {
        var held = UsableEntra().ToOptions();
        held.ClientSecret.Should().Be(AttestedEntraOptions.SecretPresentMarker);
        EntraConfigurationState.IsUsable(held).Should().BeTrue();

        var absent = (UsableEntra() with { HasClientSecret = false }).ToOptions();
        absent.ClientSecret.Should().BeEmpty();
        EntraConfigurationState.IsUsable(absent).Should().BeFalse();
    }

    [Fact]
    public void An_established_payload_round_trips_into_the_reading_the_host_made()
    {
        var running = new RunningAuthenticationReading(
            new LocalAuthOptions { Enabled = true },
            new EntraAuthOptions { Enabled = true, Authority = "https://login.microsoftonline.com/" + Tenant, ClientId = "client", ClientSecret = "s", AzureTenantId = Tenant },
            RunningHandlerBinding.Bound("https://login.microsoftonline.com/" + Tenant + "/v2.0", "client", true),
            EntraOptionsBuildQuality.Degraded);
        var reading = new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], false, new EntraAuthOptions { ClientSecret = "x" }, true, running);

        var payload = AttestedAuthenticationPayload.Established(reading);
        AttestedAuthenticationPayload.TryParse(payload.Serialize(), out var parsed).Should().BeTrue();
        var rebuilt = parsed!.ToReading();

        rebuilt.LocalSignInEnabled.Should().BeFalse("the restart side is what the host's configuration says");
        rebuilt.Running!.Local.Enabled.Should().BeTrue("the running side is what the host's options say");
        rebuilt.Running.EndpointsQuality.Should().Be(EntraOptionsBuildQuality.Degraded);
        rebuilt.Running.Handler.State.Should().Be(RunningHandlerState.Bound);
        rebuilt.Running.Handler.IsUsable.Should().BeTrue();
        rebuilt.Evidence.Should().Be(ConfigurationEvidence.Attested);
        rebuilt.AuthoritativeAboutSecrets.Should().BeFalse("the command line cannot see the host's secrets");
        rebuilt.LocalTrust.Should().Be(ConfigurationTrustLevel.Trusted);
    }

    [Fact]
    public void A_host_whose_options_are_not_established_reports_the_handler_unavailable_never_usable()
    {
        var reading = AttestedAuthenticationPayload.NotEstablished(true, true, new EntraAuthOptions { Enabled = true }).ToReading();

        reading.Running!.Handler.State.Should().Be(RunningHandlerState.Unavailable);
        reading.Running.Handler.IsUsable.Should().BeFalse();
        reading.Running.Local.Enabled.Should().BeTrue("Local is a deployment fact the host can state without building anything");
    }

    // ---------------------------------------------------------------- the reader

    private sealed class FakeStore(HostConfigurationAttestationSnapshot snapshot) : IHostConfigurationAttestationStore
    {
        public int Reads { get; private set; }

        public ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<HostAttestationRefreshOutcome> RefreshAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(snapshot);
        }
    }

    [Fact]
    public async Task The_command_line_reader_returns_the_attested_reading_for_a_fresh_row()
    {
        var reader = new AttestedDeploymentAuthenticationConfiguration(new FakeStore(Snapshot(Row())));

        var reading = await reader.ReadAsync();

        reading.Evidence.Should().Be(ConfigurationEvidence.Attested);
        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Trusted);
        reading.Running.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(UnusableSnapshots))]
    public async Task The_command_line_reader_trusts_neither_input_when_the_evidence_is_unusable(HostConfigurationAttestationSnapshot snapshot)
    {
        var reader = new AttestedDeploymentAuthenticationConfiguration(new FakeStore(snapshot));

        var reading = await reader.ReadAsync();

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Unknown);
        reading.EntraTrust.Should().Be(ConfigurationTrustLevel.Unknown);
        reading.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        reading.Reasons.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        reading.Running.Should().BeNull();
        reading.LocalSignInEnabled.Should().BeFalse("an unavailable reading never reads as Local sign-in being on");
    }

    public static IEnumerable<object[]> UnusableSnapshots()
    {
        yield return [Snapshot()];
        yield return [Snapshot(Row(age: Window + TimeSpan.FromTicks(1)))];
        yield return [Snapshot(Row(stopped: Now))];
        yield return [Snapshot(Row(fingerprint: "other"))];
        yield return [Snapshot(Row(payload: "{"))];
        yield return [Snapshot(Row("a"), Row("b"))];
    }

    [Fact]
    public void A_synchronous_read_fails_closed_instead_of_blocking_on_the_database()
    {
        var store = new FakeStore(Snapshot(Row()));
        var reading = new AttestedDeploymentAuthenticationConfiguration(store).Read();

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Unknown);
        reading.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        store.Reads.Should().Be(0);
    }

    [Fact]
    public async Task The_reader_never_falls_back_to_the_command_lines_own_configuration()
    {
        // It takes the store and nothing else: there is no configuration, no override and no assertion to fall back to.
        var constructors = typeof(AttestedDeploymentAuthenticationConfiguration).GetConstructors();
        constructors.Should().ContainSingle();
        constructors[0].GetParameters().Select(p => p.ParameterType)
            .Should().BeEquivalentTo([typeof(IHostConfigurationAttestationStore), typeof(TimeSpan?)]);

        var unavailable = await new AttestedDeploymentAuthenticationConfiguration(new FakeStore(Snapshot())).ReadAsync();
        unavailable.LocalTrust.Should().Be(ConfigurationTrustLevel.Unknown);
    }

    // ---------------------------------------------------------------- the evaluation over an attested reading

    private static async Task<EffectiveSignInConfiguration> EvaluateAsync(HostConfigurationAttestationSnapshot snapshot, EntraInstanceRegistration? active = null)
    {
        var registrations = new Mock<IEntraInstanceRegistrationRepository>();
        registrations.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        var provider = new EffectiveSignInConfigurationProvider(
            new AttestedDeploymentAuthenticationConfiguration(new FakeStore(snapshot)),
            registrations.Object);
        return await provider.ReadAsync();
    }

    private static EntraInstanceRegistration Registration() => new()
    {
        Id = "reg",
        Enabled = true,
        IsActive = true,
        Authority = "https://login.microsoftonline.com/" + Tenant,
        ClientId = "client",
        UsesDeploymentSecret = true,
        AzureTenantId = Tenant,
    };

    [Fact]
    public async Task A_fresh_attestation_of_a_working_host_gives_a_trusted_local_reading_and_carries_its_evidence_class()
    {
        var result = await EvaluateAsync(Snapshot(Row()), Registration());

        result.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
        result.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested);
        result.Now.LocalSignInEnabled.Should().BeTrue();
        result.Now.EntraSignInUsable.Should().BeTrue();
        result.Restart.LocalSignInEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_host_that_reports_local_off_gives_local_off_in_each_view_separately()
    {
        var payload = Established(localNow: false, localRestart: true).Serialize();

        var result = await EvaluateAsync(Snapshot(Row(payload: payload)), Registration());

        result.Now.LocalSignInEnabled.Should().BeFalse();
        result.Restart.LocalSignInEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_startup_attestation_is_trusted_for_local_and_unknown_for_entra()
    {
        var payload = AttestedAuthenticationPayload.NotEstablished(true, true, new EntraAuthOptions { Enabled = true, Authority = "https://login.microsoftonline.com/" + Tenant, ClientId = "client", ClientSecret = "s", AzureTenantId = Tenant }).Serialize();

        var result = await EvaluateAsync(Snapshot(Row(payload: payload)), Registration());

        result.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
        result.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown, "the process starting does not make Microsoft sign-in initialised");
        result.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested);
        result.Now.EntraSignInUsable.Should().BeFalse("an unestablished handler is never read as usable");
    }

    [Fact]
    public async Task A_degraded_build_is_unknown_for_entra_whatever_the_flag_says()
    {
        var payload = Established(quality: EntraOptionsBuildQuality.Degraded).Serialize();

        var result = await EvaluateAsync(Snapshot(Row(payload: payload)), Registration());

        result.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        result.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
    }

    [Fact]
    public async Task A_configuration_flag_that_says_enabled_is_not_a_usable_handler()
    {
        var payload = Established(handler: RunningHandlerState.Disabled).Serialize();

        var result = await EvaluateAsync(Snapshot(Row(payload: payload)), Registration());

        result.Now.EntraSignInUsable.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UnusableSnapshots))]
    public async Task Unusable_evidence_makes_both_inputs_unknown_so_a_dependent_decision_fails_closed(HostConfigurationAttestationSnapshot snapshot)
    {
        var result = await EvaluateAsync(snapshot, Registration());

        result.Trust.Local.Should().Be(ConfigurationTrustLevel.Unknown);
        result.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        result.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        result.Trust.Reasons.Should().NotBeEmpty();
    }

    [Fact]
    public void An_unusable_attestation_refuses_a_decision_that_changes_what_the_configuration_decides()
    {
        // Local administrator A (the only one) loses their credential: the inputs changed, so the full evaluation runs, and with no
        // trusted configuration it refuses instead of guessing.
        var unknown = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["no evidence"], ConfigurationEvidence.Unavailable);
        var pre = Same(["a"], ["a"], trust: unknown);
        var post = Same([], [], trust: unknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void An_unusable_attestation_does_not_block_a_change_that_left_every_input_unchanged()
    {
        var unknown = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["no evidence"], ConfigurationEvidence.Unavailable);
        var state = FromFacts([Admin("a", local: true)], trust: unknown);

        AdministratorViabilityGuard.Evaluate(state, state).IsAllowed.Should().BeTrue("the decision did not depend on the missing input");
    }

    [Fact]
    public void Remediation_from_an_already_unsafe_state_is_allowed_on_an_attested_local_reading_without_entra()
    {
        // A startup attestation: Local is established, Entra is not. The installation has no viable administrator before the change
        // (already unsafe); the change gives it an Entra-independent one. That is proven safe without Entra, so it is allowed.
        var startup = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["entra not established"], ConfigurationEvidence.Attested);
        var pre = FromFacts([], trust: startup);
        var post = FromFacts([Admin("a", local: true)], trust: startup);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Remediation_needs_no_attestation_when_nothing_the_attestation_provides_is_read()
    {
        // With no usable attestation at all, a change that leaves every stored fact as it was is still allowed, and one that changes
        // facts is not allowed on a guess: the unknown configuration is never read as proof.
        var unknown = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["no evidence"], ConfigurationEvidence.Unavailable);
        var unsafeState = FromFacts([], trust: unknown);
        var raised = FromFacts([Admin("a", local: true)], trust: unknown);

        AdministratorViabilityGuard.Evaluate(unsafeState, unsafeState).IsAllowed.Should().BeTrue();
        AdministratorViabilityGuard.Evaluate(unsafeState, raised).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void Unusable_evidence_never_proves_a_revocation_safe()
    {
        var unknown = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["no evidence"], ConfigurationEvidence.Unavailable);
        var post = Same(["a", "b"], ["a", "b"], trust: unknown);

        AdministratorViabilityGuard.EvaluateRevocation(post, "revoked").Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_stale_attestation_cannot_be_turned_into_a_proof_by_a_local_flag()
    {
        // Local trusted but Entra unknown: the full evaluation needs Entra, so only the Entra-independent proof can allow it.
        var half = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["entra"], ConfigurationEvidence.Attested);
        var pre = Same(["a", "b"], ["a"], trust: half);
        var post = Same(["b"], [], trust: half);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    // ---------------------------------------------------------------- the audit class

    [Fact]
    public void The_evidence_classes_have_exactly_the_canonical_audit_values()
    {
        ConfigurationEvidence.Attested.ToAuditValue().Should().Be("attested");
        ConfigurationEvidence.NotRequired.ToAuditValue().Should().Be("not_required");
        ConfigurationEvidence.Unavailable.ToAuditValue().Should().Be("unavailable");
        ConfigurationEvidenceNames.All.Should().Equal("attested", "not_required", "unavailable");
        ConfigurationEvidenceNames.AuditDetailName.Should().Be("configurationEvidence");
        Enum.GetValues<ConfigurationEvidence>().Should().HaveCount(3);
    }

    [Fact]
    public void Designate_revoke_and_reset_events_carry_only_the_class_and_validate_against_the_catalog()
    {
        var user = new UserId("u1");
        foreach (var (action, build) in new (string, Func<ConfigurationEvidence?, AuditEvent>)[]
        {
            (AuditActionNames.RecoveryAccessDesignated, e => AuditEvents.RecoveryAccessDesignated(user, null, e)),
            (AuditActionNames.RecoveryAccessRevoked, e => AuditEvents.RecoveryAccessRevoked(user, e)),
            (AuditActionNames.MfaReset, e => AuditEvents.MfaReset(user, e)),
        })
        {
            var schema = AuditActionCatalog.Default.Get(action).Details!;
            foreach (var evidence in Enum.GetValues<ConfigurationEvidence>())
            {
                var evt = build(evidence);
                evt.Fields["configurationEvidence"].Should().Be(evidence.ToAuditValue());
                var act = () => schema.Validate(action, evt.Fields);
                act.Should().NotThrow(action + " / " + evidence);
            }

            build(null).Fields.GetValueOrDefault("configurationEvidence").Should().BeNull("an operation that does not record the class leaves the detail out");
            var bad = new Dictionary<string, object?>(build(ConfigurationEvidence.Attested).Fields) { ["configurationEvidence"] = "trusted" };
            var reject = () => schema.Validate(action, bad);
            reject.Should().Throw<ArgumentException>(action + " accepts only the three canonical values");
        }
    }

    [Fact]
    public void An_event_with_the_class_names_no_host_fingerprint_tenant_or_option_value()
    {
        var fields = AuditEvents.RecoveryAccessRevoked(new UserId("u1"), ConfigurationEvidence.Attested).Fields;

        fields.Keys.Should().BeEquivalentTo("userId", "configurationEvidence");
    }
}
