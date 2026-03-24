namespace Apperture.Core.Domain.Client;

/// <summary>
/// Metadata for a higher-level operation exposed by a client definition. Epic 51.
/// Execution and history deferred.
/// </summary>
public sealed record ClientOperationMetadata(
    string OperationKey,
    string DisplayName,
    string? Description,
    bool IsOperatorFacing,
    bool MayRequireUserInputs,
    string? ParameterHint);
