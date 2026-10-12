namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 4: the typed producers of the binding create and delete. The binding is the target. It names its module and slot
// and, when it has them, the integration that owns it and the connection or capability it binds. The integration's owning
// resource context is the related context (null for a contextless capability binding). A connection id is an entity id, not a
// secret reference; nothing a connection holds is recorded.
public static partial class AuditEvents
{
    private const int AuditLogBindingTargetIdLength = 256;

    /// <summary>A binding was created.</summary>
    public static AuditEvent BindingCreated(
        string bindingId,
        string? integrationId,
        string moduleId,
        string slotName,
        string? connectionId,
        string? capabilityId,
        string? resourceContextId) =>
        BindingEvent(AuditActionNames.BindingCreated, bindingId, integrationId, moduleId, slotName, connectionId, capabilityId, resourceContextId);

    /// <summary>A binding was deleted. Written in the same transaction as the removal.</summary>
    public static AuditEvent BindingDeleted(
        string bindingId,
        string? integrationId,
        string moduleId,
        string slotName,
        string? connectionId,
        string? capabilityId,
        string? resourceContextId) =>
        BindingEvent(AuditActionNames.BindingDeleted, bindingId, integrationId, moduleId, slotName, connectionId, capabilityId, resourceContextId);

    private static AuditEvent BindingEvent(
        string action,
        string bindingId,
        string? integrationId,
        string moduleId,
        string slotName,
        string? connectionId,
        string? capabilityId,
        string? resourceContextId)
    {
        if (bindingId.Length > AuditLogBindingTargetIdLength)
            throw new ArgumentException("A binding id longer than the audit target column cannot be recorded.", nameof(bindingId));
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.Binding, bindingId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("moduleId", moduleId),
                ("slotName", slotName),
                ("integrationId", BoundedId(integrationId)),
                ("connectionId", BoundedId(connectionId)),
                ("capabilityId", BoundedId(capabilityId))),
            new AuditRelatedReferences(
                ModuleId: BoundedId(moduleId, AuditLogModuleIdLength),
                ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength)));
    }
}
