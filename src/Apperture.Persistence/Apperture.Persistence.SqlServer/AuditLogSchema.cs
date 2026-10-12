using Apperture.Persistence.SqlServer.Migrations;

namespace Apperture.Persistence.SqlServer;

/// <summary>Names of the <c>AuditLogs</c> integrity guard and indexes added by <see cref="Aud1_AuditFoundation"/>.</summary>
public static class AuditLogSchema
{
    public const string UpdateGuardTrigger = "TR_AuditLogs_GuardUpdate";
    public const string DeleteGuardTrigger = "TR_AuditLogs_GuardDelete";
    public const string ProducerCheck = "CK_AuditLogs_Producer";
    public const string ClassificationSetCheck = "CK_AuditLogs_ClassificationSet";
    public const string EventIdIndex = "UX_AuditLogs_EventId";
    public const string UnclassifiedIndex = "IX_AuditLogs_Unclassified_Id";

    /// <summary>The error the update guard throws.</summary>
    public const int UpdateRefusedError = 51001;

    /// <summary>The error the delete guard throws.</summary>
    public const int DeleteRefusedError = 51002;

    public const string ProducerCheckSql = Aud1_AuditFoundation.ProducerCheckSql;

    /// <summary>The current classification check: the AUD-1.1 text with <c>binding</c> accepted as a target type (AUD-1.4 slice 4).</summary>
    public static readonly string ClassificationSetCheckSql = Aud14_BindingTargetType.ClassificationSetCheckSql;
}
