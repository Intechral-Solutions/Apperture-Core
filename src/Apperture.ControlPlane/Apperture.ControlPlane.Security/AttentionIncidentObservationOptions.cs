namespace Apperture.ControlPlane.Security;

/// <summary>Epic 68: periodic control-plane observation of schedule expectation → attention incidents.</summary>
public sealed class AttentionIncidentObservationOptions
{
    public const string SectionName = "AttentionIncidents:Observation";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}
