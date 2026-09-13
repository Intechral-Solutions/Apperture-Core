using Microsoft.EntityFrameworkCore;

namespace Apperture.RuntimePlane.Persistence.SqlServer;

/// <summary>
/// Runtime-plane DbContext; thin extension of shared Apperture.Persistence.SqlServer.AppertureDbContext.
/// Prefer referencing Apperture.Persistence.SqlServer directly for new code.
/// </summary>
public class AppertureDbContext : Apperture.Persistence.SqlServer.AppertureDbContext
{
    public AppertureDbContext(DbContextOptions<AppertureDbContext> options)
        // EF Core typed options are not covariant across derived DbContext types; cast is the supported workaround.
        : base((DbContextOptions<Apperture.Persistence.SqlServer.AppertureDbContext>)(object)options)
    { }
}
