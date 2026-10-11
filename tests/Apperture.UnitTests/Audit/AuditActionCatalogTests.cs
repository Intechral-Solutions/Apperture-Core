using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Migrations;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// The action catalog (audit hardening design gate §11.7, §13.1, §11.5, §15.3) and M1 catalog completeness (§21): every
/// action a writer can name is declared once, with the classes, outcome mappings and defects the gate and its I-6
/// verification established.
/// </summary>
public sealed partial class AuditActionCatalogTests
{
    private static readonly AuditActionCatalog Catalog = AuditActionCatalog.Default;

    /// <summary>Actions AUD-1.2 adds to the AUD-1.1 inventory (record §3 and the gate's §22 new coverage).</summary>
    private static readonly string[] Aud12Additions =
    [
        "account.security_mail.delivery_attempted",
        "credential.password.reset_request.suppressed",
        "governance.runtime.enrollment.completed",
        "auth.entra.login.failed",
        "auth.sign_out",
        "entra.tenant_registration.created",
        "entra.tenant_registration.updated",
        "entra.tenant_registration.deleted",
        "entra.tenant_registration.activated",
        "entra.tenant_registration.change_refused",
        "bootstrap.configured_admin.created",
        "auth.sign_in_policy.updated",
    ];

    /// <summary>Actions AUD-1.3 adds (the operator actions on runs that had no row, or none with an actor).</summary>
    private static readonly string[] Aud13Additions =
    [
        "execution.workflow_start.requested",
        "execution.operation_start.requested",
        "execution.workflow_run.cancel_requested",
        "execution.operation_run.cancel_requested",
        "execution.workflow_run.park_requested",
        "execution.workflow_run.resume_requested",
        "execution.workflow_retry.requested",
        "execution.operation_item.retry_requested",
        "execution.operation_item.retry_blocked",
    ];

    /// <summary>Actions AUD-1.4 adds (secret-store events, recorded on the configuration entity that owns the secret).</summary>
    private static readonly string[] Aud14Additions =
    [
        "entra.tenant_registration.client_secret.stored",
        "entra.tenant_registration.client_secret.retired",
        "platform.settings.secret.stored",
        "platform.settings.secret.retired",
        "integration.settings.secret.stored",
        "integration.settings.secret.retired",
    ];

    [Fact]
    public void The_catalog_declares_every_action_written_today_exactly_once()
    {
        Catalog.Definitions.Select(d => d.Action).Should().Contain(Aud12Additions);
        Catalog.Definitions.Where(d => d.IntroducedIn == "AUD-1.2").Select(d => d.Action).Should().BeEquivalentTo(Aud12Additions);
        Catalog.Definitions.Select(d => d.Action).Should().Contain(Aud13Additions);
        Catalog.Definitions.Where(d => d.IntroducedIn == "AUD-1.3").Select(d => d.Action).Should().BeEquivalentTo(Aud13Additions);
        Catalog.Definitions.Where(d => d.IntroducedIn == "AUD-1.4").Select(d => d.Action).Should().BeEquivalentTo(Aud14Additions);
        Catalog.Definitions.Should().HaveCount(
            147 + Aud12Additions.Length + Aud13Additions.Length + Aud14Additions.Length,
            "the AUD-1.1 writer inventory at 34b9cd7 found 147 distinct action names, and AUD-1.2 to AUD-1.4 add only the named ones");
        Catalog.Definitions.Select(d => d.Action).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_action_targets_a_secret_because_a_secrets_store_id_is_the_reference_its_owner_holds()
    {
        // Gate §12.3 never records secret references, and the §22 ruling on secret audit identifiers targets the owning
        // configuration entity instead. The secret target type stays in the closed vocabulary, unused.
        AuditTargetTypes.All.Should().Contain(AuditTargetTypes.Secret);
        Catalog.Definitions.Should().NotContain(d => d.TargetType == AuditTargetTypes.Secret);
    }

    [Fact]
    public void Recorder_secret_events_target_their_owner_and_can_carry_only_ids_and_closed_values()
    {
        var secretEvents = Catalog.Definitions.Where(d => d.Category == AuditCategories.Secrets && d.Details is not null).ToList();
        secretEvents.Select(d => d.Action).Should().Contain(Aud14Additions);
        foreach (var definition in secretEvents)
        {
            definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, definition.Action);
            definition.Severity.Should().Be(AuditSeverities.Elevated, $"{definition.Action}: a change to a secret is elevated (§11.3)");
            definition.TargetType.Should().NotBeNull(definition.Action).And.NotBe(AuditTargetTypes.Secret);

            // No free text: an operator cannot type a value into a secret event, and an id names the owner, not the secret.
            definition.Details!.Fields.Should().OnlyContain(
                f => f.Kind == AuditFieldKind.Id || f.Kind == AuditFieldKind.Enum || f.Kind == AuditFieldKind.Flag || f.Kind == AuditFieldKind.Count,
                definition.Action);
        }
    }

