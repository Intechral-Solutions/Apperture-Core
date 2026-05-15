namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>
/// Declares a module-authored platform signal registered at worker startup (Epic 77 Phase 2).
/// Full event type is <see cref="ModulePlatformEventTypes.Format"/> combining manifest Id and <see cref="Name"/>.
/// </summary>
/// <param name="Name">Short signal name segments, e.g. demo.completed.</param>
/// <param name="DisplayName">Operator-facing title.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="DefaultSeverity">severity string: critical | error | warning | info.</param>
public sealed record ModuleSignalDeclaration(
    string Name,
    string DisplayName,
    string Description,
    string DefaultSeverity,
    IReadOnlyList<string>? RequiredDataProperties = null,
    bool RequiresIntegrationId = false,
    bool RequiresRunId = false)
{
    /// <inheritdoc cref="ModuleSignalDeclaration" />
    public IReadOnlyList<string> ResolvedRequiredDataProperties =>
        RequiredDataProperties ?? Array.Empty<string>();
}
