namespace Apperture.Core.Application.Audit;

// The closed audit vocabulary of the AUD-1 design gate (§10, §11, §13, §14). A new value needs a gate amendment.
// Every value here is stored in AuditLogs, so the spelling is a persisted contract.

/// <summary>Who an audit row says acted (§10.1). <c>unclassified</c> is a read-model value only and is never stored.</summary>
public static class AuditActorKinds
{
    public const string User = "user";
    public const string External = "external";
    public const string System = "system";
    public const string Anonymous = "anonymous";
    public const string Worker = "worker";
    public const string Unresolved = "unresolved";

    /// <summary>Read model only (§10.7): the row has not been classified yet.</summary>
    public const string Unclassified = "unclassified";

    public static IReadOnlySet<string> Stored { get; } = Set(User, External, System, Anonymous, Worker, Unresolved);

    internal static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
}

/// <summary>How the actor of a row was established (§10.6).</summary>
public static class AuditAttributions
{
    public const string Recorded = "recorded";
    public const string RecordedUserId = "recorded_user_id";
    public const string RecordedLiteral = "recorded_literal";
    public const string InferredUsername = "inferred_username";
    public const string InferredEntraObjectId = "inferred_entra_object_id";
    public const string Unresolved = "unresolved";
    public const string Ambiguous = "ambiguous";

    /// <summary>Read model only (§10.7).</summary>
    public const string NotYetClassified = "not_yet_classified";

    public static IReadOnlySet<string> Stored { get; } =
        AuditActorKinds.Set(Recorded, RecordedUserId, RecordedLiteral, InferredUsername, InferredEntraObjectId, Unresolved, Ambiguous);
}

/// <summary>Known attribution defects of rows not written by the recorder (§15.3).</summary>
public static class AuditAttributionIssues
{
    public const string SubjectRecordedAsActor = "subject_recorded_as_actor";
    public const string SubjectPossiblyRecordedAsActor = "subject_possibly_recorded_as_actor";
    public const string InitiatorNotRecorded = "initiator_not_recorded";
    public const string InitiatorPossiblyMisrecorded = "initiator_possibly_misrecorded";
    public const string PossiblePlaceholder = "possible_placeholder";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(
        SubjectRecordedAsActor, SubjectPossiblyRecordedAsActor, InitiatorNotRecorded, InitiatorPossiblyMisrecorded, PossiblePlaceholder);
}

/// <summary>How certain an attribution issue is (§15.3).</summary>
public static class AuditIssueCertainties
{
    public const string Definite = "definite";
    public const string Possible = "possible";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(Definite, Possible);
}

/// <summary>Subject area (§11.2). <see cref="DataAccess"/> is reserved and has no RC1 writer.</summary>
public static class AuditCategories
{
    public const string Identity = "identity";
    public const string Governance = "governance";
    public const string Configuration = "configuration";
    public const string Operations = "operations";
    public const string Secrets = "secrets";
    public const string System = "system";
    public const string DataAccess = "data_access";

    /// <summary>Legacy rows whose action is not in the catalog.</summary>
    public const string Other = "other";

    public static IReadOnlySet<string> All { get; } =
        AuditActorKinds.Set(Identity, Governance, Configuration, Operations, Secrets, System, DataAccess, Other);
}

/// <summary>Severity (§11.3). Independent of visibility.</summary>
public static class AuditSeverities
{
    public const string Standard = "standard";
    public const string Elevated = "elevated";
    public const string Critical = "critical";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(Standard, Elevated, Critical);
}

/// <summary>Visibility (§11.4): hides rows from the default view; never reduces what is stored.</summary>
public static class AuditVisibilities
{
    public const string Default = "default";
    public const string Routine = "routine";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(Default, Routine);
}

/// <summary>What a row asserts (§11.6).</summary>
public static class AuditBases
{
    public const string Change = "change";
    public const string Request = "request";
    public const string ExternalAttempt = "external_attempt";
    public const string ConfirmedResult = "confirmed_result";
    public const string SecurityEvent = "security_event";
    public const string Attempt = "attempt";
    public const string Telemetry = "telemetry";

