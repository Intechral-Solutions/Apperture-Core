namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Schedule bound to a runtime plane. Epic 28.
/// </summary>
public sealed class ScheduleAssignmentEntity
{
    public string Id { get; set; } = "";
    public string ScheduleId { get; set; } = "";
    public string RuntimePlaneId { get; set; } = "";
    public string? PolicyOverrides { get; set; }
    public int DesiredVersion { get; set; }
    public int AppliedVersion { get; set; }
}
