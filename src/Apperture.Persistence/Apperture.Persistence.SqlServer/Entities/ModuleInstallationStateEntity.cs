namespace Apperture.Persistence.SqlServer;

public sealed class ModuleInstallationStateEntity
{
    public string EnvironmentId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
