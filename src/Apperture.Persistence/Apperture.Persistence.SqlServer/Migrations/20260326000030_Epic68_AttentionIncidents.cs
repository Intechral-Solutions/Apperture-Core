using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic68_AttentionIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttentionIncidents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConditionKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FirstExpectedFireAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastExpectedFireAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AcknowledgementNote = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionIncidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidents_IntegrationId_ConditionKey",
                table: "AttentionIncidents",
                columns: new[] { "IntegrationId", "ConditionKey" },
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidents_IntegrationId_ConditionKey_StartedAtUtc",
                table: "AttentionIncidents",
                columns: new[] { "IntegrationId", "ConditionKey", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidents_IntegrationId_ConditionKey_Status",
                table: "AttentionIncidents",
                columns: new[] { "IntegrationId", "ConditionKey", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttentionIncidents");
        }
    }
}
