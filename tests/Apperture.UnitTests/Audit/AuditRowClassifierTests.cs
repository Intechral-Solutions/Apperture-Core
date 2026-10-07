using System.Text.RegularExpressions;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// Classification of rows the recorder did not write (audit hardening design gate §11.4, §11.5, §12.6, §15.2, §15.3;
/// A1.1-7, A1.1-20), the actor contract (§10.8; A1.1-5) and the read projection (§10.7).
/// </summary>
public sealed class AuditRowClassifierTests
{
    private const string Pat = "5a7000000000000000000001";
    private const string Erin = "e1110000000000000000000e";
    private const string UnknownUser = "0000000000000000000000ff";
    private const string ErinOid = "3f1d2c4b-1111-4a5b-9c8d-000000000001";

    private static readonly HistoricalActorClassifier Actors = new(
        [Pat, Erin, UnknownUser],
        [("pat.lee", Pat), ("pat.old", Pat), ("unknown", UnknownUser)],
        [(ErinOid, Erin)]);

    private static AuditClassificationSet Classify(string actor, string action, string? outcome, string? details = null, string? producer = null) =>
        AuditRowClassifier.Classify(new AuditLegacyRow(actor, action, outcome, details, producer), Actors, AuditActionCatalog.Default);

    // ---- A1.1-7: the classifier reports its rule; IAM-9.2a outcomes are unchanged ----

    [Theory]
    [InlineData("system:scheduler", HistoricalActorRule.Literal)]
    [InlineData(Pat, HistoricalActorRule.UserId)]
    [InlineData("pat.old", HistoricalActorRule.Username)]
    [InlineData(ErinOid, HistoricalActorRule.EntraObjectId)]
    [InlineData("nobody", HistoricalActorRule.None)]
    public void The_shared_classifier_reports_which_rule_decided_without_changing_its_result(string value, HistoricalActorRule rule)
    {
        var report = Actors.ClassifyReportingRule(value);

        report.Rule.Should().Be(rule);
        report.Classification.Should().Be(Actors.Classify(value), "the report is additive; the IAM-9.2a classification is unchanged");
    }

    // ---- §15.2 actor rules ----

    [Fact]
    public void Actor_rules_set_kind_user_id_recorded_sign_in_name_and_provenance()
    {
        Classify(Pat, "user.created", "auth-admin").Should().Match<AuditClassificationSet>(s =>
            s.ActorKind == "user" && s.ActorUserId == Pat && s.ActorUsername == null && s.ActorAttribution == "recorded_user_id");
        Classify("pat.old", "user.created", "auth-admin").Should().Match<AuditClassificationSet>(s =>
            s.ActorKind == "user" && s.ActorUserId == Pat && s.ActorUsername == "pat.old" && s.ActorAttribution == "inferred_username");
        Classify(ErinOid, "user.created", "auth-admin").Should().Match<AuditClassificationSet>(s =>
            s.ActorKind == "user" && s.ActorUserId == Erin && s.ActorUsername == null && s.ActorAttribution == "inferred_entra_object_id");
        Classify("system-worker", "user.created", "auth-admin").Should().Match<AuditClassificationSet>(s =>
            s.ActorKind == "worker" && s.ActorUserId == null && s.ActorAttribution == "recorded_literal");
        Classify("anonymous", "auth.local.login.failed", "auth").ActorKind.Should().Be("anonymous");
        Classify("emergency-cli", "emergency.access.revoked", "auth").ActorKind.Should().Be("system");
        Classify("someone.else", "user.created", "auth-admin").Should().Match<AuditClassificationSet>(s =>
            s.ActorKind == "unresolved" && s.ActorUserId == null && s.ActorAttribution == "unresolved");
    }

