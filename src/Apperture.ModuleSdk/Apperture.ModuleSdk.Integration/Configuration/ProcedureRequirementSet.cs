namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Procedure-specific subset of module configuration requirements.
/// When omitted for a procedure, integration-wide required deps apply.
/// </summary>
public sealed record ProcedureRequirementSet
{
    public required string ProcedureId { get; init; }

    /// <summary>Setting keys required to launch this procedure (subset of module declarations).</summary>
    public IReadOnlyList<string> RequiredSettingKeys { get; init; } = Array.Empty<string>();

    /// <summary>Connection requirement keys required to launch this procedure.</summary>
    public IReadOnlyList<string> RequiredConnectionKeys { get; init; } = Array.Empty<string>();
}
