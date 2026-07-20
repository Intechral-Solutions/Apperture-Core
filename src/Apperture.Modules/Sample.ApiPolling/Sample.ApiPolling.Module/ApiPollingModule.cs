using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public sealed class ApiPollingModule : IModule, IModuleStepHandlerRegistrar, IModuleIntegrationSettingDefinitionContributor
{
    public static readonly WorkflowDefinitionId DefinitionId = new("Sample.ApiPolling.Poll");
    public const string ModuleIdValue = "Sample.ApiPolling";

    private readonly IApiPollingStateStore _store;
    private readonly ISampleDirectoryApiClient _client;

    public ApiPollingModule(
        IApiPollingStateStore? store = null,
        ISampleDirectoryApiClient? client = null)
    {
        _store = store ?? new InMemoryApiPollingStateStore();
        _client = client ?? new EmbeddedSampleDirectoryApiClient();
    }

    public IApiPollingStateStore StateStore => _store;

    public ISampleDirectoryApiClient ApiClient => _client;

    public ModuleManifest GetManifest() => new(
        ModuleIdValue,
        "API Polling",
        "1.0.0",
        "Sample API polling: Validate → Poll → Compare → ProcessChanges → Complete with process-local incremental state and durable platform operation items.")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
        ],
        Signals =
        [
            new ModuleSignalDeclaration(
                "sync.completed",
                "Sync completed",
                "Emitted when a poll Succeeded with no new/updated/deleted records and no failures.",
                "info",
                RequiredDataProperties:
                [
                    "snapshotId", "operationGroupId", "totalRecords", "newCount", "updatedCount",
                    "unchangedCount", "deletedCount", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
            new ModuleSignalDeclaration(
                "sync.completed.with.changes",
                "Sync completed with changes",
                "Emitted when a poll Succeeded with one or more new/updated/deleted records and no failures.",
                "info",
                RequiredDataProperties:
                [
                    "snapshotId", "operationGroupId", "totalRecords", "newCount", "updatedCount",
                    "unchangedCount", "deletedCount", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
            new ModuleSignalDeclaration(
                "sync.completed.with.errors",
                "Sync completed with errors",
                "Emitted when a poll CompletedWithErrors (partial success).",
                "warning",
                RequiredDataProperties:
                [
                    "snapshotId", "operationGroupId", "totalRecords", "newCount", "updatedCount",
                    "unchangedCount", "deletedCount", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
            new ModuleSignalDeclaration(
                "sync.failed",
                "Sync failed",
                "Emitted when a poll Failed (including all-records-failed or strict mode).",
                "error",
                RequiredDataProperties:
                [
                    "snapshotId", "operationGroupId", "totalRecords", "newCount", "updatedCount",
                    "unchangedCount", "deletedCount", "succeededCount", "failedCount", "skippedCount", "status",
                ]),
        ],
        Procedures =
        [
            new ModuleProcedureDeclaration(
                "Sample.ApiPolling.procedures.poll",
                "Poll directory snapshot",
                "Polls an embedded directory snapshot, compares against process-local state, and records durable operation items.",
                Category: "Demo",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                """{"fields":[{"key":"snapshotId","type":"string","required":true,"label":"Snapshot id","help":"One of: snapshot-initial, snapshot-changes, snapshot-errors, snapshot-deletions, snapshot-empty."},{"key":"resetState","type":"boolean","required":false,"label":"Reset state","help":"Clear process-local last-seen fingerprints for this integration before polling."},{"key":"strictMode","type":"boolean","required":false,"label":"Strict mode","help":"Optional override of strictModeDefault. When true, any record failure yields sync.failed."},{"key":"failOnRecordError","type":"boolean","required":false,"label":"Fail on record error","help":"Optional override. When true, stop after the first failed record and skip the rest."}]}"""),
        ],
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "Poll",
            ModuleIdValue,
            new[] { "Validate", "Poll", "Compare", "ProcessChanges", "Complete" });

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        registry.Register(ModuleIdValue, "Validate", new ApiPollingValidateStepHandler());
        registry.Register(ModuleIdValue, "Poll", new ApiPollingPollStepHandler(_store, _client));
        registry.Register(ModuleIdValue, "Compare", new ApiPollingCompareStepHandler(_store));
        registry.Register(ModuleIdValue, "ProcessChanges", new ApiPollingProcessChangesStepHandler(_store));
        registry.Register(ModuleIdValue, "Complete", new ApiPollingCompleteStepHandler(_store));
    }

    public IReadOnlyList<PlatformSettingDefinition> GetIntegrationSettingDefinitions() =>
        ApiPollingIntegrationSettingDefinitions.All;
}
