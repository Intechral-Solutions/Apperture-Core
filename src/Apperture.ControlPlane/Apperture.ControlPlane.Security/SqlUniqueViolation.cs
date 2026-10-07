using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>Recognises a SQL Server unique-key violation (2601 or 2627) behind a failed save.</summary>
internal static class SqlUniqueViolation
{
    public static bool Is(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        }

        return false;
    }

    /// <summary>Stops tracking rows whose save failed, leaving the rest of the context's pending changes alone.</summary>
    public static void Detach(DbContext db, params object[] rows)
    {
        foreach (var row in rows)
            db.Entry(row).State = EntityState.Detached;
    }
}
