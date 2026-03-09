namespace Apperture.Core.Security.Trust;

/// <summary>
/// Result of a successful enrollment. Runtime should store operational credential locally (protected).
/// </summary>
public sealed record EnrollmentResult(
    string RuntimeId,
    bool Success,
    string? Message = null);
