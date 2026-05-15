using Apperture.ModuleSdk.Abstractions.Signals;
using Microsoft.Extensions.Logging;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

internal sealed class ModuleOperationalLogger(ILogger<ModuleOperationalLogger> logger) : IModuleOperationalLogger
{
    public void LogOperational(
        OperationalLogLevel level,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? data = null) =>
        logger.Log(ToLogLevel(level), "module.op [{Code}] {Message} | Data={@Data}", code, message, data);

    private static LogLevel ToLogLevel(OperationalLogLevel level) =>
        level switch
        {
            OperationalLogLevel.Trace => LogLevel.Trace,
            OperationalLogLevel.Debug => LogLevel.Debug,
            OperationalLogLevel.Information => LogLevel.Information,
            OperationalLogLevel.Warning => LogLevel.Warning,
            OperationalLogLevel.Error => LogLevel.Error,
            OperationalLogLevel.Critical => LogLevel.Critical,
            _ => LogLevel.Information,
        };
}
