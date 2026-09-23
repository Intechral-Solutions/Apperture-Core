namespace Apperture.Core.Application.ComponentCatalog;

/// <summary>
/// Component family tokens. A Component is a read-model projection, so a kind is the name of the
/// authoritative family that supplies it, never a persisted discriminator. Tokens are strings (not an
/// enum) so that adding a family is additive on the wire.
/// </summary>
public static class ComponentKinds
{
    public const string Connector = "connector";
    public const string NotificationDefinition = "notification-definition";

    /// <summary>The closed server-side token set. Never use this to derive what a caller may see.</summary>
    public static IReadOnlyList<string> All { get; } = [Connector, NotificationDefinition];

    public static bool IsKnown(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>
/// Catalog origin, derived uniformly as <c>SupplyingModuleId is null ? System : Module</c>.
/// There is no user origin: operator-authored content is not a Component.
/// </summary>
public static class ComponentOrigins
{
    public const string System = "system";
    public const string Module = "module";

    public static IReadOnlyList<string> All { get; } = [System, Module];

    public static bool IsKnown(string? origin) =>
        origin is not null && All.Contains(origin, StringComparer.Ordinal);

    public static string FromSupplyingModuleId(string? supplyingModuleId) =>
        supplyingModuleId is null ? System : Module;
}

/// <summary>Stable identity of a Component: the owning family plus that family's own native id, verbatim.</summary>
public sealed record ComponentKey(string Kind, string NativeId)
{
    /// <summary>Logical key for logs and assertions. Routing uses the two segments separately.</summary>
    public override string ToString() => $"{Kind}:{NativeId}";
}

public sealed record ComponentSummary(
    ComponentKey Key,
    string DisplayName,
    string? Description,
    string Origin,
    string? SupplyingModuleId);

/// <summary>Module identifier only; display names are resolved on detail, never on list.</summary>
public sealed record ComponentModuleRef(string ModuleId);

/// <summary>Module metadata, populated only when the caller holds <c>module.configure</c>.</summary>
public sealed record ComponentModuleAttribution(string ModuleId, string DisplayName, string Version, string State);

public sealed record ConnectorComponentField(
    string Key,
    string DisplayName,
    bool IsSecret,
    string? Description,
    bool IsRequired);

public sealed record ConnectorComponentFacts(
    IReadOnlyList<ConnectorComponentField> Fields,
    bool SupportsConnectionVerification);

public sealed record NotificationDefinitionComponentFacts(
    string? PrimaryEventType,
    string SourceKind,
    string Scope,
    string Mutability,
    string? DefaultSeverity,
    bool IsEnabled,
    bool ToastEnabledByDefault,
    DateTimeOffset UpdatedAtUtc);

public sealed record ComponentConnectionRef(string ConnectionId, string Name);

/// <param name="ActiveConnectionCount">
/// Active Connections made from the connector that the caller may see. Retired Connections are decommissioned
/// configuration and do not count as current usage.
/// </param>
/// <param name="Connections">Up to a bounded number of those Connections, by name; the count is always complete.</param>
public sealed record ConnectorComponentUsage(
    int ActiveConnectionCount,
    IReadOnlyList<ComponentConnectionRef> Connections);

/// <param name="SubscriptionCount">
/// Current subscriptions to the definition. Historical notification instances, per-user preferences and
/// Integration ownership are deliberately not usage.
/// </param>
public sealed record NotificationDefinitionComponentUsage(int SubscriptionCount);

/// <summary>
/// Current usage of a Component, exactly one family block populated. Detail-only: it is never part of a list
/// row. A null <see cref="ComponentDetail.Usage"/> means "not visible to this caller", never "unused".
/// </summary>
public sealed record ComponentUsage(
    ConnectorComponentUsage? Connector = null,
    NotificationDefinitionComponentUsage? NotificationDefinition = null);

/// <summary>Exactly one family block is populated, matching <see cref="ComponentSummary.Key"/>'s kind.</summary>
public sealed record ComponentDetail(
    ComponentSummary Summary,
    ComponentModuleAttribution? SupplyingModule = null,
    ConnectorComponentFacts? Connector = null,
    NotificationDefinitionComponentFacts? NotificationDefinition = null,
    ComponentUsage? Usage = null);

public sealed record ComponentCatalogQuery(
    string? Kind = null,
    string? Origin = null,
    string? ModuleId = null,
    string? Q = null,
    int Take = ComponentCatalogQuery.DefaultTake)
{
    public const int DefaultTake = 200;
    public const int MaxTake = 200;
}

/// <param name="AvailableKinds">Kinds present among the authorized items collected; never the static token set.</param>
/// <param name="AvailableModules">Distinct supplying module ids of the authorized items collected.</param>
/// <param name="TotalMatching">Post-filter count of authorized items before <see cref="ComponentCatalogQuery.Take"/> truncation.</param>
public sealed record ComponentCatalogResult(
    IReadOnlyList<ComponentSummary> Items,
    IReadOnlyList<string> AvailableKinds,
    IReadOnlyList<ComponentModuleRef> AvailableModules,
    int TotalMatching);

/// <summary>
/// Aggregated, authorized, read-only catalog over the specialized family read services. Not a registry,
/// not persisted, and it grants nothing the family gates do not.
/// </summary>
public interface IComponentCatalog
{
    /// <exception cref="UnauthorizedAccessException">Every family consulted by the query was denied to the caller.</exception>
    ValueTask<ComponentCatalogResult> QueryAsync(ComponentCatalogQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns null for an unknown kind, an unknown native id, or a family the caller may not read.</summary>
    ValueTask<ComponentDetail?> GetAsync(ComponentKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// One provider per Component family. Implementations must depend on the family's authorized read
/// service, never on the underlying registry or store, and must throw
/// <see cref="UnauthorizedAccessException"/> when the caller may not read the family.
/// </summary>
public interface IComponentCatalogProvider
{
    string Kind { get; }

    ValueTask<IReadOnlyList<ComponentSummary>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ComponentDetail?> GetAsync(string nativeId, CancellationToken cancellationToken = default);
}
