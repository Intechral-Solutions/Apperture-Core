using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proof.CampusNexus.StudentReview.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProofStudentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProofStudentReviewRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetrievedCount = table.Column<int>(type: "int", nullable: false),
                    InsertedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    MaxDateLstModSeen = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EntitySetUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ODataQueryUsed = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProofStudentReviewRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProofStudentReviewSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CnsStudentId = table.Column<int>(type: "int", nullable: false),
                    StudentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DateAdded = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DateLstMod = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFetchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceRunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastCorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProofStudentReviewSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProofStudentReviewRuns_IntegrationId",
                table: "ProofStudentReviewRuns",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProofStudentReviewRuns_RunId",
                table: "ProofStudentReviewRuns",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProofStudentReviewSnapshots_IntegrationId_CnsStudentId",
                table: "ProofStudentReviewSnapshots",
                columns: new[] { "IntegrationId", "CnsStudentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProofStudentReviewRuns");

            migrationBuilder.DropTable(
                name: "ProofStudentReviewSnapshots");
        }
    }
}
