using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Procedures;
using Apperture.Shared.Kernel;

namespace Sample.HelloWorld;

public sealed class HelloWorldModule : IModule, IModuleStepHandlerRegistrar, IOperationDefinitionProvider, IIntegrationConfigurationContributor
{
    private static readonly WorkflowDefinitionId DefinitionId = new("Sample.HelloWorld.HelloWorld");
    private static readonly OperationId OperationDefId = new("Sample.HelloWorld.DoWork");
    private const string ModuleId = "Sample.HelloWorld";

    public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Sample workflow: Validate → DoWork → Complete")
    {
        Permissions =
        [
            new ModulePermissionDeclaration(
                "workflow.execute", "WorkflowInstance",
                "Run Hello World workflows",
                "Run the workflows provided by the Hello World module.",
                PermissionSensitivity.Elevated),
            // Allow operation execution for this module's operations.
            new ModulePermissionDeclaration(
                ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition,
                "Run Hello World operations",
                "Run the operations provided by the Hello World module.",
                PermissionSensitivity.Elevated)
        ],
        Signals =
        [
            new ModuleSignalDeclaration(
                "demo.completed",
                "Demo completed",
                "Emitted when the HelloWorld Complete step runs (disable per-run with emitDemoPlatformEvent=false).",
                "info",
                RequiredDataProperties: ["message"])
        ],
        Procedures =
        [
            new ModuleProcedureDeclaration(
                "Sample.HelloWorld.procedures.greet",
                "Greet",
                "Runs HelloWorld with a name shown in operator output.",
                Category: "Demo",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                """{"fields":[{"key":"name","type":"string","required":true,"label":"Name","help":"Shown in the DoWork step output."}]}"""),
            new ModuleProcedureDeclaration(
                "Sample.HelloWorld.procedures.opPing",
                "Operation ping",
                "Starts the Sample.HelloWorld.DoWork operation with a message (procedure launch / provenance).",
                Category: "Demo",
                ProcedureLaunchKind.Operation,
                OperationDefId.Value,
                """{"fields":[{"key":"message","type":"string","required":true,"label":"Message","help":"Persisted on the operation start request."}]}""")
        ]
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "HelloWorld",
            ModuleId,
            new[] { "Validate", "DoWork", "Complete" },
            WorkflowCategory.Command,
            RequiredPermissions:
            [
                new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", $"module:{ModuleId}"))
            ]);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OperationDefinition(
            OperationDefId,
            // MVP contract: `OperationDefinition.Name` is used as the single handler "step name".
            "DoWork",
            ModuleId,
            OperationCategory.Command,
            ParameterSchema: """{"fields":[{"key":"message","type":"string","required":false}]}""",
            RequiredPermissions:
            [
                new PermissionDescriptor(
                    ActionConstants.OperationExecute,
                    new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Module(ModuleId)))
            ]);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        // Register handlers for every step declared by the workflow definition so the sample
        // truthfully exercises the worker-side step pipeline and step-instance recording.
        registry.Register(ModuleId, "Validate", new HelloWorldValidateStepHandler());
        registry.Register(ModuleId, "DoWork", new HelloWorldDoWorkStepHandler());
        registry.Register(ModuleId, "Complete", new HelloWorldCompleteStepHandler());
    }

    public IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions() =>
        HelloWorldIntegrationConfiguration.Settings;

    public IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements() =>
        HelloWorldIntegrationConfiguration.Connections;

    public IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() =>
        HelloWorldIntegrationConfiguration.Procedures;
}
