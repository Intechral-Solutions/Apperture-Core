namespace Apperture.ModuleSdk.Abstractions;

public abstract record ModuleContributionDescriptor(string Kind, string Id);

public sealed record WorkflowContributionDescriptor(string Id, string Name) : ModuleContributionDescriptor("workflow", Id);

public sealed record OperationContributionDescriptor(string Id, string Name) : ModuleContributionDescriptor("operation", Id);

public sealed record PermissionContributionDescriptor(string Action, string ResourceType) : ModuleContributionDescriptor("permission", $"{Action}:{ResourceType}");

public sealed record NavigationContributionDescriptor(string Domain, string Label, string Route, string? EntryId = null)
    : ModuleContributionDescriptor("navigation", EntryId ?? $"{Domain}:{Route}");
