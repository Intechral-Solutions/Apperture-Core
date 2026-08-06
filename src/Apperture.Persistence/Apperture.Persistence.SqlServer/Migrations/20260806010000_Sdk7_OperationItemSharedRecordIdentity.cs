using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260806010000_Sdk7_OperationItemSharedRecordIdentity")]
public partial class Sdk7_OperationItemSharedRecordIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SharedRecordType",
            table: "OperationItems",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SharedBusinessKey",
            table: "OperationItems",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        // Best-effort backfill from legacy metadata JSON (recordType + businessKey).
        // Rows without parseable metadata remain null and are still readable via metadata fallback.
        migrationBuilder.Sql("""
            UPDATE oi
            SET
                SharedRecordType = LOWER(LTRIM(RTRIM(JSON_VALUE(oi.MetadataJson, '$.recordType')))),
                SharedBusinessKey = LTRIM(RTRIM(COALESCE(
                    JSON_VALUE(oi.MetadataJson, '$.businessKey'),
                    oi.SubjectKey)))
            FROM OperationItems oi
            WHERE oi.MetadataJson IS NOT NULL
              AND JSON_VALUE(oi.MetadataJson, '$.recordType') IS NOT NULL
              AND LTRIM(RTRIM(JSON_VALUE(oi.MetadataJson, '$.recordType'))) <> ''
              AND COALESCE(JSON_VALUE(oi.MetadataJson, '$.businessKey'), oi.SubjectKey) IS NOT NULL
              AND LTRIM(RTRIM(COALESCE(JSON_VALUE(oi.MetadataJson, '$.businessKey'), oi.SubjectKey))) <> '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_OperationItems_SharedRecordType_SharedBusinessKey_CompletedAtUtc",
            table: "OperationItems",
            columns: new[] { "SharedRecordType", "SharedBusinessKey", "CompletedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OperationItems_SharedRecordType_SharedBusinessKey_CompletedAtUtc",
            table: "OperationItems");

        migrationBuilder.DropColumn(name: "SharedRecordType", table: "OperationItems");
        migrationBuilder.DropColumn(name: "SharedBusinessKey", table: "OperationItems");
    }
}
