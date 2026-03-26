using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic72A_OperationalTeamHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeamMembers_PrincipalUserId",
                table: "OperationalTeamMembers",
                column: "PrincipalUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers",
                sql: "([MemberKind] = N'Principal' AND [PrincipalUserId] IS NOT NULL) OR ([MemberKind] = N'ExternalEmail' AND [EmailAddress] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_OperationalTeamMembers_OperationalTeams_OperationalTeamId",
                table: "OperationalTeamMembers",
                column: "OperationalTeamId",
                principalTable: "OperationalTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OperationalTeamMembers_Users_PrincipalUserId",
                table: "OperationalTeamMembers",
                column: "PrincipalUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperationalTeamMembers_OperationalTeams_OperationalTeamId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_OperationalTeamMembers_Users_PrincipalUserId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_OperationalTeamMembers_PrincipalUserId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers");
        }
    }
}
