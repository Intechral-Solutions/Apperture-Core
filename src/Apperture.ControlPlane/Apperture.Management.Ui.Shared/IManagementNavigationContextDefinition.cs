using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Application-owned definition for a typed navigation context (<c>from</c> + <c>fromKey</c>).
/// </summary>
public interface IManagementNavigationContextDefinition
{
    string Type { get; }

    bool TryResolve(string key, out ManagementResolvedNavigationContext context);
}
