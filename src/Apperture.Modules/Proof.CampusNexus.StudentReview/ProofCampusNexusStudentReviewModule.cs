using Apperture.Core.Application.Execution;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Proof.CampusNexus.StudentReview;

public sealed class ProofCampusNexusStudentReviewModule : IModule, IModuleStepHandlerRegistrar
{
    private static readonly WorkflowDefinitionId DefinitionId = new(ProofCampusNexusStudentReviewConstants.WorkflowDefinitionIdValue);
    private readonly ILoggerFactory? _loggerFactory;

    public ProofCampusNexusStudentReviewModule(ILoggerFactory? loggerFactory = null) =>
        _loggerFactory = loggerFactory;

    public ModuleManifest GetManifest() => new(
        ProofCampusNexusStudentReviewConstants.ModuleId,
        "Proof: CampusNexus Student Review",
        "1.0.0",
        "Epic 74 — fetches recently modified students via Campus Nexus OData and persists review rows to the integration-bound SQL database (IntegrationDb slot).")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
            new ModulePermissionDeclaration(ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition)
        ],
        Procedures =
        [
            new ModuleProcedureDeclaration(
                ProofCampusNexusStudentReviewConstants.ProcedureSyncRecentStudents,
                "Sync recent CNS students (proof)",
                "Runs the Epic 74 proof workflow against the selected integration (requires CNS + SQL bindings).",
                Category: "Proof",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                """{"fields":[{"key":"entitySet","type":"string","required":false,"label":"OData entity set","help":"Default Students. Confirm against tenant $metadata."},{"key":"odataOrderBy","type":"string","required":false,"label":"$orderby","help":"Default LastModifiedDateTime desc"},{"key":"odataTop","type":"string","required":false,"label":"$top","help":"Default 10 (max 100)"},{"key":"odataSelect","type":"string","required":false,"label":"$select","help":"Comma-separated fields; defaults to Id,StudentNumber,FirstName,LastName,CreatedDateTime,LastModifiedDateTime"}]}""")
        ]
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "SyncRecentStudents",
            ProofCampusNexusStudentReviewConstants.ModuleId,
            new[] { "Validate", "SyncRecentStudents", "Complete" },
            WorkflowCategory.Command,
            RequiredPermissions:
            [
                new PermissionDescriptor(
                    "workflow.execute",
                    new ResourceDescriptor("WorkflowInstance", $"module:{ProofCampusNexusStudentReviewConstants.ModuleId}"))
            ]);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        var mid = ProofCampusNexusStudentReviewConstants.ModuleId;
        var syncLogger = _loggerFactory?.CreateLogger<SyncRecentStudentsStepHandler>()
            ?? NullLogger<SyncRecentStudentsStepHandler>.Instance;
        registry.Register(mid, "Validate", new ValidateProofStudentReviewStepHandler());
        registry.Register(mid, "SyncRecentStudents", new SyncRecentStudentsStepHandler(syncLogger));
        registry.Register(mid, "Complete", new CompleteProofStudentReviewStepHandler());
    }
}
