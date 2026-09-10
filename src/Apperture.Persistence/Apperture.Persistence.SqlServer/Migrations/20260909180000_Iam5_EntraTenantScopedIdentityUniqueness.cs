using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Entra identity uniqueness is tenant + object id, not object id alone.
/// Local uniqueness stays source + external id. Registration id is provenance, not an identity namespace.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260909180000_Iam5_EntraTenantScopedIdentityUniqueness")]
public partial class Iam5_EntraTenantScopedIdentityUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IdentitySource.Entra = 0, IdentitySource.Local = 1. Those numeric values are a persisted contract.
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM IdentityLinks
                WHERE [Source] = 0
                  AND ([TenantId] IS NULL OR LTRIM(RTRIM([TenantId])) = N''))
            BEGIN
                DECLARE @missing int =
                (
                    SELECT COUNT(*)
                    FROM IdentityLinks
                    WHERE [Source] = 0
                      AND ([TenantId] IS NULL OR LTRIM(RTRIM([TenantId])) = N'')
                );
                DECLARE @msg nvarchar(4000) =
                    CONCAT(
                        N'Cannot apply tenant-scoped Entra identity uniqueness: ',
                        @missing,
                        N' Entra IdentityLink row(s) have no TenantId. Tenant ids are not invented during upgrade. Complete Microsoft authentication for those identities on the previous schema, or remove the invalid Entra links, then retry. User ids, proof timestamps, Recovery Access, local credentials, account action tokens, and access requests are not rewritten by this migration.');
                THROW 50001, @msg, 1;
            END
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM IdentityLinks
                WHERE [Source] = 0
                  AND [TenantId] IS NOT NULL
                GROUP BY [TenantId], [ExternalId]
                HAVING COUNT(*) > 1)
            BEGIN
                THROW 50002, N'Cannot apply tenant-scoped Entra identity uniqueness: duplicate Entra IdentityLink rows exist for the same TenantId and object id. Resolve the duplicates, then retry. This migration does not merge users or rewrite identity links.', 1;
            END
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM EntraAccessRequests
                WHERE [Status] = 0
                GROUP BY [TenantId], [ProviderObjectId]
                HAVING COUNT(*) > 1)
            BEGIN
                THROW 50003, N'Cannot apply tenant-scoped access-request uniqueness: more than one pending Entra access request exists for the same TenantId and object id. Resolve the duplicates, then retry.', 1;
            END
            """);

        migrationBuilder.DropIndex(
            name: "IX_IdentityLinks_Source_ExternalId",
            table: "IdentityLinks");

        migrationBuilder.AddCheckConstraint(
            name: "CK_IdentityLinks_EntraRequiresTenant",
            table: "IdentityLinks",
            sql: "[Source] <> 0 OR ([TenantId] IS NOT NULL AND DATALENGTH([TenantId]) > 0)");

        migrationBuilder.CreateIndex(
            name: "UX_IdentityLinks_Local_Source_ExternalId",
            table: "IdentityLinks",
            columns: new[] { "Source", "ExternalId" },
            unique: true,
            filter: "[Source] = 1");

        migrationBuilder.CreateIndex(
            name: "UX_IdentityLinks_Entra_Source_TenantId_ExternalId",
            table: "IdentityLinks",
            columns: new[] { "Source", "TenantId", "ExternalId" },
            unique: true,
            filter: "[Source] = 0 AND [TenantId] IS NOT NULL");

        migrationBuilder.DropIndex(
            name: "UX_EntraAccessRequests_PendingIdentity",
            table: "EntraAccessRequests");

        migrationBuilder.CreateIndex(
            name: "UX_EntraAccessRequests_PendingTenantIdentity",
            table: "EntraAccessRequests",
            columns: new[] { "TenantId", "ProviderObjectId" },
            unique: true,
            filter: "[Status] = 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_EntraAccessRequests_PendingTenantIdentity",
            table: "EntraAccessRequests");

        migrationBuilder.CreateIndex(
            name: "UX_EntraAccessRequests_PendingIdentity",
            table: "EntraAccessRequests",
            columns: new[] { "RegistrationId", "TenantId", "ProviderObjectId" },
            unique: true,
            filter: "[Status] = 0");

        migrationBuilder.DropIndex(
            name: "UX_IdentityLinks_Entra_Source_TenantId_ExternalId",
            table: "IdentityLinks");

        migrationBuilder.DropIndex(
            name: "UX_IdentityLinks_Local_Source_ExternalId",
            table: "IdentityLinks");

        migrationBuilder.DropCheckConstraint(
            name: "CK_IdentityLinks_EntraRequiresTenant",
            table: "IdentityLinks");

        migrationBuilder.CreateIndex(
            name: "IX_IdentityLinks_Source_ExternalId",
            table: "IdentityLinks",
            columns: new[] { "Source", "ExternalId" },
            unique: true);
    }
}
