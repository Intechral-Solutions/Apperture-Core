using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic76_OperationalTeamManagedContactMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers");

            migrationBuilder.AddColumn<string>(
                name: "ManagedContactId",
                table: "OperationalTeamMembers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeamMembers_ManagedContactId",
                table: "OperationalTeamMembers",
                column: "ManagedContactId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTeamMembers_MemberKind_ManagedContactId",
                table: "OperationalTeamMembers",
                columns: new[] { "MemberKind", "ManagedContactId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers",
                sql: "([MemberKind] = N'Principal' AND [PrincipalUserId] IS NOT NULL AND [EmailAddress] IS NULL AND [ManagedContactId] IS NULL) OR ([MemberKind] = N'ExternalEmail' AND [EmailAddress] IS NOT NULL AND [PrincipalUserId] IS NULL AND [ManagedContactId] IS NULL) OR ([MemberKind] = N'ManagedContact' AND [ManagedContactId] IS NOT NULL AND [PrincipalUserId] IS NULL AND [EmailAddress] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_OperationalTeamMembers_ManagedContacts_ManagedContactId",
                table: "OperationalTeamMembers",
                column: "ManagedContactId",
                principalTable: "ManagedContacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperationalTeamMembers_ManagedContacts_ManagedContactId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_OperationalTeamMembers_ManagedContactId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_OperationalTeamMembers_MemberKind_ManagedContactId",
                table: "OperationalTeamMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers");

            migrationBuilder.DropColumn(
                name: "ManagedContactId",
                table: "OperationalTeamMembers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OperationalTeamMembers_MemberKindPayload",
                table: "OperationalTeamMembers",
                sql: "([MemberKind] = N'Principal' AND [PrincipalUserId] IS NOT NULL) OR ([MemberKind] = N'ExternalEmail' AND [EmailAddress] IS NOT NULL)");
        }
    }
}
