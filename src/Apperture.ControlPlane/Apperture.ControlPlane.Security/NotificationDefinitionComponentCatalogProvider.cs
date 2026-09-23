using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Projects platform- and module-supplied Notification Definitions into the Component catalog.
/// Eligibility is an <b>Origin</b> predicate (<c>platformBuiltIn</c> or <c>moduleBuiltIn</c>), never a
/// Scope or id-shape inference. Depends only on the authorized definition service, so the family's
/// <c>notification.definition.read</c> gate always applies.
/// </summary>
public sealed class NotificationDefinitionComponentCatalogProvider : IComponentCatalogProvider
{
    private static readonly string[] EligibleOrigins =
    [
        NotificationDefinitionOrigins.PlatformBuiltIn,
        NotificationDefinitionOrigins.ModuleBuiltIn,
    ];

    private readonly INotificationDefinitionService _definitions;
    private readonly INotificationSubscriptionService _subscriptions;

    public NotificationDefinitionComponentCatalogProvider(
        INotificationDefinitionService definitions,
        INotificationSubscriptionService subscriptions)
    {
        _definitions = definitions;
        _subscriptions = subscriptions;
    }

    public string Kind => ComponentKinds.NotificationDefinition;

    public async ValueTask<IReadOnlyList<ComponentSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Bounded by the store's own 200-row cap. Real scale is a handful of built-ins, so no paging is
        // built; were the cap ever reached the catalog total would under-report, which is why it is stated here.
        // One origin-set query. The origin predicate is re-applied below so eligibility never depends on
        // the storage layer having honoured the query.
        var rows = await _definitions
            .ListAsync(
                new NotificationDefinitionListQuery(Take: ComponentCatalogQuery.MaxTake, Origins: EligibleOrigins),
                cancellationToken)
            .ConfigureAwait(false);
        return rows.Where(IsEligible).Select(ToSummary).ToList();
    }

    public async ValueTask<ComponentDetail?> GetAsync(string nativeId, CancellationToken cancellationToken = default)
    {
        NotificationDefinition? row;
        try
        {
            row = await _definitions.GetAsync(nativeId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Raised when an operator-authored definition references an Integration that no longer exists.
            // Such a row is not a Component, so the lookup resolves to "not found".
            return null;
        }

        if (row is null || !IsEligible(row))
            return null;

        return new ComponentDetail(
            ToSummary(row),
            NotificationDefinition: new NotificationDefinitionComponentFacts(
                row.PrimaryEventType,
                row.SourceKind,
                row.Scope,
                row.Mutability,
                row.DefaultSeverity,
                row.IsEnabled,
                row.ToastEnabledByDefault,
                row.UpdatedAtUtc),
            Usage: await GetUsageAsync(row.Id.Value, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Current subscriptions to the definition, through the authorized subscription service. Historical
    /// notification instances, per-user preference rows and Integration ownership are deliberately not usage.
    /// Null (not zero) when the caller may not read the definition's subscriptions.
    /// </summary>
    private async ValueTask<ComponentUsage?> GetUsageAsync(string definitionId, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptions.ListByDefinitionAsync(definitionId, cancellationToken).ConfigureAwait(false);
            // Every subscription row counts, exactly as the authoritative subscriptions section lists them
            // (disabled and muted rows included), so the two surfaces never disagree.
            return new ComponentUsage(NotificationDefinition: new NotificationDefinitionComponentUsage(subscriptions.Count));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // The definition (or an owning Integration) disappeared between the lookup and this call. Usage
            // decorates a Component that was just read, so it degrades to "not available" instead of failing the page.
            return null;
        }
    }

    private static bool IsEligible(NotificationDefinition row) =>
        EligibleOrigins.Contains(row.Origin, StringComparer.Ordinal);

    private static ComponentSummary ToSummary(NotificationDefinition row)
    {
        // Cross-check, not a second rule: Origin and ModuleId must agree. Inconsistent data is surfaced,
        // never silently coerced into one catalog origin or the other.
        var isPlatform = string.Equals(row.Origin, NotificationDefinitionOrigins.PlatformBuiltIn, StringComparison.Ordinal);
        if (isPlatform != (row.ModuleId is null))
        {
            throw new InvalidOperationException(
                $"Notification definition '{row.Id.Value}' has origin '{row.Origin}' but module id " +
                $"'{row.ModuleId ?? "(none)"}'; platform built-ins must have no module and module built-ins must have one.");
        }

        return new ComponentSummary(
            new ComponentKey(ComponentKinds.NotificationDefinition, row.Id.Value),
            row.DisplayName,
            row.Description,
            ComponentOrigins.FromSupplyingModuleId(row.ModuleId),
            row.ModuleId);
    }
}
