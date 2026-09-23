namespace Apperture.Management.Client;

/// <summary>One row of the Component catalog. Identity is <c>(Kind, NativeId)</c>; there is no Component id.</summary>
public sealed record ComponentSummaryDto(
    string Kind,
    string NativeId,
    string DisplayName,
    string? Description,
    string Origin,
    string? SupplyingModuleId);

/// <summary>Module identifier only. Display names are resolved on detail, never on list.</summary>
public sealed record ComponentModuleRefDto(string ModuleId);

/// <summary>
/// Catalog list. <paramref name="AvailableKinds"/> and <paramref name="AvailableModules"/> reflect only the
/// authorized items collected, so a family the caller cannot read never appears in them.
/// </summary>
public sealed record ComponentCatalogResultDto(
    IReadOnlyList<ComponentSummaryDto> Items,
    IReadOnlyList<string> AvailableKinds,
    IReadOnlyList<ComponentModuleRefDto> AvailableModules,
    int TotalMatching);

/// <summary>Module metadata; present only when the caller holds <c>module.configure</c>.</summary>
public sealed record ModuleAttributionDto(string ModuleId, string DisplayName, string Version, string State);

public sealed record ConnectorComponentDetailDto(
    IReadOnlyList<ConnectionFieldCatalogDto> Fields,
    bool SupportsConnectionVerification);

public sealed record NotificationDefinitionComponentDetailDto(
    string? PrimaryEventType,
    string SourceKind,
    string Scope,
    string Mutability,
    string? DefaultSeverity,
    bool IsEnabled,
    bool ToastEnabledByDefault,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Exactly one family block (<c>Connector</c> or <c>NotificationDefinition</c>) is non-null, matching <c>Kind</c>.</summary>
public sealed record ComponentDetailDto(
    string Kind,
    string NativeId,
    string DisplayName,
    string? Description,
    string Origin,
    string? SupplyingModuleId,
    ModuleAttributionDto? SupplyingModule,
    ConnectorComponentDetailDto? Connector,
    NotificationDefinitionComponentDetailDto? NotificationDefinition);
