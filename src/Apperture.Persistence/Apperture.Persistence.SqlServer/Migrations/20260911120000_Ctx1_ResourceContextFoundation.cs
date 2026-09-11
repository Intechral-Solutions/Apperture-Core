using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// ResourceContext foundation: the ownership namespace dimension, the reserved Shared context, and
/// required ownership on the three context-owned resource tables.
/// </summary>
/// <remarks>
/// <para>
/// Ordering matters. The table is created and the reserved Shared row inserted <i>before</i> the
/// ownership columns are backfilled, because the backfill and the foreign keys both depend on that row
/// existing. Seeding inside the migration rather than from a hosted service avoids a startup race
/// against a backfill that requires the row; several existing migrations already seed data this way.
/// </para>
/// <para>
/// Columns are added nullable, backfilled to <c>'shared'</c>, then altered to required. No operator
/// classification is required during upgrade and no existing resource id is rewritten: every
/// pre-existing Integration, Connection and ScheduleDefinition simply becomes owned by Shared, which is
/// a true statement about the present rather than an invented history.
/// </para>
/// <para>
/// The real foreign keys deviate from this codebase's usual string correlation between operator
/// aggregates. That convention avoids coupling aggregate lifecycles; ResourceContexts is a small
/// ownership dimension, and the restrictive FK buys a structural guarantee that a context owning
/// resources cannot be deleted out from under them. Supported product behavior never deletes a
/// ResourceContext at all -- retirement is <c>State = 'Archived'</c> -- so the FK is backup, not the
/// primary rule.
/// </para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260911120000_Ctx1_ResourceContextFoundation")]
public partial class Ctx1_ResourceContextFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. The ownership dimension.
        migrationBuilder.CreateTable(
            name: "ResourceContexts",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                IsReserved = table.Column<bool>(type: "bit", nullable: false),
                Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResourceContexts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_ResourceContexts_Slug",
            table: "ResourceContexts",
            column: "Slug",
            unique: true);

        // 2. The single reserved Shared context. The literal id is intentional and matches Apperture's
        //    existing well-known string identifiers; it lets step 4 backfill deterministically.
        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM [ResourceContexts] WHERE [Id] = N'shared')
            BEGIN
                INSERT INTO [ResourceContexts]
                    ([Id], [Name], [Slug], [Kind], [State], [IsReserved], [Description], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES
                    (N'shared', N'Shared', N'shared', N'Shared', N'Active', 1, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            END
            """);

        // 3. Ownership columns, nullable for the moment.
        migrationBuilder.AddColumn<string>(
            name: "ResourceContextId",
            table: "Integrations",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResourceContextId",
            table: "Connections",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResourceContextId",
            table: "ScheduleDefinitions",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        // 4. Backfill every existing row to Shared. No operator classification during upgrade.
        migrationBuilder.Sql("UPDATE [Integrations] SET [ResourceContextId] = N'shared' WHERE [ResourceContextId] IS NULL;");
        migrationBuilder.Sql("UPDATE [Connections] SET [ResourceContextId] = N'shared' WHERE [ResourceContextId] IS NULL;");
        migrationBuilder.Sql("UPDATE [ScheduleDefinitions] SET [ResourceContextId] = N'shared' WHERE [ResourceContextId] IS NULL;");

        // 5. Ownership is required from here on.
        migrationBuilder.AlterColumn<string>(
            name: "ResourceContextId",
            table: "Integrations",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "ResourceContextId",
            table: "Connections",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "ResourceContextId",
            table: "ScheduleDefinitions",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);

        // 6. Indexes for context-filtered queries, and the referential guard.
        migrationBuilder.CreateIndex(
            name: "IX_Integrations_ResourceContextId",
            table: "Integrations",
            column: "ResourceContextId");

        migrationBuilder.CreateIndex(
            name: "IX_Connections_ResourceContextId",
            table: "Connections",
            column: "ResourceContextId");

        migrationBuilder.CreateIndex(
            name: "IX_ScheduleDefinitions_ResourceContextId",
            table: "ScheduleDefinitions",
            column: "ResourceContextId");

        migrationBuilder.AddForeignKey(
            name: "FK_Integrations_ResourceContexts_ResourceContextId",
            table: "Integrations",
            column: "ResourceContextId",
            principalTable: "ResourceContexts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Connections_ResourceContexts_ResourceContextId",
            table: "Connections",
            column: "ResourceContextId",
            principalTable: "ResourceContexts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ScheduleDefinitions_ResourceContexts_ResourceContextId",
            table: "ScheduleDefinitions",
            column: "ResourceContextId",
            principalTable: "ResourceContexts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ScheduleDefinitions_ResourceContexts_ResourceContextId",
            table: "ScheduleDefinitions");

        migrationBuilder.DropForeignKey(
            name: "FK_Connections_ResourceContexts_ResourceContextId",
            table: "Connections");

        migrationBuilder.DropForeignKey(
            name: "FK_Integrations_ResourceContexts_ResourceContextId",
            table: "Integrations");

        migrationBuilder.DropIndex(
            name: "IX_ScheduleDefinitions_ResourceContextId",
            table: "ScheduleDefinitions");

        migrationBuilder.DropIndex(
            name: "IX_Connections_ResourceContextId",
            table: "Connections");

        migrationBuilder.DropIndex(
            name: "IX_Integrations_ResourceContextId",
            table: "Integrations");

        migrationBuilder.DropColumn(
            name: "ResourceContextId",
            table: "ScheduleDefinitions");

        migrationBuilder.DropColumn(
            name: "ResourceContextId",
            table: "Connections");

        migrationBuilder.DropColumn(
            name: "ResourceContextId",
            table: "Integrations");

        migrationBuilder.DropIndex(
            name: "UX_ResourceContexts_Slug",
            table: "ResourceContexts");

        migrationBuilder.DropTable(
            name: "ResourceContexts");
    }
}
