using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

public sealed record ToastItem(
    Guid Id,
    ToastSeverity Severity,
    string Message,
    string? Title,
    DateTimeOffset ExpiresAt);
