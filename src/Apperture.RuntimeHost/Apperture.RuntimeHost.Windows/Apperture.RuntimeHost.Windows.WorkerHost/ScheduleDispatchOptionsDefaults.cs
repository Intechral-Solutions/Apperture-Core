namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Resolves schedule-dispatch plane independently of runtime host identity (SWM-1).
/// Plane selects which <c>ScheduleAssignments</c> the timer evaluates.
/// Host / claim identity continues to come from <c>APERTURE_RUNTIME_ID</c> / <see cref="RuntimeHostIdentityOptions"/>.
/// </summary>
public static class ScheduleDispatchOptionsDefaults
{
    public const string DefaultRuntimePlaneId = "local";

    /// <summary>
    /// Applies plane default when <see cref="ScheduleDispatchOptions.RuntimePlaneId"/> is unset.
    /// Does not inherit from <c>APERTURE_RUNTIME_ID</c> (that value is host identity, not plane).
    /// </summary>
    public static void ApplyPlaneDefaults(ScheduleDispatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RuntimePlaneId))
            options.RuntimePlaneId = DefaultRuntimePlaneId;
    }

    /// <summary>
    /// Resolves the effective dispatch plane after config bind + defaults.
    /// </summary>
    public static string ResolveRuntimePlaneId(ScheduleDispatchOptions options) =>
        string.IsNullOrWhiteSpace(options.RuntimePlaneId)
            ? DefaultRuntimePlaneId
            : options.RuntimePlaneId.Trim();
}
