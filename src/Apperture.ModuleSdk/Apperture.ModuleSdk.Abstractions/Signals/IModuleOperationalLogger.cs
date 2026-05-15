namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>Structured operational log lines scoped to workflow/operation execution (Epic 77 Phase 2).</summary>
public interface IModuleOperationalLogger
{
    void LogOperational(
        OperationalLogLevel level,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? data = null);
}

public enum OperationalLogLevel
{
    Trace = 0,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
