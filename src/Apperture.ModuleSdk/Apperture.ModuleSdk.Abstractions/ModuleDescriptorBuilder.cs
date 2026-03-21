using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ModuleSdk.Abstractions;

public static class ModuleDescriptorBuilder
{
    public static async ValueTask<ModuleDescriptor> BuildAsync(IModule module, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);

        var manifest = module.GetManifest();
        ValidateManifest(manifest);

        var contributions = new List<ModuleContributionDescriptor>();
        var workflowIds = new HashSet<string>(StringComparer.Ordinal);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var permissionKeys = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!workflowIds.Add(definition.Id.Value))
                throw new InvalidOperationException($"Module '{manifest.Id}' contributes duplicate workflow id '{definition.Id.Value}'.");
            contributions.Add(new WorkflowContributionDescriptor(definition.Id.Value, definition.Name));
        }

        if (module is IOperationDefinitionProvider operationProvider)
        {
            await foreach (var operation in operationProvider.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!operationIds.Add(operation.Id.Value))
                    throw new InvalidOperationException($"Module '{manifest.Id}' contributes duplicate operation id '{operation.Id.Value}'.");
                contributions.Add(new OperationContributionDescriptor(operation.Id.Value, operation.Name));
            }
        }

        foreach (var permission in manifest.Permissions ?? [])
        {
            var resourceType = permission.ResourceType ?? string.Empty;
            var key = $"{permission.Action}:{resourceType}";
            if (!permissionKeys.Add(key))
                continue;
            contributions.Add(new PermissionContributionDescriptor(permission.Action, resourceType));
        }

        if (module is IModuleNavigationProvider navigationProvider)
        {
            foreach (var navigation in navigationProvider.GetNavigationContributions())
                contributions.Add(navigation);
        }

        return new ModuleDescriptor(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Description,
            Publisher: null,
            MinimumPlatformVersion: null,
            contributions);
    }

    private static void ValidateManifest(ModuleManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidOperationException("Module descriptor requires non-empty module id.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException($"Module '{manifest.Id}' descriptor requires non-empty display name.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException($"Module '{manifest.Id}' descriptor requires non-empty version.");
    }
}
