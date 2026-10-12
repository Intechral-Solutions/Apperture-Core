using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: the catalog entries, detail schemas and typed producers of the connection, resource context and ownership move
/// families (design gate §11, §12, §13.1; M7). The transactional behaviour is proven by the service tests and the SQL Server suites.
/// </summary>
public sealed class Slice5AuditEventTests
{
    private static readonly string[] ConnectionWriters =
    [
        AuditActionNames.ConnectionCreated, AuditActionNames.ConnectionUpdated, AuditActionNames.ConnectionSecretRotated,
        AuditActionNames.ConnectionRetired, AuditActionNames.ConnectionVerified, AuditActionNames.ConnectionSecretStored,
    ];

    private static readonly string[] ContextWriters =
    [
        AuditActionNames.ResourceContextCreated, AuditActionNames.ResourceContextUpdated,
        AuditActionNames.ResourceContextArchived, AuditActionNames.ResourceContextUnarchived,
    ];

    private static readonly string[] MoveWriters =
    [
        AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn,
        AuditActionNames.ConnectionMovedOut, AuditActionNames.ConnectionMovedIn,
        AuditActionNames.ScheduleMovedOut, AuditActionNames.ScheduleMovedIn,
    ];

    private static readonly string[] MoveRefusals =
    [
        AuditActionNames.IntegrationMoveRejected, AuditActionNames.ConnectionMoveRejected, AuditActionNames.ScheduleMoveRejected,
    ];

    private static readonly string[] AllActions =
        [.. ConnectionWriters, AuditActionNames.ConnectionStructureBrowsed, AuditActionNames.ConnectionStructureBrowseFailed,
            .. ContextWriters, .. MoveWriters, .. MoveRefusals];

    [Fact]
    public void Every_action_has_a_structured_schema()
    {
        foreach (var action in AllActions)
            AuditActionCatalog.Default.Get(action).Details.Should().NotBeNull(action);
    }

