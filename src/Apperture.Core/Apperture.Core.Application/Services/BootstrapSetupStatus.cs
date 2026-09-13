namespace Apperture.Core.Application.Services;

/// <summary>
/// Platform bootstrap/setup state for first-admin flows. v1 uses user-count as the initialization gate.
/// </summary>
public sealed record BootstrapSetupStatus(bool Initialized, bool AllowsFirstAdmin)
{
    public static BootstrapSetupStatus FromUserCount(int userCount)
    {
        var initialized = userCount > 0;
        return new BootstrapSetupStatus(initialized, AllowsFirstAdmin: !initialized);
    }
}
