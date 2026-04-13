namespace Proof.CampusNexus.StudentReview;

public static class ProofCampusNexusStudentReviewConstants
{
    public const string ModuleId = "Proof.CampusNexus.StudentReview";

    public const string WorkflowDefinitionIdValue = "Proof.CampusNexus.StudentReview.SyncRecentStudents";

    /// <summary>Campus Nexus API client slot (contract: full name of <see cref="Apperture.Connectors.CampusNexus.ICampusNexusApiClient"/>).</summary>
    public const string SlotCnsApi = "CnsApi";

    /// <summary>Platform SQL slot for binding proof (same DB as Apperture; used for readiness + optional checks).</summary>
    public const string SlotIntegrationDb = "IntegrationDb";

    public const string ProcedureSyncRecentStudents = "Proof.CampusNexus.StudentReview.procedures.syncRecentStudents";
}

/// <summary>Reserved workflow step parameter keys injected by the worker (not accepted from API for security — overwritten server-side).</summary>
public static class ProofStudentReviewExecutionKeys
{
    public const string RunId = "__appertureRunId";

    public const string WorkflowInstanceId = "__appertureWorkflowInstanceId";
}
