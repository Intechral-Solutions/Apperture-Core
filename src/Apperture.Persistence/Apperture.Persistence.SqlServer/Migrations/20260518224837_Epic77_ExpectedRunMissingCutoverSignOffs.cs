using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_ExpectedRunMissingCutoverSignOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpectedRunMissingCutoverSignOffs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationDefinitionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReviewedByDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ParityAcceptableAtReview = table.Column<bool>(type: "bit", nullable: false),
                    ConfigurationWarningCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockingFindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NonBlockingFindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperatorNote = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectedRunMissingCutoverSignOffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedRunMissingCutoverSignOffs_IntegrationId_NotificationDefinitionId_ReviewedAtUtc",
                table: "ExpectedRunMissingCutoverSignOffs",
                columns: new[] { "IntegrationId", "NotificationDefinitionId", "ReviewedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpectedRunMissingCutoverSignOffs");
        }
    }
}
