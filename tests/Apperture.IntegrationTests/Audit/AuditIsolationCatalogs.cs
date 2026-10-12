using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Two migrated throwaway catalogs for a class fixture: one with the default locking READ COMMITTED, one with
/// READ_COMMITTED_SNAPSHOT on. A suite shares them across the cases that scope their assertions to rows of their own; a case that
/// needs an empty audit table (the audit table is append-only) takes a catalog of its own through <see cref="AuditIsolation"/>.
/// </summary>
public sealed class AuditIsolationCatalogs : IAsyncLifetime
{
    public AuditSqlScratch Locking { get; private set; } = null!;
    public AuditSqlScratch Rcsi { get; private set; } = null!;

    public AuditSqlScratch For(bool rcsi) => rcsi ? Rcsi : Locking;

    public async Task InitializeAsync()
    {
        Locking = await AuditIsolation.CreateAsync("Apperture_Aud14Shared", rcsi: false);
        Rcsi = await AuditIsolation.CreateAsync("Apperture_Aud14Shared", rcsi: true);
    }

    public async Task DisposeAsync()
    {
        await Locking.DisposeAsync();
        await Rcsi.DisposeAsync();
    }
}
