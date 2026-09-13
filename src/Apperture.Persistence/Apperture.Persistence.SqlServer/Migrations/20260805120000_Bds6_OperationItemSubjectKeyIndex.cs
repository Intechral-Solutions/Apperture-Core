using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260805120000_Bds6_OperationItemSubjectKeyIndex")]
public partial class Bds6_OperationItemSubjectKeyIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_OperationItems_SubjectKey",
            table: "OperationItems",
            column: "SubjectKey");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OperationItems_SubjectKey",
            table: "OperationItems");
    }
}
