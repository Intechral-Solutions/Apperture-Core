namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Visibility of a module-provided capability. Public capabilities may be consumed by other modules via binding; private only by the defining module.
/// </summary>
public enum CapabilityVisibility
{
    Private,
    Public
}