    [Fact]
    public void The_unknown_placeholder_is_never_credited_to_a_user_named_unknown()
    {
        Actors.Classify("unknown").Category.Should().Be(HistoricalActorCategory.Resolved, "the shared classifier is unchanged");

        var set = Classify("unknown", "execution.operation_item.bulk_retry.requested", "success");

        set.ActorKind.Should().Be("unresolved");
        set.ActorUserId.Should().BeNull();
        set.ActorAttribution.Should().Be("unresolved");
        set.AttributionIssue.Should().BeNull("the placeholder notice was removed by I-6: that fallback was never reachable");
    }

    [Fact]
    public void Historical_rows_are_never_classified_external()
    {
        Classify("entra/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222", "auth.entra.login.success", "auth")
            .Should().Match<AuditClassificationSet>(s => s.ActorKind == "unresolved" && s.ActorExternalProvider == null);
    }

    // ---- A1.1-20: outcome provenance ----

    [Fact]
    public void A_pre_epic_37_non_era_invariant_action_with_raw_failed_is_indeterminate_and_unknown()
    {
        var set = Classify("system-worker", "execution.workflow_run.failed_definition_unavailable", "failed");

        set.OutcomeProvenance.Should().Be("indeterminate");
        set.OutcomeCode.Should().Be("unknown", "an unknown row never matches outcome=failed");
    }

    [Fact]
    public void The_same_raw_value_on_a_post_epic_37_action_is_recorded_and_failed()
    {
        var set = Classify(Pat, "connection.verified", "failed");

        set.OutcomeProvenance.Should().Be("recorded");
        set.OutcomeCode.Should().Be("failed");
    }

    [Fact]
    public void An_era_invariant_pre_epic_37_action_maps_although_its_provenance_is_indeterminate()
    {
        var set = Classify("system:scheduler", "scheduler.schedule.failed", "failed");

        set.OutcomeProvenance.Should().Be("indeterminate");
        set.OutcomeCode.Should().Be("failed");
    }

    [Fact]
    public void A_row_with_a_producer_is_recorded_whatever_the_action_age()
    {
        var set = Classify("system-worker", "execution.workflow_run.failed_definition_unavailable", "Blocked", producer: AuditProducers.Append);

        set.OutcomeProvenance.Should().Be("recorded");
        set.OutcomeCode.Should().Be("unknown", "Blocked from the definition-unavailable writer is not normalized");
    }

    [Fact]
    public void An_undeclared_legacy_action_asserts_nothing()
    {
        var set = Classify(Pat, "role.assigned", "succeeded");

        set.Should().Match<AuditClassificationSet>(s =>
            s.Category == "other" && s.Severity == "standard" && s.Basis == "unspecified"
            && s.OutcomeCode == "unknown" && s.OutcomeProvenance == "indeterminate" && s.Visibility == "default");
    }

    // ---- §11.4 visibility ----

    [Fact]
    public void Only_a_routine_eligible_action_with_a_routine_outcome_is_routine()
    {
        Classify("system:scheduler", "scheduler.schedule.dispatched", "success").Visibility.Should().Be("routine");
        Classify("system:scheduler", "scheduler.schedule.failed", "failed").Visibility.Should().Be("default");
        Classify("system:scheduler", "scheduler.schedule.blocked_by_lifecycle", "blocked").Visibility.Should().Be("default");
        Classify("system:scheduler", "scheduler.schedule.blocked_by_readiness", "blocked").Visibility.Should().Be("default");
        Classify("system:module-lifecycle", "module.lifecycle.discover", "succeeded").Visibility.Should().Be("routine");
        Classify("system:module-lifecycle", "module.lifecycle.discover", "failed").Visibility.Should().Be("default");
        Classify(Pat, "account.profile.avatar.updated", "account").Visibility.Should().Be("default", "an unknown outcome is never routine");
    }

    // ---- §15.3 defects ----

