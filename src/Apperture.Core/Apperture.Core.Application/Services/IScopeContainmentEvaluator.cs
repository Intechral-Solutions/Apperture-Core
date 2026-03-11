namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates scope containment for authorization. Determines whether an inner scope is contained within an outer scope.
/// Example: instance contains module:X; module:X contains module:X:slot:Y.
/// Per ADR-0012.
/// </summary>
public interface IScopeContainmentEvaluator
{
    /// <summary>
    /// Returns true if innerScopeKey is contained within or equal to outerScopeKey.
    /// </summary>
    bool IsContained(string innerScopeKey, string outerScopeKey);
}
