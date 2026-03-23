using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPmiIntegrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnisaFileImportWatermarks",
                columns: table => new
                {
                    SourceKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastWriteTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnisaFileImportWatermarks", x => x.SourceKey);
                });

            migrationBuilder.CreateTable(
                name: "UnisaFundSourceCampusMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SchoolFundId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CampusId = table.Column<int>(type: "int", nullable: false),
                    FundSourceIdsCsv = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnisaFundSourceCampusMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnisaPaymentPlanArrangements",
                columns: table => new
                {
                    ArrangementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SchoolFundId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CnsStudentId = table.Column<int>(type: "int", nullable: true),
                    StudentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InSchoolPaymentPlanId = table.Column<int>(type: "int", nullable: true),
                    OutOfSchoolPaymentPlanId = table.Column<int>(type: "int", nullable: true),
                    SourceKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnisaPaymentPlanArrangements", x => x.ArrangementId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnisaFundSourceCampusMaps_SchoolFundId",
                table: "UnisaFundSourceCampusMaps",
                column: "SchoolFundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnisaPaymentPlanArrangements_ExternalKey_SchoolFundId_IsCurrent",
                table: "UnisaPaymentPlanArrangements",
                columns: new[] { "ExternalKey", "SchoolFundId", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnisaFileImportWatermarks");

            migrationBuilder.DropTable(
                name: "UnisaFundSourceCampusMaps");

            migrationBuilder.DropTable(
                name: "UnisaPaymentPlanArrangements");
        }
    }
}
