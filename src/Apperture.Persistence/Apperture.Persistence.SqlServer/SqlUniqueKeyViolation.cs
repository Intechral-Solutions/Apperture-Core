using Microsoft.Data.SqlClient;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Recognises a SQL Server unique-key violation (2601 for a unique index, 2627 for a unique or primary key constraint) on one
/// named index. A caller that treats a duplicate as a concurrent request that committed first matches the index that expresses
/// that rule, so an unrelated duplicate (another unique index, a primary key, an audit event id) keeps its own meaning.
/// </summary>
public static class SqlUniqueKeyViolation
{
    public static bool IsOnIndex(Exception ex, string indexName)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql
                && sql.Number is 2601 or 2627
                && sql.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627
                    && error.Message.Contains($"'{indexName}'", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
