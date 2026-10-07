using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

/// <summary>The one primary target of an event (§11.1). A deleted target keeps its id.</summary>
public sealed record AuditTarget
{
    public const int MaxIdLength = 256;

    public AuditTarget(string type, string id)
    {
        if (!AuditTargetTypes.All.Contains(type))
            throw new ArgumentException($"'{type}' is not an audit target type.", nameof(type));
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength)
            throw new ArgumentException("An audit target id is non-empty and bounded.", nameof(id));
        Type = type;
        Id = id;
    }

    public string Type { get; }
    public string Id { get; }
}

/// <summary>Related references kept alongside the primary target (§11.1).</summary>
public sealed record AuditRelatedReferences(
    string? ModuleId = null,
    string? ScheduleId = null,
    string? RunId = null,
    string? ResourceContextId = null,
    string? CorrelationId = null);

/// <summary>
/// One structured audit event, built only by a typed producer (<see cref="AuditEvents"/>). Its fields were validated
/// against the action's declared schema when it was built.
/// </summary>
public sealed class AuditEvent
{
    internal AuditEvent(
        AuditActionDefinition definition,
        AuditTarget? target,
        string outcomeCode,
        IReadOnlyDictionary<string, object?> fields,
        AuditRelatedReferences? related = null)
    {
        if (definition.Details is null)
            throw new InvalidOperationException($"Audit action '{definition.Action}' has no structured producer yet; it is written through the append bridge.");
        if (!AuditOutcomeCodes.All.Contains(outcomeCode))
            throw new ArgumentException($"'{outcomeCode}' is not an audit outcome code.", nameof(outcomeCode));
        if (definition.TargetType is { } declared && target is not null && target.Type != declared)
            throw new ArgumentException($"Audit action '{definition.Action}' targets {declared}, not {target.Type}.", nameof(target));
        if (definition.TargetType is not null && target is null)
            throw new ArgumentException($"Audit action '{definition.Action}' requires a {definition.TargetType} target.", nameof(target));

        definition.Details.Validate(definition.Action, fields);
        Definition = definition;
        Target = target;
        OutcomeCode = outcomeCode;
        Fields = fields;
        Related = related ?? new AuditRelatedReferences();
    }

    public AuditActionDefinition Definition { get; }
    public string Action => Definition.Action;
    public AuditTarget? Target { get; }
    public string OutcomeCode { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }
    public AuditRelatedReferences Related { get; }

    /// <summary>Builds an event for any catalog action with a declared schema. Producers and tests only.</summary>
    internal static AuditEvent Create(
        AuditActionDefinition definition,
        AuditTarget? target,
        string outcomeCode,
        IReadOnlyDictionary<string, object?> fields,
        AuditRelatedReferences? related = null) =>
        new(definition, target, outcomeCode, fields, related);
}

/// <summary>
/// The typed producers (§12.1): one factory per converted action. There is no public way to write an arbitrary field
/// name. AUD-1.2 to AUD-1.4 add producers as they convert their writers.
/// </summary>
public static partial class AuditEvents
{
    /// <summary>Builds an event for a catalog action: null fields are omitted, and the schema validates the rest.</summary>
    private static AuditEvent Event(
        string action,
        AuditTarget? target,
        string outcome,
        params (string Name, object? Value)[] fields) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            target,
            outcome,
            fields.Where(f => f.Value is not null).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));

    private static AuditTarget Target(string type, string id) => new(type, id);

    /// <summary>The audit identity of each permission (<see cref="PermissionSetChange.Identity"/>).</summary>
    private static IReadOnlyList<string> Identities(IEnumerable<Apperture.Shared.Kernel.PermissionDescriptor> permissions) =>
        permissions.Select(PermissionSetChange.Identity).ToList();

    /// <summary>
    /// An administrator renamed a user, or the user reclaimed one of their own retired names. The names are the ones the
    /// renaming transaction replaced and stored, never a separate read (IAM-9 §29.3).
    /// </summary>
    public static AuditEvent UsernameChanged(UserId user, string previousUsername, string newUsername, UsernameChangeOutcome change)
    {
        if (change == UsernameChangeOutcome.Unchanged)
            throw new ArgumentException("An unchanged username is not audited as a change.", nameof(change));
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.UsernameChanged),
            new AuditTarget(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["userId"] = user.Value,
                ["previousUsername"] = previousUsername,
                ["newUsername"] = newUsername,
                ["change"] = change == UsernameChangeOutcome.Reclaimed ? "reclaimed" : "assigned",
            });
    }

    /// <summary>
    /// The designated Recovery Access user was given the canonical Recovery Access username and display name. When only the
    /// display name changed, the previous and new username are equal.
    /// </summary>
    public static AuditEvent RecoveryAccessCanonicalized(UserId user, string previousUsername, string newUsername) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.RecoveryAccessCanonicalized),
            new AuditTarget(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["userId"] = user.Value,
                ["previousUsername"] = previousUsername,
                ["newUsername"] = newUsername,
            });
}