    [Fact]
    public void Defects_apply_to_non_recorder_rows_by_action_and_recorded_actor()
    {
        Classify(Pat, "account.invitation.sent", "account").Should().Match<AuditClassificationSet>(s =>
            s.AttributionIssue == "subject_recorded_as_actor" && s.AttributionIssueCertainty == "definite");
        Classify("system", "execution.workflow_retry.blocked", "Blocked").AttributionIssue.Should().Be("initiator_not_recorded");
        Classify(Pat, "execution.workflow_retry.blocked", "Blocked").AttributionIssue.Should().BeNull();
        Classify("emergency-cli", "emergency.access.canonicalized", "auth").Should().Match<AuditClassificationSet>(s =>
            s.AttributionIssue == "initiator_possibly_misrecorded" && s.AttributionIssueCertainty == "possible");
        Classify("emergency-cli", "emergency.access.designated", "auth").AttributionIssue.Should().BeNull("removed by I-6");
        Classify("anonymous", "credential.password.reset_requested", "account").AttributionIssue.Should().BeNull();
        Classify(Pat, "credential.password.reset_requested", "account").AttributionIssue.Should().Be("subject_possibly_recorded_as_actor");

        var recorder = () => Classify(Pat, "account.invitation.sent", "account", producer: AuditProducers.Recorder);
        recorder.Should().Throw<ArgumentException>("recorder rows are complete at insert and never classified");
    }

    // ---- §12.6 P-5 ----

    [Theory]
    [InlineData("connection.created", """{"resourceType":"Connection","resourceId":"c1","displayName":"Ledger","connectorDefinitionId":"sql"}""", "connection", "c1")]
    [InlineData("connection.structure_browsed", """{"resourceType":"Connection","resourceId":"c2","displayName":null,"connectorDefinitionId":"sql","tool":"sql-structure","operation":"tables","toolOutcome":"succeeded","resultCount":3}""", "connection", "c2")]
    [InlineData("integration.ownership.moved_out", """{"resourceType":"Integration","resourceId":"i1","displayName":"Payroll","sourceResourceContextId":"a","targetResourceContextId":"b","movedScheduleIds":["s1"]}""", "integration", "i1")]
    [InlineData("schedule.ownership.move_rejected", """{"resourceType":"Schedule","resourceId":"s9","displayName":"Nightly","sourceResourceContextId":"a","targetResourceContextId":"b","blockerCodes":["x"]}""", "schedule", "s9")]
    [InlineData("connection.created", """{"resourceType":"Connection","resourceId":"c1","displayName":"L","connectorDefinitionId":"sql"}; Resource=Connection/instance""", null, null)]
    [InlineData("connection.created", """{"resourceType":"Connection","resourceId":"c1","displayName":"L","connectorDefinitionId":"sql","secret":"x"}""", null, null)]
    [InlineData("connection.created", """{"resourceType":"Integration","resourceId":"c1","displayName":"L","connectorDefinitionId":"sql"}""", null, null)]
    [InlineData("connection.created", "connectionId=c1 name=Ledger", null, null)]
    [InlineData("user.created", """{"resourceType":"Connection","resourceId":"c1","displayName":"L","connectorDefinitionId":"sql"}""", null, null)]
    public void A_legacy_target_comes_only_from_an_exact_typed_json_document(string action, string details, string? type, string? id)
    {
        var set = Classify(Pat, action, "succeeded", details);

        set.TargetType.Should().Be(type);
        set.TargetId.Should().Be(id);
    }

    // ---- §10.8 actor contract, A1.1-5 ----

