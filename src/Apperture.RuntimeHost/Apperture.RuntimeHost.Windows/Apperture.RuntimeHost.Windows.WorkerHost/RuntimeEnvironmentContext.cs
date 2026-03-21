using Apperture.Shared.Kernel;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

internal sealed class RuntimeEnvironmentContext : IEnvironmentContext
{
    private const string EnvVarName = "APERTURE_ENVIRONMENT_ID";

    public string EnvironmentId { get; }

    public RuntimeEnvironmentContext()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVarName);
        EnvironmentId = string.IsNullOrWhiteSpace(raw) ? "local" : raw.Trim();
    }
}
