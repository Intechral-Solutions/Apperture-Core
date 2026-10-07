using Apperture.Core.Application.Services;

namespace Apperture.Core.Application.Audit;

/// <summary>
/// Who performs an audited operation (§10.8). A person is never passed in: <see cref="CurrentPrincipal"/> tells the
/// recorder to take the actor from the authenticated context. Non-human actors are passed only through these named
/// entries, so every recorded literal comes from <see cref="ActorLiterals"/>.
/// </summary>
public sealed record AuditActor
{
    private AuditActor(string kind, string? literal, string? userId, AuditExternalActor? external)
    {
        Kind = kind;
        Literal = literal;
        UserId = userId;
        External = external;
    }

    /// <summary><see cref="AuditActorKinds"/> value, or null for <see cref="CurrentPrincipal"/> before resolution.</summary>
    public string? Kind { get; }

    /// <summary>The recorded literal of a non-human actor.</summary>
    public string? Literal { get; }

    /// <summary>The user id of a resolved <c>user</c> actor.</summary>
    public string? UserId { get; }

    /// <summary>The verified provider identity of an <c>external</c> actor.</summary>
    public AuditExternalActor? External { get; }

    /// <summary>True for the marker the recorder resolves from the authenticated context.</summary>
    public bool IsCurrentPrincipal => Kind is null;

    /// <summary>The authenticated principal of the current request, resolved by the recorder.</summary>
    public static AuditActor CurrentPrincipal { get; } = new(null, null, null, null);

    /// <summary>The platform itself, or a named subsystem (<c>system:{subtype}</c>).</summary>
    public static AuditActor System(string? subtype = null)
    {
        if (subtype is null)
            return new(AuditActorKinds.System, ActorLiterals.System, null, null);
        if (subtype.Length == 0 || subtype.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
            throw new ArgumentException("A system subtype is lower-case letters, digits and hyphens.", nameof(subtype));
        return new(AuditActorKinds.System, ActorLiterals.SystemPrefix + subtype, null, null);
    }

    /// <summary>The Worker Host processing durable start requests.</summary>
    public static AuditActor Worker { get; } = new(AuditActorKinds.Worker, ActorLiterals.SystemWorker, null, null);

    /// <summary>An unauthenticated caller, including failed or unvalidated authentication.</summary>
    public static AuditActor Anonymous { get; } = new(AuditActorKinds.Anonymous, ActorLiterals.Anonymous, null, null);

    /// <summary>The emergency access command-line tool, run by an operator on the server.</summary>
    public static AuditActor EmergencyCli { get; } = new(AuditActorKinds.System, ActorLiterals.EmergencyCli, null, null);

    /// <summary>Bootstrap platform-access recovery.</summary>
    public static AuditActor RecoveryTool { get; } = new(AuditActorKinds.System, ActorLiterals.Recovery, null, null);

    /// <summary>A platform user, as resolved by the recorder from the authenticated context.</summary>
    internal static AuditActor ForUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || ActorLiterals.IsNonHumanLiteral(userId))
            throw new ArgumentException("A user actor needs a user id.", nameof(userId));
        return new(AuditActorKinds.User, null, userId, null);
    }

    /// <summary>
    /// The user this request's own flow has just authenticated, where no principal context exists yet: credentials verified
    /// by a sign-in or step-up, an active one-time account link (setup, contact verification, reset) presented and checked
    /// by its flow, or the initiating user of a Microsoft self-link carried in the sign-in handler's protected,
    /// correlation-bound state. Every other operation records <see cref="CurrentPrincipal"/>, and a person is never passed
    /// in from request input. A test pins the call sites.
    /// </summary>
    public static AuditActor AuthenticatedUser(Apperture.Core.Domain.Identity.UserId user) =>
        ForUser(user.Value);

    /// <summary>
    /// An identity verified by an external provider in the current request, with no platform user (§10.4). The triple
    /// must come from a validated authentication result, never from request input.
    /// </summary>
    internal static AuditActor ForExternal(AuditExternalActor external) =>
        new(AuditActorKinds.External, null, null, external ?? throw new ArgumentNullException(nameof(external)));

    /// <summary>
    /// An identity authenticated by an external provider in the current request, for which no platform user is the actor
    /// (§10.4, OR-2): provider, tenant id and object id, all taken from the validated principal. Never from a request body,
    /// query string, header or typed value; failed or unvalidated authentication is <see cref="Anonymous"/>. A test pins
    /// the call sites to the flows that hold a validated principal.
    /// </summary>
    public static AuditActor ValidatedExternalIdentity(AuditExternalActor external) => ForExternal(external);

    /// <summary>The verbatim <c>Actor</c> column value: a user id, a literal, or the canonical external key.</summary>
    public string RecordedValue =>
        Kind switch
        {
            null => throw new InvalidOperationException("The current principal must be resolved before it is recorded."),
            AuditActorKinds.User => UserId!,
            AuditActorKinds.External => External!.CanonicalKey,
            _ => Literal!,
        };
}

/// <summary>A verified external identity (§10.4). Email, UPN and display name are not keys.</summary>
public sealed record AuditExternalActor
{
    public const string EntraProvider = "entra";

    public AuditExternalActor(string provider, string tenantId, string subjectId, string? nameHint = null)
    {
        if (provider != EntraProvider)
            throw new ArgumentException("Only Microsoft Entra identities are recorded as external actors.", nameof(provider));
        if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(subjectId, out _))
            throw new ArgumentException("An Entra identity is its tenant id and object id, both GUIDs.");
        Provider = provider;
        TenantId = tenantId.ToLowerInvariant();
        SubjectId = subjectId.ToLowerInvariant();
        NameHint = nameHint;
    }

    public string Provider { get; }
    public string TenantId { get; }
    public string SubjectId { get; }

    /// <summary>A provider display name, presentation only.</summary>
    public string? NameHint { get; }

    /// <summary><c>entra/{tenantId}/{objectId}</c>. The <c>/</c> cannot occur in a username or a literal.</summary>
    public string CanonicalKey => $"{Provider}/{TenantId}/{SubjectId}";
}