    [Fact]
    public void The_connection_context_and_move_writers_are_required_mutations_that_commit_with_their_change()
    {
        foreach (var action in ConnectionWriters.Concat(ContextWriters).Concat(MoveWriters))
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, action);
        }

        AuditActionCatalog.Default.Get(AuditActionNames.ConnectionVerified).Basis.Should().Be(AuditBases.ExternalAttempt);
        foreach (var action in ConnectionWriters.Where(a => a != AuditActionNames.ConnectionVerified).Concat(ContextWriters).Concat(MoveWriters))
            AuditActionCatalog.Default.Get(action).Basis.Should().Be(AuditBases.Change, action);
    }

    [Fact]
    public void A_browse_is_a_required_security_event_and_a_failed_browse_is_an_attempt()
    {
        var browsed = AuditActionCatalog.Default.Get(AuditActionNames.ConnectionStructureBrowsed);
        browsed.WriteClass.Should().Be(AuditWriteClass.RequiredSecurityEvent);
        browsed.Basis.Should().Be(AuditBases.SecurityEvent);

        var failed = AuditActionCatalog.Default.Get(AuditActionNames.ConnectionStructureBrowseFailed);
        failed.WriteClass.Should().Be(AuditWriteClass.Attempt);
        failed.Basis.Should().Be(AuditBases.ExternalAttempt);
    }

    [Fact]
    public void A_rejected_move_is_an_attempt_and_a_secret_row_is_in_the_secrets_category()
    {
        foreach (var action in MoveRefusals)
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.Attempt, action);
            definition.Basis.Should().Be(AuditBases.Attempt, action);
        }

        var secret = AuditActionCatalog.Default.Get(AuditActionNames.ConnectionSecretStored);
        secret.Category.Should().Be(AuditCategories.Secrets);
        secret.Severity.Should().Be(AuditSeverities.Elevated);
        AuditActionCatalog.Default.Get(AuditActionNames.ConnectionSecretRotated).Category.Should().Be(AuditCategories.Secrets);
    }

    [Fact]
    public void Each_action_targets_its_own_entity()
    {
        foreach (var action in AllActions.Where(a => a.StartsWith("connection.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Connection, action);
        foreach (var action in AllActions.Where(a => a.StartsWith("resource_context.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.ResourceContext, action);
        foreach (var action in AllActions.Where(a => a.StartsWith("integration.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Integration, action);
        foreach (var action in AllActions.Where(a => a.StartsWith("schedule.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Schedule, action);
    }

    [Fact]
    public void No_connection_event_can_carry_a_value_a_secret_a_reference_or_remote_text()
    {
        var actions = AllActions.Where(a => a.StartsWith("connection.", StringComparison.Ordinal));
        foreach (var action in actions)
        {
            var fields = AuditActionCatalog.Default.Get(action).Details!.Fields;
            fields.Should().OnlyContain(
                f => f.Kind == AuditFieldKind.NameSnapshot || f.Kind == AuditFieldKind.Id || f.Kind == AuditFieldKind.Count
                    || f.Kind == AuditFieldKind.Enum || f.Kind == AuditFieldKind.Flag || f.Kind == AuditFieldKind.IdList,
                action);
            fields.Select(f => f.Name).Should().NotContain(n =>
                n.Contains("summary", StringComparison.OrdinalIgnoreCase)
                || n.Contains("message", StringComparison.OrdinalIgnoreCase)
                || n.Contains("ref", StringComparison.OrdinalIgnoreCase)
                || n.Contains("value", StringComparison.OrdinalIgnoreCase)
                || n.Contains("config", StringComparison.OrdinalIgnoreCase) && n != "configChanged" && n != "configFieldsChanged",
                action);
        }
    }

    [Fact]
    public void A_connection_creation_records_its_name_connector_and_how_many_secret_fields_it_stored()
    {
        var created = AuditEvents.ConnectionCreated("con-1", "Reporting database", "platform.sql", fieldsStored: 2, resourceContextId: "ctx-1");

        created.Action.Should().Be("connection.created");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.Connection, "con-1"));
        created.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        created.Related.ResourceContextId.Should().Be("ctx-1");
        created.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Reporting database",
            ["connectorId"] = "platform.sql",
            ["fieldsStored"] = 2,
        });
    }

    [Fact]
    public void A_connection_change_is_a_rotation_when_any_secret_field_changed_and_an_update_otherwise()
    {
        var rotated = AuditEvents.ConnectionChanged("con-1", "Reporting", "platform.sql", nameChanged: true, ["BaseUrl"], ["Password"], "ctx-1");
        rotated.Action.Should().Be("connection.secret_rotated");
        rotated.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["connectorId"] = "platform.sql",
            ["nameChanged"] = true,
            ["configFieldsChanged"] = 1,
            ["fieldsRotated"] = 1,
        }, "the Secrets-category row counts the fields; their keys are on the connection.secret.stored rows");

        var updated = AuditEvents.ConnectionChanged("con-1", "Reporting", "platform.sql", nameChanged: false, ["BaseUrl"], [], "ctx-1");
        updated.Action.Should().Be("connection.updated");
        updated.Fields["configChanged"].Should().BeEquivalentTo(new[] { "BaseUrl" });
        updated.Fields.Should().NotContainKey("nameChanged");

        var nothing = () => AuditEvents.ConnectionChanged("con-1", "Reporting", "platform.sql", false, [], [], null);
        nothing.Should().Throw<ArgumentException>("an update that changes nothing is not recorded");
    }

    [Fact]
    public void A_secret_stored_row_names_the_field_and_whether_it_replaced_a_secret_and_nothing_else()
    {
        var stored = AuditEvents.ConnectionSecretStored("con-1", "Password", replaced: true, "ctx-1");

        stored.Action.Should().Be("connection.secret.stored");
        stored.Target.Should().Be(new AuditTarget(AuditTargetTypes.Connection, "con-1"));
        stored.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["fieldKey"] = "Password", ["change"] = "replaced" });
        AuditEvents.ConnectionSecretStored("con-1", "Password", replaced: false, null).Fields["change"].Should().Be("created");
    }

    [Theory]
    [InlineData(ConnectionVerificationStatus.Success, "succeeded", "success")]
    [InlineData(ConnectionVerificationStatus.Failed, "failed", "failed")]
    [InlineData(ConnectionVerificationStatus.Unavailable, "unknown", "unavailable")]
    public void A_verification_records_only_what_the_verifier_answered(ConnectionVerificationStatus status, string outcome, string recorded)
    {
        var verified = AuditEvents.ConnectionVerified("con-1", "Reporting", "platform.sql", status, "ctx-1");

        verified.Action.Should().Be("connection.verified");
        verified.OutcomeCode.Should().Be(outcome);
        verified.Fields["verificationStatus"].Should().Be(recorded);
        verified.Fields.Keys.Should().BeEquivalentTo("name", "connectorId", "verificationStatus");
    }

    [Fact]
    public void A_verifier_that_produced_no_answer_is_a_failed_attempt_with_no_status_and_a_never_run_verification_is_refused()
    {
        var threw = AuditEvents.ConnectionVerified("con-1", "Reporting", "platform.sql", null, null);
        threw.OutcomeCode.Should().Be(AuditOutcomeCodes.Failed);
        threw.Fields.Should().NotContainKey("verificationStatus");

        var cancelled = AuditEvents.ConnectionVerified("con-1", "Reporting", "platform.sql", null, null, noAnswerBecauseCancelled: true);
        cancelled.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown);
        cancelled.Fields.Should().NotContainKey("verificationStatus");

        var neverRan = () => AuditEvents.ConnectionVerified("con-1", "Reporting", "platform.sql", ConnectionVerificationStatus.NeverTested, null);
        neverRan.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ConnectionToolOutcome.Succeeded, "succeeded", "succeeded")]
    [InlineData(ConnectionToolOutcome.RemoteDenied, "failed", "remote_denied")]
    [InlineData(ConnectionToolOutcome.RemoteUnavailable, "failed", "remote_unavailable")]
    [InlineData(ConnectionToolOutcome.SecretsUnavailable, "failed", "secrets_unavailable")]
    [InlineData(ConnectionToolOutcome.Failed, "failed", "failed")]
    public void A_browse_records_the_operation_and_the_outcome_but_never_what_was_browsed(ConnectionToolOutcome result, string outcome, string recorded)
    {
        var browsed = AuditEvents.ConnectionStructureBrowsed("con-1", "Reporting", "platform.sql", "tables", result, resultCount: 7, truncated: false, "ctx-1");

        browsed.Action.Should().Be("connection.structure_browsed");
        browsed.OutcomeCode.Should().Be(outcome);
        browsed.Fields["result"].Should().Be(recorded);
        browsed.Fields["operation"].Should().Be("tables");
        browsed.Fields["tool"].Should().Be("sql-structure");
        browsed.Fields["resultCount"].Should().Be(7);
        browsed.Fields.Keys.Should().BeEquivalentTo("name", "connectorId", "tool", "operation", "result", "resultCount", "truncated");
    }

    [Fact]
    public void A_browse_that_threw_or_was_cancelled_is_a_failed_attempt()
    {
        var cancelled = AuditEvents.ConnectionStructureBrowseFailed("con-1", "Reporting", "platform.sql", "schemas", cancelled: true, "ctx-1");
        var failed = AuditEvents.ConnectionStructureBrowseFailed("con-1", "Reporting", "platform.sql", "schemas", cancelled: false, "ctx-1");

        cancelled.Action.Should().Be("connection.structure_browse_failed");
        cancelled.OutcomeCode.Should().Be(AuditOutcomeCodes.Failed);
        cancelled.Fields["reason"].Should().Be("cancelled");
        failed.Fields["reason"].Should().Be("failed");

        var badOperation = () => AuditEvents.ConnectionStructureBrowseFailed("con-1", "R", "platform.sql", "rows", false, null);
        badOperation.Should().Throw<ArgumentException>("only the three structure operations are declared");
    }

    [Fact]
    public void A_context_event_is_about_the_context_and_its_own_related_context()
    {
        var created = AuditEvents.ResourceContextCreated("ctx-1", "Canvas", "canvas");
        created.Action.Should().Be("resource_context.created");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.ResourceContext, "ctx-1"));
        created.Related.ResourceContextId.Should().Be("ctx-1");
        created.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["name"] = "Canvas", ["slug"] = "canvas" });

        AuditEvents.ResourceContextArchived("ctx-1", "Canvas").Action.Should().Be("resource_context.archived");
        AuditEvents.ResourceContextUnarchived("ctx-1", "Canvas").Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["name"] = "Canvas" });
    }

    [Fact]
    public void A_context_update_names_the_changed_fields_in_declared_order_and_is_never_empty()
    {
        var updated = AuditEvents.ResourceContextUpdated("ctx-1", "Canvas", ["description", "name"]);
        updated.Fields["changed"].Should().BeEquivalentTo(new[] { "name", "description" }, o => o.WithStrictOrdering());

        var empty = () => AuditEvents.ResourceContextUpdated("ctx-1", "Canvas", []);
        var unknown = () => AuditEvents.ResourceContextUpdated("ctx-1", "Canvas", ["state"]);
        empty.Should().Throw<ArgumentException>();
        unknown.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_moved_out_and_moved_in_pair_shares_a_correlation_and_each_is_related_to_its_own_context()
    {
        var movedOut = AuditEvents.OwnershipMovedOut(AuditTargetTypes.Connection, "con-1", "Reporting", "ctx-a", "ctx-b", null, "op-1");
        var movedIn = AuditEvents.OwnershipMovedIn(AuditTargetTypes.Connection, "con-1", "Reporting", "ctx-a", "ctx-b", null, "op-1");

        movedOut.Action.Should().Be("connection.ownership.moved_out");
        movedIn.Action.Should().Be("connection.ownership.moved_in");
        movedOut.Target.Should().Be(new AuditTarget(AuditTargetTypes.Connection, "con-1"));
        movedOut.Related.ResourceContextId.Should().Be("ctx-a", "a filter by the source finds what left it");
        movedIn.Related.ResourceContextId.Should().Be("ctx-b", "a filter by the target finds what arrived in it");
        movedOut.Related.CorrelationId.Should().Be("op-1");
        movedIn.Related.CorrelationId.Should().Be("op-1");
        movedOut.Fields.Should().BeEquivalentTo(movedIn.Fields);
        movedOut.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Reporting",
            ["sourceContextId"] = "ctx-a",
            ["targetContextId"] = "ctx-b",
        });
    }

    [Theory]
    [InlineData(AuditTargetTypes.Integration, "integration.ownership.moved_out", "integration.ownership.moved_in", "integration.ownership.move_rejected")]
    [InlineData(AuditTargetTypes.Connection, "connection.ownership.moved_out", "connection.ownership.moved_in", "connection.ownership.move_rejected")]
    [InlineData(AuditTargetTypes.Schedule, "schedule.ownership.moved_out", "schedule.ownership.moved_in", "schedule.ownership.move_rejected")]
    public void Each_movable_resource_has_its_own_actions(string targetType, string movedOut, string movedIn, string rejected)
    {
        AuditEvents.OwnershipMovedOut(targetType, "r-1", "R", "a", "b", null, "op").Action.Should().Be(movedOut);
        AuditEvents.OwnershipMovedIn(targetType, "r-1", "R", "a", "b", null, "op").Action.Should().Be(movedIn);
        var reject = AuditEvents.OwnershipMoveRejected(targetType, "r-1", "R", "a", "b", ["target_archived"]);
        reject.Action.Should().Be(rejected);
        reject.Target.Should().Be(new AuditTarget(targetType, "r-1"));
    }

    [Fact]
    public void An_integration_move_counts_its_attached_schedules_exactly_and_lists_them_up_to_a_bound()
    {
        var ids = Enumerable.Range(0, 600).Select(i => $"sch-{i:D4}").ToList();
        var moved = AuditEvents.OwnershipMovedOut(AuditTargetTypes.Integration, "int-1", "Sample", "ctx-a", "ctx-b", ids, "op-1");

        moved.Fields["schedulesMoved"].Should().Be(600, "the count is always exact");
        ((IReadOnlyList<string>)moved.Fields["scheduleIds"]!).Should().HaveCount(500).And.StartWith("sch-0000");

        var none = AuditEvents.OwnershipMovedOut(AuditTargetTypes.Integration, "int-1", "Sample", "ctx-a", "ctx-b", [], "op-1");
        none.Fields["schedulesMoved"].Should().Be(0);
        none.Fields.Should().NotContainKey("scheduleIds");
    }

    [Fact]
    public void A_rejection_lists_its_blockers_once_each_sorted_and_is_related_to_the_source()
    {
        var rejected = AuditEvents.OwnershipMoveRejected(
            AuditTargetTypes.Integration, "int-1", "Sample", "ctx-a", "ctx-b", ["target_archived", "already_in_target", "target_archived"]);

        rejected.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        rejected.Related.ResourceContextId.Should().Be("ctx-a");
        rejected.Fields["blockers"].Should().BeEquivalentTo(new[] { "already_in_target", "target_archived" }, o => o.WithStrictOrdering());

        var none = () => AuditEvents.OwnershipMoveRejected(AuditTargetTypes.Integration, "int-1", "Sample", "a", "b", []);
        none.Should().Throw<ArgumentException>();
        var notMovable = () => AuditEvents.OwnershipMovedOut(AuditTargetTypes.Binding, "b-1", "B", "a", "b", null, "op");
        notMovable.Should().Throw<ArgumentException>();
    }
}
