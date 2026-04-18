using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Proof.CampusNexus.StudentReview.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameProofStudentReviewCnsTimestampColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DateLstMod",
                table: "ProofStudentReviewSnapshots",
                newName: "LastModifiedDateTime");

            migrationBuilder.RenameColumn(
                name: "DateAdded",
                table: "ProofStudentReviewSnapshots",
                newName: "CreatedDateTime");

            migrationBuilder.RenameColumn(
                name: "MaxDateLstModSeen",
                table: "ProofStudentReviewRuns",
                newName: "MaxLastModifiedDateTimeSeen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastModifiedDateTime",
                table: "ProofStudentReviewSnapshots",
                newName: "DateLstMod");

            migrationBuilder.RenameColumn(
                name: "CreatedDateTime",
                table: "ProofStudentReviewSnapshots",
                newName: "DateAdded");

            migrationBuilder.RenameColumn(
                name: "MaxLastModifiedDateTimeSeen",
                table: "ProofStudentReviewRuns",
                newName: "MaxDateLstModSeen");
        }
    }
}
