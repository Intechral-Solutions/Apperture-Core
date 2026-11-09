using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The native MFA service as production composes it over one context (IAM-10.2b WP4B-C): the audited unit, the global administrator-set
/// gate and the viability reads it takes for a change to an authenticator or its codes, the writer of a refused reset or replacement, the
/// user rows it names the account by, and the replacement candidates. Unless a coordinator is given, the viability reads trust Local sign-in
/// as the host reads it, which is what the persistence suites built on this service need: their operations add, or change an account that is
/// not the Recovery Access account.
/// </summary>
internal static class NativeMfaComposition
{
    /// <param name="refusalWriterOptions">The catalog the refusal writer writes to; by default the catalog of <paramref name="db"/>.</param>
    public static NativeMfaService Over(
        AppertureDbContext db,
        DbContextOptions<AppertureDbContext>? refusalWriterOptions,
        ISecretProtector protector,
        IAuditedUnitOfWork? unit = null,
        IAdministratorViabilityCoordinator? coordinator = null,
        TimeProvider? time = null) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            protector,
            time ?? TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            unit ?? NewUnit(db, null),
            coordinator ?? RoleAuthorityWorld.Viability(db),
            new SqlAuditEventWriter(refusalWriterOptions ?? OptionsOf(db), null, HarnessCatalog, null, null, null),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlNativeMfaReplacementCandidateRepository(db));

    /// <summary>A context of its own on the catalog of <paramref name="db"/> (the connection string it was configured with).</summary>
    private static DbContextOptions<AppertureDbContext> OptionsOf(AppertureDbContext db) =>
        new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(db.Database.GetConnectionString()!)
            .Options;
}
