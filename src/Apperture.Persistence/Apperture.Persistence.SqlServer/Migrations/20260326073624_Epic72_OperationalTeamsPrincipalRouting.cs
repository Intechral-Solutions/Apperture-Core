using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic72_OperationalTeamsPrincipalRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings",
                newName: "PrimaryOperationalTeamId");

            migrationBuilder.RenameColumn(
                name: "EscalationContactGroupId",
                table: "IntegrationAttentionRoutings",
                newName: "EscalationOperationalTeamId");

            migrationBuilder.RenameIndex(
                name: "IX_IntegrationAttentionRoutings_PrimaryContactGroupId",
                table: "IntegrationAttentionRoutings",
                newName: "IX_IntegrationAttentionRoutings_PrimaryOperationalTeamId");

            migrationBuilder.RenameIndex(
                name: "IX_IntegrationAttentionRoutings_EscalationContactGroupId",
                table: "IntegrationAttentionRoutings",
                newName: "IX_IntegrationAttentionRoutings_EscalationOperationalTeamId");

            migrationBuilder.RenameColumn(
                name: "PrimaryContactGroupId",
                table: "InstanceAttentionRoutings",
                newName: "PrimaryOperationalTeamId");

            migrationBuilder.RenameColumn(
                name: "EscalationContactGroupId",
                table: "InstanceAttentionRoutings",
                newName: "EscalationOperationalTeamId");

            migrationBuilder.RenameIndex(
                name: "IX_InstanceAttentionRoutings_PrimaryContactGroupId",
                table: "InstanceAttentionRoutings",
                newName: "IX_InstanceAttentionRoutings_PrimaryOperationalTeamId");

            migrationBuilder.RenameIndex(
                name: "IX_InstanceAttentionRoutings_EscalationContactGroupId",
                table: "InstanceAttentionRoutings",
                newName: "IX_InstanceAttentionRoutings_EscalationOperationalTeamId");

            migrationBuilder.RenameColumn(
                name: "ContactGroupNameSnapshot",
                table: "AttentionIncidentNotifications",
                newName: "OperationalTeamNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "ContactGroupId",
                table: "AttentionIncidentNotifications",
                newName: "OperationalTeamId");

            migrationBuilder.AddColumn<string>(
                name: "EmailAddress",
                table: "Users",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperationalTeamMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationalTeamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MemberKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrincipalUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalTeamMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalTeams",
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
                    table.PrimaryKey("PK_OperationalTeams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeamMembers_MemberKind_PrincipalUserId",
                table: "OperationalTeamMembers",
                columns: new[] { "MemberKind", "PrincipalUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeamMembers_OperationalTeamId",
                table: "OperationalTeamMembers",
                column: "OperationalTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeams_Name",
                table: "OperationalTeams",
                column: "Name");

            migrationBuilder.Sql(
                """
                INSERT INTO OperationalTeams (Id, Name, Description, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT g.Id, g.Name, g.Description, g.IsEnabled, g.CreatedAtUtc, g.UpdatedAtUtc
                FROM AttentionContactGroups g
                WHERE NOT EXISTS (SELECT 1 FROM OperationalTeams t WHERE t.Id = g.Id);

                INSERT INTO OperationalTeamMembers (Id, OperationalTeamId, MemberKind, PrincipalUserId, EmailAddress, DisplayName, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
                SELECT m.Id, m.AttentionContactGroupId, N'ExternalEmail', NULL, m.EmailAddress, m.DisplayName, m.IsEnabled, m.CreatedAtUtc, m.UpdatedAtUtc
                FROM AttentionContactGroupMembers m
                WHERE NOT EXISTS (SELECT 1 FROM OperationalTeamMembers tm WHERE tm.Id = m.Id);
                """);

            migrationBuilder.DropTable(
                name: "AttentionContactGroupMembers");

            migrationBuilder.DropTable(
                name: "AttentionContactGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalTeamMembers");

            migrationBuilder.DropTable(
                name: "OperationalTeams");

            migrationBuilder.DropColumn(
                name: "EmailAddress",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "PrimaryOperationalTeamId",
                table: "IntegrationAttentionRoutings",
                newName: "PrimaryContactGroupId");

            migrationBuilder.RenameColumn(
                name: "EscalationOperationalTeamId",
                table: "IntegrationAttentionRoutings",
                newName: "EscalationContactGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_IntegrationAttentionRoutings_PrimaryOperationalTeamId",
                table: "IntegrationAttentionRoutings",
                newName: "IX_IntegrationAttentionRoutings_PrimaryContactGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_IntegrationAttentionRoutings_EscalationOperationalTeamId",
                table: "IntegrationAttentionRoutings",
                newName: "IX_IntegrationAttentionRoutings_EscalationContactGroupId");

            migrationBuilder.RenameColumn(
                name: "PrimaryOperationalTeamId",
                table: "InstanceAttentionRoutings",
                newName: "PrimaryContactGroupId");

            migrationBuilder.RenameColumn(
                name: "EscalationOperationalTeamId",
                table: "InstanceAttentionRoutings",
                newName: "EscalationContactGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_InstanceAttentionRoutings_PrimaryOperationalTeamId",
                table: "InstanceAttentionRoutings",
                newName: "IX_InstanceAttentionRoutings_PrimaryContactGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_InstanceAttentionRoutings_EscalationOperationalTeamId",
                table: "InstanceAttentionRoutings",
                newName: "IX_InstanceAttentionRoutings_EscalationContactGroupId");

            migrationBuilder.RenameColumn(
                name: "OperationalTeamNameSnapshot",
                table: "AttentionIncidentNotifications",
                newName: "ContactGroupNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "OperationalTeamId",
                table: "AttentionIncidentNotifications",
                newName: "ContactGroupId");

            migrationBuilder.CreateTable(
                name: "AttentionContactGroupMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AttentionContactGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionContactGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttentionContactGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionContactGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionContactGroupMembers_AttentionContactGroupId",
                table: "AttentionContactGroupMembers",
                column: "AttentionContactGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionContactGroups_Name",
                table: "AttentionContactGroups",
                column: "Name");
        }
    }
}
