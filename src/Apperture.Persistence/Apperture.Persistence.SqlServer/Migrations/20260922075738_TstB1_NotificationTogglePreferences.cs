using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class TstB1_NotificationTogglePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ToastEnabledByDefault",
                table: "NotificationDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "NotificationUserPreferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationDefinitionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationUserPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationUserInboxItems_PrincipalUserId_CreatedAtUtc_Id",
                table: "NotificationUserInboxItems",
                columns: new[] { "PrincipalUserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel",
                table: "NotificationUserPreferences",
                columns: new[] { "UserId", "NotificationDefinitionId", "Channel" },
                unique: true);

            // Seed: flip the four built-in notification definitions to Toast-enabled-by-default.
            // Plain UPDATE is idempotent by construction (re-running sets 1 to 1 again).
            migrationBuilder.Sql(
                """
                UPDATE [NotificationDefinitions]
                SET [ToastEnabledByDefault] = 1
                WHERE [Id] IN (
                    N'platform.schedule.expected_run_missing',
                    N'module.sample.helloworld.demo.completed',
                    N'module.sample.fileimport.import.completed_with_errors',
                    N'module.sample.apipolling.sync.completed_with_errors');
                """);

            // Legacy opt-out conversion: historical self-service "Email off" overrides
            // (NotificationSubscriptionUserOverrides rows with OperationalTeamId IS NULL and
            // CreatedByActorId = UserId, i.e. a user's own self-service override, not an
            // operational-team-scoped one) today also suppress in-app delivery — a bug P2 fixes.
            // Convert each qualifying row into explicit email=false + toast=false preference rows
            // so those users don't suddenly start getting interruptive Toasts after upgrade, then
            // remove the superseded override rows. Both INSERTs run before the DELETE because they
            // both read from the same override rows. Each INSERT is guarded with NOT EXISTS so this
            // block is safe to re-run (e.g. a partially-applied prior run).
            migrationBuilder.Sql(
                """
                INSERT INTO [NotificationUserPreferences] ([Id],[UserId],[NotificationDefinitionId],[Channel],[IsEnabled],[CreatedAtUtc],[UpdatedAtUtc],[UpdatedByActorId])
                SELECT CONCAT(N'nupref_', LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''))),
                       o.[UserId], o.[NotificationDefinitionId], N'email', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), o.[CreatedByActorId]
                FROM [NotificationSubscriptionUserOverrides] o
                WHERE o.[OperationalTeamId] IS NULL AND o.[CreatedByActorId] = o.[UserId]
                  AND NOT EXISTS (
                      SELECT 1 FROM [NotificationUserPreferences] p
                      WHERE p.[UserId] = o.[UserId] AND p.[NotificationDefinitionId] = o.[NotificationDefinitionId] AND p.[Channel] = N'email');

                INSERT INTO [NotificationUserPreferences] ([Id],[UserId],[NotificationDefinitionId],[Channel],[IsEnabled],[CreatedAtUtc],[UpdatedAtUtc],[UpdatedByActorId])
                SELECT CONCAT(N'nupref_', LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''))),
                       o.[UserId], o.[NotificationDefinitionId], N'toast', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), o.[CreatedByActorId]
                FROM [NotificationSubscriptionUserOverrides] o
                WHERE o.[OperationalTeamId] IS NULL AND o.[CreatedByActorId] = o.[UserId]
                  AND NOT EXISTS (
                      SELECT 1 FROM [NotificationUserPreferences] p
                      WHERE p.[UserId] = o.[UserId] AND p.[NotificationDefinitionId] = o.[NotificationDefinitionId] AND p.[Channel] = N'toast');

                DELETE FROM [NotificationSubscriptionUserOverrides]
                WHERE [OperationalTeamId] IS NULL AND [CreatedByActorId] = [UserId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema rollback only. This migration also seeds Toast defaults and converts legacy
            // "Email off" override rows into NotificationUserPreferences rows (deleting the
            // originals in the process) as data migrations in Up(). Those data changes are lossy:
            // the deleted NotificationSubscriptionUserOverrides rows and the converted preference
            // rows cannot be reconstructed from the schema alone, so Down() intentionally does not
            // attempt to restore them (mirrors the repair-migration precedent in
            // EnsureOperationRunsExtendedColumns.cs). It only reverses the schema-changing calls
            // scaffolded above.
            migrationBuilder.DropTable(
                name: "NotificationUserPreferences");

            migrationBuilder.DropIndex(
                name: "IX_NotificationUserInboxItems_PrincipalUserId_CreatedAtUtc_Id",
                table: "NotificationUserInboxItems");

            migrationBuilder.DropColumn(
                name: "ToastEnabledByDefault",
                table: "NotificationDefinitions");
        }
    }
}
