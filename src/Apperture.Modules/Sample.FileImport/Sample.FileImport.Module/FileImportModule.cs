using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public sealed class FileImportModule : IModule, IModuleStepHandlerRegistrar, IModuleIntegrationSettingDefinitionContributor
{
    public static readonly WorkflowDefinitionId DefinitionId = new("Sample.FileImport.Import");
    public const string ModuleIdValue = "Sample.FileImport";

    private readonly IImportOutcomeStore _store;

    public FileImportModule(IImportOutcomeStore? store = null) =>
        _store = store ?? new InMemoryImportOutcomeStore();

    public IImportOutcomeStore OutcomeStore => _store;

    public ModuleManifest GetManifest() => new(
        ModuleIdValue,
        "File Import",
        "1.0.0",
        "Sample file import: Validate → Parse → ProcessRows → Complete with process-local outcomes and durable platform operation items.")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
        ],
        Signals =
        [
            new ModuleSignalDeclaration(
                "import.completed",
                "Import completed",
                "Emitted when an import batch Succeeded with no failed rows.",
                "info",
                RequiredDataProperties:
                [
                    "fixtureId", "batchId", "totalRows", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
            new ModuleSignalDeclaration(
                "import.completed.with.errors",
                "Import completed with errors",
                "Emitted when an import batch CompletedWithErrors (partial success).",
                "warning",
                RequiredDataProperties:
                [
                    "fixtureId", "batchId", "totalRows", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
            new ModuleSignalDeclaration(
                "import.failed",
                "Import failed",
                "Emitted when an import batch Failed (including all-rows-failed or strict mode).",
                "error",
                RequiredDataProperties:
                [
                    "fixtureId", "batchId", "totalRows", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
        ],
        Procedures =
        [
            new ModuleProcedureDeclaration(
                "Sample.FileImport.procedures.import",
                "Import customers",
                "Imports an embedded customer CSV fixture and records module-local row outcomes.",
                Category: "Demo",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                """{"fields":[{"key":"fixtureId","type":"string","required":true,"label":"Fixture id","help":"One of: customers-valid, customers-mixed, customers-invalid-structure, customers-all-bad."},{"key":"strictMode","type":"boolean","required":false,"label":"Strict mode","help":"Optional override of strictModeDefault. When true, any row failure yields import.failed."}]}"""),
        ],
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "Import",
            ModuleIdValue,
            new[] { "Validate", "Parse", "ProcessRows", "Complete" });

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        registry.Register(ModuleIdValue, "Validate", new FileImportValidateStepHandler());
        registry.Register(ModuleIdValue, "Parse", new FileImportParseStepHandler(_store));
        registry.Register(ModuleIdValue, "ProcessRows", new FileImportProcessRowsStepHandler(_store));
        registry.Register(ModuleIdValue, "Complete", new FileImportCompleteStepHandler(_store));
    }

    public IReadOnlyList<PlatformSettingDefinition> GetIntegrationSettingDefinitions() =>
        FileImportIntegrationSettingDefinitions.All;
}