    /// <summary>Legacy rows only: the action is not in the catalog, so nothing is asserted.</summary>
    public const string Unspecified = "unspecified";

    public static IReadOnlySet<string> All { get; } =
        AuditActorKinds.Set(Change, Request, ExternalAttempt, ConfirmedResult, SecurityEvent, Attempt, Telemetry, Unspecified);
}

/// <summary>Normalized outcome (§11.5). <see cref="Unknown"/> never matches an asserted outcome.</summary>
public static class AuditOutcomeCodes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string Rejected = "rejected";
    public const string Blocked = "blocked";
    public const string Requested = "requested";
    public const string Unknown = "unknown";

    public static IReadOnlySet<string> All { get; } =
        AuditActorKinds.Set(Succeeded, Failed, Denied, Rejected, Blocked, Requested, Unknown);
}

/// <summary>Who is known to have written a row's outcome (§11.5, AC-4).</summary>
public static class AuditOutcomeProvenances
{
    /// <summary>Written by a writer: a row with a producer, or an action first written by code containing the Epic 37 migration.</summary>
    public const string Recorded = "recorded";

    /// <summary>A legacy row of an action that existed before Epic 37: the outcome may have been derived from text.</summary>
    public const string Indeterminate = "indeterminate";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(Recorded, Indeterminate);
}

/// <summary>The primary target vocabulary (§11.1).</summary>
public static class AuditTargetTypes
{
    public const string User = "user";
    public const string Role = "role";
    public const string IdentityLink = "identity_link";
    public const string ExternalIdentity = "external_identity";
    public const string EntraAccessRequest = "entra_access_request";
    public const string EntraTenantRegistration = "entra_tenant_registration";
    public const string AuthPolicy = "auth_policy";
    public const string ResourceContext = "resource_context";
    public const string Integration = "integration";
    public const string Connection = "connection";
    public const string Schedule = "schedule";
    public const string Binding = "binding";
    public const string PlatformSetting = "platform_setting";
    public const string IntegrationSetting = "integration_setting";
    public const string NotificationDefinition = "notification_definition";
    public const string NotificationSubscription = "notification_subscription";
    public const string ManagedContact = "managed_contact";
    public const string OperationalTeam = "operational_team";
    public const string Module = "module";
    public const string WorkflowRun = "workflow_run";
    public const string OperationRun = "operation_run";
    public const string OperationItem = "operation_item";
    public const string RuntimeInstance = "runtime_instance";
    public const string Secret = "secret";
    public const string Instance = "instance";

    public static IReadOnlySet<string> All { get; } = AuditActorKinds.Set(
        User, Role, IdentityLink, ExternalIdentity, EntraAccessRequest, EntraTenantRegistration, AuthPolicy,
        ResourceContext, Integration, Connection, Schedule, Binding, PlatformSetting, IntegrationSetting, NotificationDefinition,
        NotificationSubscription, ManagedContact, OperationalTeam, Module,
        WorkflowRun, OperationRun, OperationItem, RuntimeInstance,
        Secret, Instance);
}

/// <summary>Which path wrote a row (§14). Rows written before AUD-1.1 have no producer.</summary>
public static class AuditProducers
{
    public const string Recorder = "recorder";
    public const string Append = "append";
}

/// <summary>The classification set's completion state (§11.8). Null means unclassified.</summary>
public static class AuditClassificationStates
{
    public const string Complete = "complete";
}

/// <summary>The details format of recorder rows (§12.1). Append and legacy rows have none.</summary>
public static class AuditDetailsFormats
{
    public const string Json1 = "json/1";
}

/// <summary>The guarantee an action's writes carry (§13.1).</summary>
public enum AuditWriteClass
{
    /// <summary>W1: the mutation and its row commit in one transaction, with commit verification.</summary>
    RequiredMutation,

    /// <summary>W2: the row commits before the result is delivered.</summary>
    RequiredSecurityEvent,

    /// <summary>W3: its own short transaction; a failed write is logged and the response is unchanged.</summary>
    Attempt,

    /// <summary>W4: best effort, logged; never blocks the runtime.</summary>
    Telemetry,

    /// <summary>A listed exception (§13.1): written by a startup data step in its own transaction.</summary>
    ListedException,
}
