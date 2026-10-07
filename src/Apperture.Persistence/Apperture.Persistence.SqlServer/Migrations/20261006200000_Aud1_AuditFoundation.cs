using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// AUD-1.1 audit foundation (audit hardening design gate §10.1, §11.8, §13, §14, §16, §19.2): event identity, producer,
/// details format and the classification set on <c>AuditLogs</c>, the §16 indexes, and the integrity guard.
/// </summary>
/// <remarks>
/// <para>Additive: every new column is nullable and no existing row or column is changed. Existing rows stay unclassified
/// until the startup classification step completes them, one row per conditional transition.</para>
/// <para>The guard is two triggers and two check constraints. It refuses any change to an evidence column, any change to a
/// completed classification (including filling an intentionally null column), any partial or invalid classification, and
/// every delete. It is an integrity guard against application defects and ordinary DML, not access control: a login that
/// can alter the table can disable it (§19.3).</para>
/// <para>The SQL here is frozen with the migration. <see cref="AuditLogSchema"/> refers to these constants so the EF model
/// and this migration cannot drift.</para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Aud1_AuditFoundation : Migration
{
    public const string MigrationId = "20261006200000_Aud1_AuditFoundation";

    // Both checks are written as CASE WHEN <rule> THEN 1 ELSE 0 END = 1. A CHECK constraint rejects only FALSE, and a
    // comparison with NULL is UNKNOWN, so a bare rule would let a row with a missing required value through.
    // Vocabulary values must match exactly. The binary collation makes the comparison case-sensitive, but = and IN ignore
    // trailing spaces under every collation (ANSI padding), so each value is also required to have none:
    // DATALENGTH([c]) = DATALENGTH(RTRIM([c])).

    /// <summary>A producer row has its identity; a recorder row is complete with json/1 details; an append row has no format.</summary>
    public const string ProducerCheckSql =
        "CASE WHEN [Producer] IS NULL AND [EventId] IS NULL AND [OperationEventId] IS NULL AND [DetailsFormat] IS NULL"
        + " OR [Producer] COLLATE Latin1_General_BIN2 = N'append' AND DATALENGTH([Producer]) = DATALENGTH(RTRIM([Producer]))"
        + " AND [EventId] IS NOT NULL AND [OperationEventId] IS NOT NULL AND [DetailsFormat] IS NULL"
        + " OR [Producer] COLLATE Latin1_General_BIN2 = N'recorder' AND DATALENGTH([Producer]) = DATALENGTH(RTRIM([Producer]))"
        + " AND [EventId] IS NOT NULL AND [OperationEventId] IS NOT NULL"
        + " AND [DetailsFormat] COLLATE Latin1_General_BIN2 = N'json/1' AND DATALENGTH([DetailsFormat]) = DATALENGTH(RTRIM([DetailsFormat]))"
        + " AND [ClassificationState] IS NOT NULL"
        + " THEN 1 ELSE 0 END = 1";

    /// <summary>
    /// A row is either wholly unclassified or complete, with every value from the closed vocabulary and the intentional
    /// nulls consistent with its kind (§11.8).
    /// </summary>
    public const string ClassificationSetCheckSql =
        "CASE WHEN [ClassificationState] IS NULL"
        + " AND [ActorKind] IS NULL AND [ActorUserId] IS NULL AND [ActorUsername] IS NULL"
        + " AND [ActorExternalProvider] IS NULL AND [ActorExternalTenantId] IS NULL AND [ActorExternalSubjectId] IS NULL"
        + " AND [ActorAttribution] IS NULL AND [AttributionIssue] IS NULL AND [AttributionIssueCertainty] IS NULL"
        + " AND [TargetType] IS NULL AND [TargetId] IS NULL"
        + " AND [Category] IS NULL AND [Severity] IS NULL AND [Visibility] IS NULL AND [Basis] IS NULL"
        + " AND [OutcomeCode] IS NULL AND [OutcomeProvenance] IS NULL"
        + " OR [ClassificationState] COLLATE Latin1_General_BIN2 = N'complete'"
        + " AND [ActorKind] COLLATE Latin1_General_BIN2 IN (N'user', N'external', N'system', N'anonymous', N'worker', N'unresolved')"
        + " AND [ActorAttribution] COLLATE Latin1_General_BIN2 IN (N'recorded', N'recorded_user_id', N'recorded_literal', N'inferred_username', N'inferred_entra_object_id', N'unresolved', N'ambiguous')"
        + " AND [Category] COLLATE Latin1_General_BIN2 IN (N'identity', N'governance', N'configuration', N'operations', N'secrets', N'system', N'data_access', N'other')"
        + " AND [Severity] COLLATE Latin1_General_BIN2 IN (N'standard', N'elevated', N'critical')"
        + " AND [Visibility] COLLATE Latin1_General_BIN2 IN (N'default', N'routine')"
        + " AND [Basis] COLLATE Latin1_General_BIN2 IN (N'change', N'request', N'external_attempt', N'confirmed_result', N'security_event', N'attempt', N'telemetry', N'unspecified')"
        + " AND [OutcomeCode] COLLATE Latin1_General_BIN2 IN (N'succeeded', N'failed', N'denied', N'rejected', N'blocked', N'requested', N'unknown')"
        + " AND [OutcomeProvenance] COLLATE Latin1_General_BIN2 IN (N'recorded', N'indeterminate')"
        + " AND DATALENGTH([ClassificationState]) = DATALENGTH(RTRIM([ClassificationState]))"
        + " AND DATALENGTH([ActorKind]) = DATALENGTH(RTRIM([ActorKind]))"
        + " AND DATALENGTH([ActorAttribution]) = DATALENGTH(RTRIM([ActorAttribution]))"
        + " AND DATALENGTH([Category]) = DATALENGTH(RTRIM([Category]))"
        + " AND DATALENGTH([Severity]) = DATALENGTH(RTRIM([Severity]))"
        + " AND DATALENGTH([Visibility]) = DATALENGTH(RTRIM([Visibility]))"
        + " AND DATALENGTH([Basis]) = DATALENGTH(RTRIM([Basis]))"
        + " AND DATALENGTH([OutcomeCode]) = DATALENGTH(RTRIM([OutcomeCode]))"
        + " AND DATALENGTH([OutcomeProvenance]) = DATALENGTH(RTRIM([OutcomeProvenance]))"
        + " AND ([ActorKind] = N'user' AND [ActorUserId] IS NOT NULL OR [ActorKind] <> N'user' AND [ActorUserId] IS NULL)"
        + " AND ([ActorKind] = N'external' AND [ActorExternalProvider] IS NOT NULL AND [ActorExternalTenantId] IS NOT NULL AND [ActorExternalSubjectId] IS NOT NULL"
        + " OR [ActorKind] <> N'external' AND [ActorExternalProvider] IS NULL AND [ActorExternalTenantId] IS NULL AND [ActorExternalSubjectId] IS NULL)"
        + " AND ([ActorUsername] IS NULL OR [ActorKind] = N'user')"
        + " AND ([AttributionIssue] IS NULL AND [AttributionIssueCertainty] IS NULL"
        + " OR [AttributionIssue] COLLATE Latin1_General_BIN2 IN (N'subject_recorded_as_actor', N'subject_possibly_recorded_as_actor', N'initiator_not_recorded', N'initiator_possibly_misrecorded', N'possible_placeholder')"
        + " AND [AttributionIssueCertainty] COLLATE Latin1_General_BIN2 IN (N'definite', N'possible')"
        + " AND DATALENGTH([AttributionIssue]) = DATALENGTH(RTRIM([AttributionIssue]))"
        + " AND DATALENGTH([AttributionIssueCertainty]) = DATALENGTH(RTRIM([AttributionIssueCertainty])))"
        + " AND ([TargetType] IS NULL AND [TargetId] IS NULL"
        + " OR [TargetType] COLLATE Latin1_General_BIN2 IN (N'user', N'role', N'identity_link', N'external_identity', N'entra_access_request', N'entra_tenant_registration', N'auth_policy',"
        + " N'resource_context', N'integration', N'connection', N'schedule', N'platform_setting', N'integration_setting', N'notification_definition',"
        + " N'notification_subscription', N'managed_contact', N'operational_team', N'module', N'workflow_run', N'operation_run', N'operation_item',"
        + " N'runtime_instance', N'secret', N'instance') AND DATALENGTH([TargetType]) = DATALENGTH(RTRIM([TargetType])) AND [TargetId] IS NOT NULL)"
        + " THEN 1 ELSE 0 END = 1";

    /// <summary>Columns that never change after insert (§19.1). <c>Id</c> is an identity and cannot be updated at all.</summary>
    private static readonly string[] ImmutableColumns =
    [
        "EventId", "OperationEventId", "At", "Actor", "Action", "Details", "DetailsFormat", "Outcome",
        "ModuleId", "ScheduleId", "RunId", "ResourceContextId", "CorrelationId", "Producer",
    ];

    /// <summary>The classification set (§11.8), frozen as a whole once complete.</summary>
    private static readonly string[] ClassificationSetColumns =
    [
        "ClassificationState",
        "ActorKind", "ActorUserId", "ActorUsername",
        "ActorExternalProvider", "ActorExternalTenantId", "ActorExternalSubjectId",
        "ActorAttribution", "AttributionIssue", "AttributionIssueCertainty",
        "TargetType", "TargetId",
        "Category", "Severity", "Visibility", "Basis",
        "OutcomeCode", "OutcomeProvenance",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddColumn(migrationBuilder, "EventId", 64);
        AddColumn(migrationBuilder, "OperationEventId", 64);
        AddColumn(migrationBuilder, "Producer", 16);
        AddColumn(migrationBuilder, "DetailsFormat", 16);
        AddColumn(migrationBuilder, "ClassificationState", 16);
        AddColumn(migrationBuilder, "ActorKind", 16);
        AddColumn(migrationBuilder, "ActorUserId", 64);
        AddColumn(migrationBuilder, "ActorUsername", 256);
        AddColumn(migrationBuilder, "ActorExternalProvider", 16);
        AddColumn(migrationBuilder, "ActorExternalTenantId", 64);
        AddColumn(migrationBuilder, "ActorExternalSubjectId", 64);
        AddColumn(migrationBuilder, "ActorAttribution", 32);
        AddColumn(migrationBuilder, "AttributionIssue", 64);
        AddColumn(migrationBuilder, "AttributionIssueCertainty", 16);
        AddColumn(migrationBuilder, "TargetType", 64);
        AddColumn(migrationBuilder, "TargetId", 256);
        AddColumn(migrationBuilder, "Category", 32);
        AddColumn(migrationBuilder, "Severity", 16);
        AddColumn(migrationBuilder, "Visibility", 16);
        AddColumn(migrationBuilder, "Basis", 32);
        AddColumn(migrationBuilder, "OutcomeCode", 16);
        AddColumn(migrationBuilder, "OutcomeProvenance", 16);

        migrationBuilder.AddCheckConstraint(
            name: AuditLogSchema.ProducerCheck,
            table: "AuditLogs",
            sql: ProducerCheckSql);
        migrationBuilder.AddCheckConstraint(
            name: AuditLogSchema.ClassificationSetCheck,
            table: "AuditLogs",
            sql: ClassificationSetCheckSql);

        migrationBuilder.CreateIndex(
            name: AuditLogSchema.EventIdIndex,
            table: "AuditLogs",
            column: "EventId",
            unique: true,
            filter: "[EventId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_ActorUserId_At_Id",
            table: "AuditLogs",
            columns: ["ActorUserId", "At", "Id"],
            descending: [false, true, true],
            filter: "[ActorUserId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_TargetType_TargetId_At_Id",
            table: "AuditLogs",
            columns: ["TargetType", "TargetId", "At", "Id"],
            descending: [false, false, true, true],
            filter: "[TargetType] IS NOT NULL AND [TargetId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_DefaultVisible_At_Id",
            table: "AuditLogs",
            columns: ["At", "Id"],
            descending: [true, true],
            filter: "[Visibility] = N'default'");
        migrationBuilder.CreateIndex(
            name: AuditLogSchema.UnclassifiedIndex,
            table: "AuditLogs",
            column: "Id",
            filter: "[ClassificationState] IS NULL");

        migrationBuilder.Sql(UpdateGuardTriggerSql());
        migrationBuilder.Sql(DeleteGuardTriggerSql());
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [dbo].[{AuditLogSchema.DeleteGuardTrigger}];");
        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [dbo].[{AuditLogSchema.UpdateGuardTrigger}];");

        migrationBuilder.DropIndex(name: AuditLogSchema.UnclassifiedIndex, table: "AuditLogs");
        migrationBuilder.DropIndex(name: "IX_AuditLogs_DefaultVisible_At_Id", table: "AuditLogs");
        migrationBuilder.DropIndex(name: "IX_AuditLogs_TargetType_TargetId_At_Id", table: "AuditLogs");
        migrationBuilder.DropIndex(name: "IX_AuditLogs_ActorUserId_At_Id", table: "AuditLogs");
        migrationBuilder.DropIndex(name: AuditLogSchema.EventIdIndex, table: "AuditLogs");

        migrationBuilder.DropCheckConstraint(name: AuditLogSchema.ClassificationSetCheck, table: "AuditLogs");
        migrationBuilder.DropCheckConstraint(name: AuditLogSchema.ProducerCheck, table: "AuditLogs");

        foreach (var column in new[]
        {
            "OutcomeProvenance", "OutcomeCode", "Basis", "Visibility", "Severity", "Category", "TargetId", "TargetType",
            "AttributionIssueCertainty", "AttributionIssue", "ActorAttribution", "ActorExternalSubjectId",
            "ActorExternalTenantId", "ActorExternalProvider", "ActorUsername", "ActorUserId", "ActorKind",
            "ClassificationState", "DetailsFormat", "Producer", "OperationEventId", "EventId",
        })
        {
            migrationBuilder.DropColumn(name: column, table: "AuditLogs");
        }
    }

    private static void AddColumn(MigrationBuilder migrationBuilder, string name, int maxLength) =>
        migrationBuilder.AddColumn<string>(
            name: name,
            table: "AuditLogs",
            type: $"nvarchar({maxLength})",
            maxLength: maxLength,
            nullable: true);

    /// <summary>
    /// True when the column differs between <c>inserted</c> and <c>deleted</c>, byte for byte: a change of letter case or
    /// trailing spaces counts, which collation equality would hide.
    /// </summary>
    private static string Changed(string column) =>
        $"(i.[{column}] IS NULL AND d.[{column}] IS NOT NULL OR i.[{column}] IS NOT NULL AND d.[{column}] IS NULL"
        + $" OR DATALENGTH(i.[{column}]) <> DATALENGTH(d.[{column}])"
        + $" OR CAST(i.[{column}] AS varbinary(max)) <> CAST(d.[{column}] AS varbinary(max)))";

    private static string AnyChanged(IEnumerable<string> columns) =>
        "(" + string.Join("\n               OR ", columns.Select(Changed)) + ")";

    private static string UpdateGuardTriggerSql() =>
        $"""
        CREATE TRIGGER [dbo].[{AuditLogSchema.UpdateGuardTrigger}] ON [dbo].[AuditLogs]
        AFTER UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            -- Integrity guard (AUD-1 design gate §19.2). Refuses the whole statement when any affected row:
            --   changes an evidence column; or
            --   was completely classified (any change to its classification set, including filling a null column); or
            --   was unclassified and changes a classification column while leaving the state null, or sets a state other
            --   than 'complete' (the one legitimate transition is unclassified -> complete, checked whole by
            --   {AuditLogSchema.ClassificationSetCheck}).
            IF EXISTS (
                SELECT 1
                FROM inserted AS i
                INNER JOIN deleted AS d ON d.[Id] = i.[Id]
                WHERE {AnyChanged(ImmutableColumns)}
                   OR d.[ClassificationState] IS NOT NULL AND {AnyChanged(ClassificationSetColumns)}
                   OR d.[ClassificationState] IS NULL AND i.[ClassificationState] IS NULL AND {AnyChanged(ClassificationSetColumns)}
                   OR d.[ClassificationState] IS NULL AND i.[ClassificationState] IS NOT NULL
                      AND CAST(i.[ClassificationState] AS varbinary(max)) <> CAST(N'complete' AS varbinary(max))
            )
            BEGIN
                THROW {AuditLogSchema.UpdateRefusedError}, N'Audit records are append-only: evidence never changes, and a completed classification is frozen.', 1;
            END
        END
        """;

    private static string DeleteGuardTriggerSql() =>
        $"""
        CREATE TRIGGER [dbo].[{AuditLogSchema.DeleteGuardTrigger}] ON [dbo].[AuditLogs]
        INSTEAD OF DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            -- Integrity guard (AUD-1 design gate §19.2): audit records are never deleted.
            THROW {AuditLogSchema.DeleteRefusedError}, N'Audit records are never deleted.', 1;
        END
        """;
}
