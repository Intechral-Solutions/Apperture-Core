using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary><c>ExpiresAt</c> is null for toasts that persist until dismissed (UIX-6 errors).</summary>
public sealed record ToastItem(
    Guid Id,
    ToastSeverity Severity,
    string Message,
    string? Title,
    DateTimeOffset? ExpiresAt);