    [Fact]
    public void Named_non_human_actors_record_only_ActorLiterals_values_with_their_labels_and_kinds()
    {
        var named = new[] { AuditActor.System(), AuditActor.System("scheduler"), AuditActor.Worker, AuditActor.Anonymous, AuditActor.EmergencyCli, AuditActor.RecoveryTool };

        named.Should().OnlyContain(a => ActorLiterals.IsNonHumanLiteral(a.RecordedValue));
        named.Should().OnlyContain(a => ActorLiterals.AuditKindOf(a.RecordedValue) == a.Kind);
        ActorLiterals.LabelOf("system:scheduler").Should().Be("Scheduler");
        ActorLiterals.LabelOf("emergency-cli").Should().Be("Emergency access command line");
        ActorLiterals.LabelOf("recovery").Should().Be("Recovery tool");
        ActorLiterals.LabelOf("system-worker").Should().Be("Worker");
        ActorLiterals.LabelOf("anonymous").Should().Be("Not signed in");
        ActorLiterals.LabelOf("system:something-new").Should().Be("System");
        ActorLiterals.LabelOf("0123456789abcdef01234567").Should().BeNull();
        AuditActor.CurrentPrincipal.IsCurrentPrincipal.Should().BeTrue();
        var invalid = () => AuditActor.System("Bad Subtype");
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_external_actor_is_the_verified_entra_triple_never_an_object_id_alone()
    {
        var external = new AuditExternalActor(AuditExternalActor.EntraProvider, "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Jo");

        AuditActor.ForExternal(external).RecordedValue.Should().Be("entra/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222");
        var noTenant = () => new AuditExternalActor(AuditExternalActor.EntraProvider, "", "22222222-2222-2222-2222-222222222222");
        var email = () => new AuditExternalActor(AuditExternalActor.EntraProvider, "11111111-1111-1111-1111-111111111111", "jo@contoso.com");
        noTenant.Should().Throw<ArgumentException>();
        email.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("src/Apperture.ControlPlane/Apperture.ControlPlane.Security/EmergencyAccessService.cs", "EnsureCanonicalIdentityAsync(AuditActor actor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.ControlPlane.Security/UserManagementService.cs", "RenameUsernameAsync(")]
    public void Converted_paths_pass_no_actor_literal_outside_ActorLiterals(string file, string methodStart)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf(methodStart, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var body = text[start..text.IndexOf("\n    }\n", start, StringComparison.Ordinal)];

        Regex.Matches(body, "\"(system[^\"]*|emergency-cli|recovery|anonymous|unknown|system-worker)\"").Should().BeEmpty();
        if (file.EndsWith("EmergencyAccessService.cs", StringComparison.Ordinal))
            text.Should().Contain("CliActorId = ActorLiterals.EmergencyCli");
    }

    // ---- §10.7 projection ----

    [Fact]
    public void The_read_projection_renders_each_kind_and_degrades_an_unresolvable_user()
    {
        var pat = new UserReferenceDto(Pat, "pat.lee", "Pat Lee");
        var resolved = new Dictionary<string, UserReferenceDto> { [Pat] = pat };

        AuditActorProjection.Project(Stored(Pat, "complete", "user", Pat, "pat.old", "inferred_username"), resolved)
            .Should().Be(new AuditActorDto("user", pat, null, null, Pat, "pat.old", "inferred_username", null));
        AuditActorProjection.Project(Stored("gone", "complete", "user", "gone", null, "recorded_user_id"), resolved).Kind.Should().Be("unresolved");
        AuditActorProjection.Project(Stored("system:scheduler", "complete", "system", null, null, "recorded_literal"), resolved).Label.Should().Be("Scheduler");
        AuditActorProjection.Project(Stored("pat?", "complete", "unresolved", null, null, "ambiguous"), resolved).Label.Should().Be("Unrecognized actor");
        AuditActorProjection.Project(Stored("x", null, null, null, null, null), resolved)
            .Should().Match<AuditActorDto>(d => d.Kind == "unclassified" && d.Attribution == "not_yet_classified" && d.Recorded == "x");
        AuditActorProjection.Project(
                Stored(Pat, "complete", "user", Pat, null, "recorded_user_id") with { AttributionIssue = "subject_recorded_as_actor", AttributionIssueCertainty = "definite" },
                resolved)
            .Issue.Should().Be(new AuditAttributionIssueDto("subject_recorded_as_actor", "definite"));
    }

    private static AuditStoredActor Stored(string actor, string? state, string? kind, string? userId, string? username, string? attribution) =>
        new(actor, state, kind, userId, username, null, null, null, attribution, null, null);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
