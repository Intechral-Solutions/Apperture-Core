namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Declares a single connection configuration or secret field for operator UX and documentation.
/// Not an executable operation surface.
/// </summary>
public sealed record ConnectionFieldDescriptor(
    string Key,
    string DisplayName,
    bool IsSecret,
    string? Description = null);
