namespace Apperture.Persistence.SqlServer;

public sealed class NotificationDefinitionEntity
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string SourceKind { get; set; } = null!;
    public string Origin { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public string Mutability { get; set; } = null!;
    public string? ModuleId { get; set; }
    public string? IntegrationId { get; set; }
    public string? PrimaryEventType { get; set; }
    public bool IsEnabled { get; set; }
    public string? DefaultSeverity { get; set; }
    public string RuleJson { get; set; } = null!;
    public string TemplateJson { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? CreatedByActorId { get; set; }
    public string? UpdatedByActorId { get; set; }
    public bool ToastEnabledByDefault { get; set; }
}
