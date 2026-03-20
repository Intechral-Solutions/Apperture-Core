using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Minimal Environment identity for governance scoping.
/// Single-tenant: EnvironmentId is stable for the process and derived from configuration/env vars.
/// </summary>
public sealed class StaticEnvironmentContext : IEnvironmentContext
{
    private const string EnvVarName = "APERTURE_ENVIRONMENT_ID";

    public string EnvironmentId { get; }

    public StaticEnvironmentContext()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVarName);
        EnvironmentId = string.IsNullOrWhiteSpace(raw) ? "local" : raw.Trim();
    }
}

