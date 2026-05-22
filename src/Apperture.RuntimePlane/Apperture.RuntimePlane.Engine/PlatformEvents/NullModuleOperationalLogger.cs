using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>Used when operational logging is not registered for the runtime host.</summary>
public sealed class NullModuleOperationalLogger : IModuleOperationalLogger
{
    public static NullModuleOperationalLogger Instance { get; } = new();

    private NullModuleOperationalLogger()
    {
    }

    /// <inheritdoc />
    public void LogOperational(
        OperationalLogLevel level,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? data = null)
    {
    }
}
