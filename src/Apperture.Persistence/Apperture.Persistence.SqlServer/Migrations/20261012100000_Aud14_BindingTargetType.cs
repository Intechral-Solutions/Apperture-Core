using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// AUD-1.4 slice 4: <c>binding</c> joins the closed audit target vocabulary (audit hardening design gate §11.1). The classification
/// check constraint spells that vocabulary out, so the constraint is replaced by one that also accepts <c>binding</c>.
/// </summary>
/// <remarks>
/// <para>Nothing about an existing row changes: the new vocabulary is a superset of the old one, so every stored row still
/// satisfies the constraint. The row guard triggers (§19.2) are untouched; they refuse row changes, not a constraint
/// replacement. The constraint text is derived from the AUD-1.1 text by one named substitution, so the two cannot drift
/// apart in any other respect, and a unit test pins that the result spells exactly <c>AuditTargetTypes.All</c>.</para>
/// <para><c>Down</c> restores the AUD-1.1 constraint. It fails, by design, while any recorded <c>binding</c> row exists: the
/// audit table is append-only, so a rollback cannot remove evidence.</para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Aud14_BindingTargetType : Migration
{
    public const string MigrationId = "20261012100000_Aud14_BindingTargetType";

    private const string ScheduleTargetEntry = "N'schedule', ";
    private const string ScheduleAndBindingTargetEntries = "N'schedule', N'binding', ";

    /// <summary>The classification check with <c>binding</c> accepted as a target type.</summary>
    public static readonly string ClassificationSetCheckSql = Build();

    private static string Build()
    {
        var sql = Aud1_AuditFoundation.ClassificationSetCheckSql;
        var index = sql.IndexOf(ScheduleTargetEntry, StringComparison.Ordinal);
        if (index < 0 || sql.IndexOf(ScheduleTargetEntry, index + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("The AUD-1.1 classification check no longer has exactly one schedule target entry.");
        return sql.Replace(ScheduleTargetEntry, ScheduleAndBindingTargetEntries, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: AuditLogSchema.ClassificationSetCheck, table: "AuditLogs");
        migrationBuilder.AddCheckConstraint(
            name: AuditLogSchema.ClassificationSetCheck,
            table: "AuditLogs",
            sql: ClassificationSetCheckSql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: AuditLogSchema.ClassificationSetCheck, table: "AuditLogs");
        migrationBuilder.AddCheckConstraint(
            name: AuditLogSchema.ClassificationSetCheck,
            table: "AuditLogs",
            sql: Aud1_AuditFoundation.ClassificationSetCheckSql);
    }
}
