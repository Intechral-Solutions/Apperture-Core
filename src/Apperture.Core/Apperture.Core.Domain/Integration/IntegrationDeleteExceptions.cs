namespace Apperture.Core.Domain.Integration;

/// <summary>
/// An integration delete targeted an integration that does not exist, as the delete read it (before its unit, or under the
/// integration row lock inside it). AUD-1.4.
/// </summary>
/// <remarks>
/// Inherits <see cref="InvalidOperationException"/> so a caller that only catches the base type still fails closed. The delete
/// endpoint catches exactly this type and maps it to 404; any other failure of the delete (an audit invariant, a transaction or
/// infrastructure error, an unconfirmed commit) is not a "not found" and reaches the host's handler instead. A delete whose commit
/// was verified after a lost acknowledgement reports success, never this.
/// </remarks>
public sealed class IntegrationNotFoundException : InvalidOperationException
{
    public IntegrationNotFoundException(IntegrationId integrationId)
        : base($"Integration {integrationId.Value} not found.")
    {
        IntegrationId = integrationId;
    }

    public IntegrationId IntegrationId { get; }
}

/// <summary>The current-state dependents that block an integration delete (CTX-3B referential-lifecycle inventory).</summary>
public enum IntegrationDeleteBlocker
{
    Bindings,
    Schedules,
    NotificationDefinitions,
    NotificationSubscriptions,
    AttentionRoutings,
}

/// <summary>
/// An integration delete was refused because a current-state dependent still refers to the integration. Nothing was changed. The
/// message is the operator-facing text the delete has always returned; <see cref="Blocker"/> and <see cref="Count"/> are the
/// typed reason.
/// </summary>
public sealed class IntegrationDeleteBlockedException : InvalidOperationException
{
    public IntegrationDeleteBlockedException(IntegrationDeleteBlocker blocker, int count)
        : base(MessageFor(blocker, count))
    {
        Blocker = blocker;
        Count = count;
    }

    public IntegrationDeleteBlocker Blocker { get; }

    public int Count { get; }

    private static string MessageFor(IntegrationDeleteBlocker blocker, int count) => blocker switch
    {
        IntegrationDeleteBlocker.Bindings => $"Cannot delete integration: {count} binding(s) reference it.",
        IntegrationDeleteBlocker.Schedules => $"Cannot delete integration: {count} schedule(s) reference it.",
        IntegrationDeleteBlocker.NotificationDefinitions =>
            $"Cannot delete integration: {count} notification definition(s) reference it. Delete them first.",
        IntegrationDeleteBlocker.NotificationSubscriptions =>
            $"Cannot delete integration: {count} notification subscription(s) reference it. Remove them first.",
        IntegrationDeleteBlocker.AttentionRoutings =>
            $"Cannot delete integration: {count} attention routing override(s) reference it. Clear them first.",
        _ => $"Cannot delete integration: {count} dependent record(s) reference it.",
    };
}
