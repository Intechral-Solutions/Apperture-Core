using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

internal static class WorkerHostStartupValidation
{
    public static void ValidateModules(IReadOnlyList<IModule> modules)
    {
        if (modules.Count == 0)
            throw new InvalidOperationException("WorkerHost has no modules registered. Configure an explicit known-module list.");

        var seenModuleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in modules)
        {
            var id = m.GetManifest().Id;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("A module manifest has an empty Id.");
            if (!seenModuleIds.Add(id))
                throw new InvalidOperationException($"Duplicate module id '{id}' in WorkerHost module list.");
        }

        ModuleSignalManifestValidator.ThrowIfInvalid(modules);

        // Definition ownership guardrail:
        // - WorkflowDefinition.Id must be globally unique across loaded modules.
        // - WorkflowDefinition.ModuleId must match the owning module's manifest id.
        var seenDefinitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in modules)
        {
            var moduleId = m.GetManifest().Id;
            var definitions = DrainDefinitions(m).GetAwaiter().GetResult();
            foreach (var d in definitions)
            {
                if (!string.Equals(d.ModuleId, moduleId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"WorkflowDefinition '{d.Id.Value}' declares ModuleId '{d.ModuleId}' but is owned by module '{moduleId}'.");

                if (!seenDefinitionIds.Add(d.Id.Value))
                    throw new InvalidOperationException($"Duplicate workflow definition id '{d.Id.Value}' across loaded modules.");
            }
        }
    }

    public static void ValidateHandlerOwnership(IStepHandlerRegistry registry, IReadOnlyList<IModule> modules)
    {
        // Currently enforced at registration time by ModuleScopedStepHandlerRegistry (no collisions, no cross-module).
        // This hook exists to keep the guardrails centralized if/when we extend validation.
        _ = registry;
        _ = modules;
    }

    private static async Task<IReadOnlyList<WorkflowDefinition>> DrainDefinitions(IModule module)
    {
        var list = new List<WorkflowDefinition>();
        await foreach (var d in module.GetDefinitionsAsync(CancellationToken.None).ConfigureAwait(false))
            list.Add(d);
        return list;
    }
}

