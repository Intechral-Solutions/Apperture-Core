using Apperture.Core.Application.Execution;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace Unisa.CampusNexus.PaymentPlans;

public sealed class UnisaCampusNexusPaymentPlansModule : IModule, IModuleStepHandlerRegistrar, IIntegrationConfigurationContributor
{
    private static readonly WorkflowDefinitionId DefinitionId = new("Unisa.CampusNexus.PaymentPlans.ProcessDailyFiles");
    private readonly IServiceScopeFactory? _scopeFactory;

    /// <param name="scopeFactory">Required on the worker for step handlers; omit on the control plane (manifest-only).</param>
    public UnisaCampusNexusPaymentPlansModule(IServiceScopeFactory? scopeFactory = null) =>
        _scopeFactory = scopeFactory;

    public ModuleManifest GetManifest() => new(
        UnisaCampusNexusPaymentPlansConstants.ModuleId,
        "UNISA Campus Nexus payment plans",
        "1.0.0",
        "UNISA payment plan integration with Campus Nexus (eProm / TRANS). Vendor-specific profiles (e.g. PMI) are configuration, not separate modules.")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
            new ModulePermissionDeclaration(ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition)
        ],
        Procedures =
        [
            new ModuleProcedureDeclaration(
                UnisaCampusNexusPaymentPlansConstants.ProcedureProcessDailyFiles,
                "Process daily files (full)",
                "Downloads SFTP files and processes signed loan and TRANS files against Campus Nexus and the integration database.",
                Category: "Operations",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                null),
            new ModuleProcedureDeclaration(
                UnisaCampusNexusPaymentPlansConstants.ProcedureProcessTransFiles,
                "Process TRANS files (API + DB)",
                "Processes TRANS files using Campus Nexus and the integration database without requiring SFTP download.",
                Category: "Operations",
                ProcedureLaunchKind.Workflow,
                DefinitionId.Value,
                null),
        ],
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "ProcessDailyFiles",
            UnisaCampusNexusPaymentPlansConstants.ModuleId,
            new[] { "DownloadFiles", "ProcessSignedLoanFiles", "ProcessTransFiles", "Complete" },
            WorkflowCategory.Command,
            RequiredPermissions:
            [
                new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", $"module:{UnisaCampusNexusPaymentPlansConstants.ModuleId}"))
            ]);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        if (_scopeFactory is null)
            throw new InvalidOperationException("UnisaCampusNexusPaymentPlansModule requires IServiceScopeFactory when registering step handlers (worker host).");

        var scopeFactory = _scopeFactory;
        string mid = UnisaCampusNexusPaymentPlansConstants.ModuleId;
        registry.Register(mid, "DownloadFiles", new DownloadFilesStepHandler(scopeFactory));
        registry.Register(mid, "ProcessSignedLoanFiles", new ProcessSignedLoanFilesStepHandler(scopeFactory));
        registry.Register(mid, "ProcessTransFiles", new ProcessTransFilesStepHandler(scopeFactory));
        registry.Register(mid, "Complete", new CompleteStepHandler());
    }

    public IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions() =>
        UnisaIntegrationConfiguration.Settings;

    public IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements() =>
        UnisaIntegrationConfiguration.Connections;

    public IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() =>
        UnisaIntegrationConfiguration.Procedures;
}