    [Fact]
    public void M1_every_audit_action_name_constant_is_declared()
    {
        var neverWritten = new HashSet<string>(StringComparer.Ordinal)
        {
            // Declared constants with no writer at 34b9cd7 (inventory): not written, so not in the catalog.
            "identity_link.entra.legacy_tenant_completed",
            "auth.entra.authentication_context.changed",
            "credential.password.setup_requested",
            "credential.password.changed",
        };

        // Action names are dotted; a constants class may also hold its writer's outcome label (for example "auth-admin").
        var constants = ActionNameConstants()
            .Where(v => v.Value.Contains('.', StringComparison.Ordinal) && !neverWritten.Contains(v.Value))
            .ToList();

        constants.Should().NotBeEmpty();
        constants.Where(c => Catalog.Find(c.Value) is null).Select(c => $"{c.Name} = {c.Value}")
            .Should().BeEmpty("every action-name constant a writer can pass must be declared");
    }

    [Fact]
    public void M1_every_literal_action_passed_to_an_audit_writer_in_source_is_declared()
    {
        var root = RepoRoot();
        var undeclared = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var separator = Path.DirectorySeparatorChar;
            if (new[] { "Migrations", "obj", "bin", "artifacts", "node_modules" }
                .Any(skip => file.Contains($"{separator}{skip}{separator}", StringComparison.Ordinal)))
                continue;

            var text = File.ReadAllText(file);
            if (!text.Contains("AuditRecord(", StringComparison.Ordinal) && !text.Contains("AuditActionNames", StringComparison.Ordinal))
                continue;

            // A literal action passed straight to an audit helper or record: Helper("a.b", or new AuditRecord(id, actor, "a.b",
            foreach (Match match in LiteralActionCall().Matches(text))
            {
                var action = match.Groups["action"].Value;
                if (Catalog.Find(action) is null)
                    undeclared.Add($"{Path.GetRelativePath(root, file)}: {action}");
            }
        }

