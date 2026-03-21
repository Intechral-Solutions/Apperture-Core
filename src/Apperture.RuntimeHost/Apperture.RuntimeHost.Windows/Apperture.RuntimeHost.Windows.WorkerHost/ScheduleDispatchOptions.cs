namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Options for schedule dispatch. Epic 28.
/// </summary>
public sealed class ScheduleDispatchOptions
{
    public const string SectionName = "ScheduleDispatch";

    public int PollIntervalSeconds { get; set; } = 30;
    public string? RuntimePlaneId { get; set; }
}
