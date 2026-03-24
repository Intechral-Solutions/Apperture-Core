namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Optional static manifest entry documenting a connector definition contributed by the module (documentation/tooling).
/// Runtime registration remains via DI and connector-definition contributors registered by the host.
/// </summary>
public sealed record ConnectorDefinitionDeclaration(
    string ConnectorDefinitionId,
    string DisplayName,
    string? Description = null);
