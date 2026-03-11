using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements scope containment rules per ADR-0012: instance contains module; module contains slot.
/// </summary>
public sealed class ScopeContainmentEvaluator : IScopeContainmentEvaluator
{
    /// <inheritdoc />
    public bool IsContained(string innerScopeKey, string outerScopeKey)
    {
        if (string.IsNullOrEmpty(innerScopeKey) || string.IsNullOrEmpty(outerScopeKey))
            return false;

        if (string.Equals(innerScopeKey, outerScopeKey, StringComparison.Ordinal))
            return true;

        // instance contains everything (module:X, connection:X, operation:X, module:X:slot:Y)
        if (string.Equals(outerScopeKey, ScopeKeyConstants.Instance, StringComparison.Ordinal))
            return true;

        // module:X contains module:X:slot:Y
        if (innerScopeKey.StartsWith(outerScopeKey + ":", StringComparison.Ordinal))
            return true;

        return false;
    }
}