        undeclared.Should().BeEmpty();
    }

    [Theory]
    [InlineData("governance.user.username.changed", AuditWriteClass.RequiredMutation)]
    [InlineData("emergency.access.canonicalized", AuditWriteClass.RequiredMutation)]
    [InlineData("schedule.deleted", AuditWriteClass.RequiredMutation)]
    [InlineData("integration.deleted", AuditWriteClass.RequiredMutation)]
    [InlineData("connection.verified", AuditWriteClass.RequiredMutation)]
    [InlineData("execution.operation_item.bulk_retry.requested", AuditWriteClass.RequiredMutation)]
    [InlineData("auth.local.login.success", AuditWriteClass.RequiredSecurityEvent)]
    [InlineData("auth.strong_authentication.step_up.satisfied", AuditWriteClass.RequiredSecurityEvent)]
    [InlineData("connection.structure_browsed", AuditWriteClass.RequiredSecurityEvent)]
    [InlineData("auth.local.login.failed", AuditWriteClass.Attempt)]
    [InlineData("audit.read.denied", AuditWriteClass.Attempt)]
    [InlineData("schedule.delete_rejected", AuditWriteClass.Attempt)]
    [InlineData("execution.workflow_retry.blocked", AuditWriteClass.Attempt)]
    [InlineData("scheduler.schedule.dispatched", AuditWriteClass.Telemetry)]
    [InlineData("module.lifecycle.discover", AuditWriteClass.Telemetry)]
    [InlineData("runtime.composition.invalid_at_startup", AuditWriteClass.Telemetry)]
    [InlineData("identity.username.assigned", AuditWriteClass.ListedException)]
    public void Write_classes_follow_the_gate_table(string action, AuditWriteClass expected) =>
        Catalog.Get(action).WriteClass.Should().Be(expected);

    [Fact]
    public void The_converted_actions_are_exactly_the_named_recorder_actions_and_data_access_is_reserved()
    {
        // An action has a detail schema (and so a typed producer) exactly when it is named in AuditActionNames: AUD-1.1's
        // two actions plus each family AUD-1.2 to AUD-1.4 convert. Every other action stays on the append bridge.
        var named = typeof(AuditActionNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        named.Should().OnlyHaveUniqueItems();
        Catalog.Definitions.Where(d => d.Details is not null).Select(d => d.Action).Should().BeEquivalentTo(named);
        Catalog.Definitions.Should().NotContain(d => d.Category == AuditCategories.DataAccess);
        AuditCategories.All.Should().Contain(AuditCategories.DataAccess);
    }

    [Theory]
    // §11.5 rows, by writer.
    [InlineData("scheduler.schedule.blocked_by_lifecycle", "blocked", "blocked")]
    [InlineData("module.lifecycle.disable", "blocked", "blocked")]
    [InlineData("execution.workflow_start.blocked", "Blocked", "blocked")]
    [InlineData("execution.operation_start.blocked", "Blocked", "blocked")]
    [InlineData("execution.workflow_retry.blocked", "Blocked", "blocked")]
    [InlineData("execution.workflow_run.failed_definition_unavailable", "Blocked", "unknown")]
    [InlineData("execution.operation_run.failed_definition_unavailable", "Blocked", "unknown")]
    [InlineData("scheduler.schedule.dispatched", "success", "succeeded")]
    [InlineData("scheduler.claim.recovered", "recovered", "succeeded")]
    [InlineData("recovery.blocked_work.retry_requested", "requested", "requested")]
    [InlineData("recovery.blocked_work.retry_dispatched", "dispatched", "requested")]
    [InlineData("recovery.blocked_work.retry_refused", "refused", "rejected")]
    [InlineData("recovery.schedules.run_cycle_requested", "completed", "unknown")]
    [InlineData("recovery.schedules.run_cycle_requested", "requested", "requested")]
    [InlineData("scheduler.schedule.legacy_without_integration", "warning", "unknown")]
    [InlineData("runtime.composition.invalid_at_startup", "Degraded", "unknown")]
    // I-6 correction: bulk retry requested records "success"; "success" on completion covers partial failure.
    [InlineData("execution.operation_item.bulk_retry.requested", "success", "requested")]
    [InlineData("execution.operation_item.bulk_retry.completed", "success", "unknown")]
    [InlineData("execution.operation_item.bulk_retry.completed", "failure", "failed")]
    [InlineData("execution.operation_item.attention.acknowledged", "success", "succeeded")]
    // The generic four, any writer.
    [InlineData("connection.created", "succeeded", "succeeded")]
    [InlineData("connection.verified", "failed", "failed")]
    [InlineData("schedule.delete_rejected", "rejected", "rejected")]
    [InlineData("audit.read.denied", "denied", "denied")]
    // Category-like values: unknown unless the action name states the result.
    [InlineData("governance.permission.assignment.updated", "auth-admin", "unknown")]
    [InlineData("platform.settings.set", "platform-settings", "unknown")]
    [InlineData("resource_context.created", "resource-context-management", "unknown")]
    [InlineData("governance.user.username.changed", "auth-admin", "unknown")]
    [InlineData("entra.access_request.rejected", "auth-admin", "unknown")]
    [InlineData("governance.role.mutation_rejected", "auth-admin", "rejected")]
    [InlineData("auth_admin.blocked.last_admin_link_removed", "auth-admin", "blocked")]
    [InlineData("auth.local.login.failed", "auth", "failed")]
    [InlineData("recovery.restore_platform_access.denied", "recovery", "denied")]
    [InlineData("auth.local.login.success", "auth", "succeeded")]
    // Null and anything else.
    [InlineData("connection.structure_browsed", "cancelled", "unknown")]
    [InlineData("connection.created", "", "unknown")]
    public void Recorded_outcomes_map_by_action_and_raw_value(string action, string raw, string expected) =>
        AuditActionCatalog.MapRecordedOutcome(Catalog.Get(action), raw).Should().Be(expected);

    [Fact]
    public void The_entra_access_request_rejection_is_an_administrators_decision_not_a_refused_attempt() =>
        AuditActionCatalog.MapRecordedOutcome(Catalog.Get("entra.access_request.rejected"), "auth-admin").Should().NotBe("rejected");

    [Fact]
    public void Era_invariance_is_declared_only_for_actions_older_than_epic_37_and_matches_the_verified_exceptions()
    {
        Catalog.Definitions.Where(d => d.EraInvariant).Should().OnlyContain(d => !d.IntroducedAfterEpic37Migration);
        Catalog.Definitions.Where(d => !d.IntroducedAfterEpic37Migration && !d.EraInvariant).Select(d => d.Action).Should().BeEquivalentTo(
        [
            "credential.password_set",
            "execution.workflow_run.failed_definition_unavailable",
            "execution.operation_run.failed_definition_unavailable",
            "recovery.schedules.run_cycle_requested",
        ]);
        Catalog.Definitions.Count(d => !d.IntroducedAfterEpic37Migration).Should().Be(44);
    }

    [Fact]
    public void Defects_follow_the_I6_verification_of_section_15_3()
    {
        Defect("account.invitation.sent").Should().BeEquivalentTo(new[] { (AuditAttributionIssues.SubjectRecordedAsActor, AuditIssueCertainties.Definite) });
        Defect("account.invitation.resent").Should().BeEquivalentTo(new[] { (AuditAttributionIssues.SubjectRecordedAsActor, AuditIssueCertainties.Definite) });
        Defect("emergency.auth.blocked.unsafe_operation").Should().BeEquivalentTo(new[] { (AuditAttributionIssues.SubjectRecordedAsActor, AuditIssueCertainties.Definite) });
        Defect("account.security_mail.delivery_failed").Should().BeEquivalentTo(new[] { (AuditAttributionIssues.SubjectPossiblyRecordedAsActor, AuditIssueCertainties.Possible) });
        Defect("emergency.access.canonicalized").Should().BeEquivalentTo(new[] { (AuditAttributionIssues.InitiatorPossiblyMisrecorded, AuditIssueCertainties.Possible) });

        // Removed: the fallbacks were never reachable.
        Catalog.Get("emergency.access.designated").Defects.Should().BeEmpty();
        Catalog.Get("execution.operation_item.bulk_retry.requested").Defects.Should().BeEmpty();
        Catalog.Get("execution.operation_item.bulk_retry.completed").Defects.Should().BeEmpty();
        Catalog.Get("recovery.schedules.run_cycle_requested").Defects.Should().NotContain(d => d.WhenActorEquals == "system:unknown");

        // Actor-conditional entries apply only to the recorded fallback value.
        var retry = Catalog.Get("execution.workflow_retry.blocked").Defects.Single();
        retry.AppliesTo("system").Should().BeTrue();
        retry.AppliesTo("0123456789abcdef01234567").Should().BeFalse();
        Catalog.Get("recovery.run_cycle.denied").Defects.Select(d => d.WhenActorEquals).Should().BeEquivalentTo(["system:unknown", "system"]);
        var reset = Catalog.Get("credential.password.reset_requested").Defects.Single();
        reset.AppliesTo("anonymous").Should().BeFalse();
        reset.AppliesTo("0123456789abcdef01234567").Should().BeTrue();
    }

    [Fact]
    public void Routine_eligibility_is_limited_to_the_gate_families_and_never_covers_blocked_rows()
    {
        Catalog.Definitions.Where(d => d.RoutineOutcomes.Count > 0).Select(d => d.Action).Should().BeEquivalentTo(
        [
            "scheduler.schedule.dispatched", "module.lifecycle.discover",
            "account.profile.avatar.updated", "account.profile.avatar.reset",
            "account.notification.email.opted_in", "account.notification.email.opted_out",
            "account.notification.toast.opted_in", "account.notification.toast.opted_out",
        ]);
        Catalog.Definitions.Should().OnlyContain(d => !d.RoutineOutcomes.Contains(AuditOutcomeCodes.Blocked));
    }

    [Fact]
    public void The_database_check_constraint_spells_exactly_the_closed_vocabulary()
    {
        var sql = Aud1_AuditFoundation.ClassificationSetCheckSql;
        foreach (var (column, values) in new (string, IReadOnlySet<string>)[]
        {
            ("ActorKind", AuditActorKinds.Stored), ("ActorAttribution", AuditAttributions.Stored), ("Category", AuditCategories.All),
            ("Severity", AuditSeverities.All), ("Visibility", AuditVisibilities.All), ("Basis", AuditBases.All),
            ("OutcomeCode", AuditOutcomeCodes.All), ("OutcomeProvenance", AuditOutcomeProvenances.All),
            ("AttributionIssue", AuditAttributionIssues.All), ("AttributionIssueCertainty", AuditIssueCertainties.All),
            ("TargetType", AuditTargetTypes.All),
        })
        {
            InList(sql, column).Should().BeEquivalentTo(values, column);
            sql.Should().Contain(NoTrailingSpace(column), because: $"IN ignores trailing spaces, so {column} needs its own exactness term");
        }

        sql.Should().Contain(NoTrailingSpace("ClassificationState"));
        Aud1_AuditFoundation.ProducerCheckSql.Should().Contain(NoTrailingSpace("Producer")).And.Contain(NoTrailingSpace("DetailsFormat"));
        AuditLogSchema.ClassificationSetCheckSql.Should().Be(sql);
    }

    private static string NoTrailingSpace(string column) => $"DATALENGTH([{column}]) = DATALENGTH(RTRIM([{column}]))";

    [Fact]
    public void The_audit_foundation_migration_runs_after_the_username_backfill_barrier()
    {
        // UsernameBackfill parses legacy canonicalization text and runs only before Iam9_UsernameRequired. The recorder's JSON
        // rows need this migration, so they can never exist while the backfill can run.
        string.CompareOrdinal(Aud1_AuditFoundation.MigrationId, Iam9_UsernameRequired.UsernameRequiredMigrationId).Should().BePositive();
    }

    [Fact]
    public void The_existing_action_name_constants_are_the_foundations_spelling()
    {
        GovernanceAuditActionNames.UserUsernameChanged.Should().Be(AuditActionNames.UsernameChanged);
        EmergencyAccessAuditActionNames.Canonicalized.Should().Be(AuditActionNames.RecoveryAccessCanonicalized);
    }

    private static IEnumerable<(string Code, string Certainty)> Defect(string action) =>
        Catalog.Get(action).Defects.Select(d => (d.Code, d.Certainty));

    private static IEnumerable<string> InList(string sql, string column)
    {
        var match = Regex.Match(sql, $@"\[{column}\] COLLATE Latin1_General_BIN2 IN \((?<values>[^)]*)\)");
        match.Success.Should().BeTrue(column);
        return Regex.Matches(match.Groups["values"].Value, "N'([^']*)'").Select(m => m.Groups[1].Value);
    }

    private static IEnumerable<(string Name, string Value)> ActionNameConstants()
    {
        var assemblies = new[]
        {
            typeof(GovernanceAuditActionNames).Assembly,
            typeof(AuditActionNames).Assembly,
            typeof(AppertureDbContext).Assembly,
            typeof(Apperture.Management.Api.Controllers.UsersController).Assembly,
        };
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => t.Name.EndsWith("AuditActionNames", StringComparison.Ordinal)))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
                yield return ($"{type.Name}.{field.Name}", (string)field.GetRawConstantValue()!);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(
        // AppendBreakGlassUseAuditIfNeededAsync takes the operation name, not an action; its action is a constant.
        @"(?:(?:Append(?!BreakGlass)\w*Audit\w*|\bAuditAsync|TryAuditAsync|WriteAuditAsync)\(\s*|new AuditRecord\(\s*[^,;]+,\s*[^,;]+,\s*)""(?<action>[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex LiteralActionCall();
}
