using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic71_ContactGroupsAndRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttentionContactGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionContactGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttentionContactGroupMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AttentionContactGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionContactGroupMembers", x => x.Id);
                });

            migrationBuilder.AddColumn<string>(
                name: "EscalationContactGroupId",
                table: "IntegrationAttentionRoutings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationContactGroupId",
                table: "InstanceAttentionRoutings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactGroupId",
                table: "InstanceAttentionRoutings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactGroupId",
                table: "AttentionIncidentNotifications",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactGroupNameSnapshot",
                table: "AttentionIncidentNotifications",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttentionContactGroupMembers_AttentionContactGroupId",
                table: "AttentionContactGroupMembers",
                column: "AttentionContactGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionContactGroups_Name",
                table: "AttentionContactGroups",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationAttentionRoutings_EscalationContactGroupId",
                table: "IntegrationAttentionRoutings",
                column: "EscalationContactGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationAttentionRoutings_PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings",
                column: "PrimaryContactGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceAttentionRoutings_EscalationContactGroupId",
                table: "InstanceAttentionRoutings",
                column: "EscalationContactGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceAttentionRoutings_PrimaryContactGroupId",
                table: "InstanceAttentionRoutings",
                column: "PrimaryContactGroupId");

            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();

                DECLARE @InstMap TABLE (
                    RoutingId nvarchar(64) NOT NULL,
                    Role nvarchar(32) NOT NULL,
                    GroupId nvarchar(64) NOT NULL,
                    PRIMARY KEY (RoutingId, Role)
                );

                INSERT INTO @InstMap (RoutingId, Role, GroupId)
                SELECT DISTINCT ar.InstanceAttentionRoutingId, ar.Role,
                    N'attncg_mig_' + LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), N'-', N''))
                FROM AttentionRoutingRecipients ar
                WHERE ar.InstanceAttentionRoutingId IS NOT NULL;

                INSERT INTO AttentionContactGroups (Id, Name, Description, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT m.GroupId,
                    N'Instance ' + m.RoutingId + N' — ' + CASE m.Role
                        WHEN N'Primary' THEN N'Primary'
                        WHEN N'Escalation' THEN N'Escalation'
                        ELSE m.Role END,
                    N'Migrated from AttentionRoutingRecipients',
                    CAST(1 AS bit),
                    @now,
                    @now
                FROM @InstMap m;

                INSERT INTO AttentionContactGroupMembers (Id, AttentionContactGroupId, EmailAddress, DisplayName, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT N'attnmem_mig_' + LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), N'-', N'')),
                    m.GroupId,
                    ar.Address,
                    NULL,
                    CAST(1 AS bit),
                    @now,
                    @now
                FROM AttentionRoutingRecipients ar
                INNER JOIN @InstMap m ON m.RoutingId = ar.InstanceAttentionRoutingId AND m.Role = ar.Role;

                UPDATE iar SET PrimaryContactGroupId = m.GroupId
                FROM InstanceAttentionRoutings iar
                INNER JOIN @InstMap m ON m.RoutingId = iar.Id AND m.Role = N'Primary';

                UPDATE iar SET EscalationContactGroupId = m.GroupId
                FROM InstanceAttentionRoutings iar
                INNER JOIN @InstMap m ON m.RoutingId = iar.Id AND m.Role = N'Escalation';

                DECLARE @IntMap TABLE (
                    RoutingId nvarchar(64) NOT NULL,
                    Role nvarchar(32) NOT NULL,
                    GroupId nvarchar(64) NOT NULL,
                    PRIMARY KEY (RoutingId, Role)
                );

                INSERT INTO @IntMap (RoutingId, Role, GroupId)
                SELECT DISTINCT ar.IntegrationAttentionRoutingId, ar.Role,
                    N'attncg_mig_' + LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), N'-', N''))
                FROM AttentionRoutingRecipients ar
                WHERE ar.IntegrationAttentionRoutingId IS NOT NULL;

                INSERT INTO AttentionContactGroups (Id, Name, Description, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT m.GroupId,
                    N'Integration routing ' + m.RoutingId + N' — ' + CASE m.Role
                        WHEN N'Primary' THEN N'Primary'
                        WHEN N'Escalation' THEN N'Escalation'
                        ELSE m.Role END,
                    N'Migrated from AttentionRoutingRecipients',
                    CAST(1 AS bit),
                    @now,
                    @now
                FROM @IntMap m;

                INSERT INTO AttentionContactGroupMembers (Id, AttentionContactGroupId, EmailAddress, DisplayName, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT N'attnmem_mig_' + LOWER(REPLACE(CAST(NEWID() AS nvarchar(36)), N'-', N'')),
                    m.GroupId,
                    ar.Address,
                    NULL,
                    CAST(1 AS bit),
                    @now,
                    @now
                FROM AttentionRoutingRecipients ar
                INNER JOIN @IntMap m ON m.RoutingId = ar.IntegrationAttentionRoutingId AND m.Role = ar.Role;

                UPDATE ir SET PrimaryContactGroupId = m.GroupId
                FROM IntegrationAttentionRoutings ir
                INNER JOIN @IntMap m ON m.RoutingId = ir.Id AND m.Role = N'Primary';

                UPDATE ir SET EscalationContactGroupId = m.GroupId
                FROM IntegrationAttentionRoutings ir
                INNER JOIN @IntMap m ON m.RoutingId = ir.Id AND m.Role = N'Escalation';
                """);

            migrationBuilder.DropTable(
                name: "AttentionRoutingRecipients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttentionContactGroupMembers");

            migrationBuilder.DropTable(
                name: "AttentionContactGroups");

            migrationBuilder.DropIndex(
                name: "IX_IntegrationAttentionRoutings_EscalationContactGroupId",
                table: "IntegrationAttentionRoutings");

            migrationBuilder.DropIndex(
                name: "IX_IntegrationAttentionRoutings_PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings");

            migrationBuilder.DropIndex(
                name: "IX_InstanceAttentionRoutings_EscalationContactGroupId",
                table: "InstanceAttentionRoutings");

            migrationBuilder.DropIndex(
                name: "IX_InstanceAttentionRoutings_PrimaryContactGroupId",
                table: "InstanceAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "EscalationContactGroupId",
                table: "IntegrationAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "EscalationContactGroupId",
                table: "InstanceAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "PrimaryContactGroupId",
                table: "InstanceAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "ContactGroupId",
                table: "AttentionIncidentNotifications");

            migrationBuilder.DropColumn(
                name: "ContactGroupNameSnapshot",
                table: "AttentionIncidentNotifications");

            migrationBuilder.CreateTable(
                name: "AttentionRoutingRecipients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    InstanceAttentionRoutingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IntegrationAttentionRoutingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionRoutingRecipients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionRoutingRecipients_InstanceAttentionRoutingId",
                table: "AttentionRoutingRecipients",
                column: "InstanceAttentionRoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionRoutingRecipients_IntegrationAttentionRoutingId",
                table: "AttentionRoutingRecipients",
                column: "IntegrationAttentionRoutingId");
        }
    }
}
