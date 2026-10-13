using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Recognises the SQL Server unique-key violation (2601 or 2627) behind a failed save that means a concurrent request committed
/// the same durable request first. Only the named index or key that expresses that rule qualifies: any other duplicate keeps its
/// own meaning and propagates.
/// </summary>
internal static class SqlUniqueViolation
{
    /// <summary>The one cancel intent a workflow run can have.</summary>
    public const string WorkflowCancelIntent = "PK_WorkflowCancelRequests";

    /// <summary>The one cancel intent an operation run can have.</summary>
    public const string OperationCancelIntent = "PK_OperationCancelRequests";

    /// <summary>The one queued external control request of a kind a run can have.</summary>
    public const string QueuedExternalControl = "IX_WorkflowExternalControlRequests_RunId_ControlKind";

    /// <summary>The one queued control request of a kind a runtime can have.</summary>
    public const string QueuedRuntimeControl = "IX_RuntimeControlRequests_RuntimeId_ControlKind";

    public static bool IsOn(DbUpdateException ex, string indexName) => SqlUniqueKeyViolation.IsOnIndex(ex, indexName);

    /// <summary>Stops tracking rows whose save failed, leaving the rest of the context's pending changes alone.</summary>
    public static void Detach(DbContext db, params object[] rows)
    {
        foreach (var row in rows)
            db.Entry(row).State = EntityState.Detached;
    }
}
