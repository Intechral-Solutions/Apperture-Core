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

public sealed record ComponentConnectionRefDto(string ConnectionId, string Name);

/// <summary>Active Connections made from the connector. Names are bounded; the count is complete.</summary>
public sealed record ConnectorComponentUsageDto(
    int ActiveConnectionCount,
    IReadOnlyList<ComponentConnectionRefDto> Connections);

/// <summary>Current subscriptions to the definition. Historical notification instances are not usage.</summary>
public sealed record NotificationDefinitionComponentUsageDto(int SubscriptionCount);

/// <summary>
/// Detail-only usage; exactly one family block is non-null. The whole object is null (not zero) when the caller
/// may not read the usage source.
/// </summary>
public sealed record ComponentUsageDto(
    ConnectorComponentUsageDto? Connector,
    NotificationDefinitionComponentUsageDto? NotificationDefinition);

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
    NotificationDefinitionComponentDetailDto? NotificationDefinition,
    ComponentUsageDto? Usage = null);
