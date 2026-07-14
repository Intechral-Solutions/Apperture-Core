using System.Diagnostics;
using System.Reflection;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// SWM-7: best-effort safe worker metadata for scheduler heartbeats.
/// Never throws — missing values stay null.
/// </summary>
internal static class WorkerRuntimeHeartbeatMetadata
{
    private static readonly Lazy<DateTimeOffset?> ProcessStarted =
        new(ResolveProcessStartedUtc, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string?> ApplicationVersion =
        new(ResolveApplicationVersion, LazyThreadSafetyMode.ExecutionAndPublication);

    public static DateTimeOffset? TryGetProcessStartedUtc() => ProcessStarted.Value;

    public static string? TryGetApplicationVersion() => ApplicationVersion.Value;

    public static string? TryGetEnvironmentName()
    {
        try
        {
            var name = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                       ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ResolveProcessStartedUtc()
    {
        try
        {
            var local = Process.GetCurrentProcess().StartTime;
            var utc = DateTime.SpecifyKind(local.ToUniversalTime(), DateTimeKind.Utc);
            return new DateTimeOffset(utc);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveApplicationVersion()
    {
        try
        {
            var asm = typeof(WorkerRuntimeHeartbeatMetadata).Assembly;
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return Truncate(informational);

            var version = asm.GetName().Version?.ToString();
            return string.IsNullOrWhiteSpace(version) ? null : Truncate(version);
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }
}
