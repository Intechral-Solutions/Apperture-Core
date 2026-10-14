using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// The setting-secret boundary (<see cref="ISettingSecretResolver"/>). It is the only production code that turns a reference read
/// from a setting row into material.
/// </summary>
/// <remarks>
/// <para><b>Ownership first.</b> The reference is resolved with <see cref="ISecretResolver.ResolveOwnedAsync"/> against the owner
/// <see cref="SettingSecretOwnership"/> defines for the setting, so a reference that names another holder's secret is refused
/// before that secret's payload is read or unprotected. A blank reference, an unreadable or missing one, a disabled secret and a
/// foreign one are all the same refusal. A failure that is not a refusal (a protector error, a cancellation) is not turned into
/// one: it propagates and writes nothing.</para>
/// <para><b>One refusal row per call.</b> A call is one top-level operation, so it writes one W3 row, naming the setting and the
/// kind of operation and nothing about why. The actor is the worker for an integration setting on <see cref="SettingSecretResolutionPath.Run"/>,
/// the platform itself for an instance setting on that path, and on <see cref="SettingSecretResolutionPath.Management"/> the signed-in
/// caller, or an anonymous caller when no one is signed in (so the row can always be attributed).</para>
/// <para><b>Evidence never gates the refusal.</b> The refusal is decided before the row is attempted. A row that cannot be written
/// (the writer reports it, or throws) is logged, and the caller still gets the refusal and no secret. What is logged is the setting
/// and the path, never the reference.</para>
/// </remarks>
public sealed class SettingSecretResolver : ISettingSecretResolver
{
    /// <summary>How long a refusal's evidence write may take. It is bounded because the caller is waiting to be told no.</summary>
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(15);

    private readonly ISecretResolver _secretResolver;
    private readonly IAuditEventWriter _auditEvents;
    private readonly ILogger<SettingSecretResolver> _logger;
    private readonly IIntegrationRepository? _integrations;
    private readonly IPrincipalContext? _principals;

    public SettingSecretResolver(
        ISecretResolver secretResolver,
        IAuditEventWriter auditEvents,
        ILogger<SettingSecretResolver>? logger = null,
        IIntegrationRepository? integrations = null,
        IPrincipalContext? principals = null)
    {
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _auditEvents = auditEvents ?? throw new ArgumentNullException(nameof(auditEvents));
        _logger = logger ?? NullLogger<SettingSecretResolver>.Instance;
        _integrations = integrations;
        _principals = principals;
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolveIntegrationSettingAsync(
        string integrationId, string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(integrationId);
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            return await ResolveOwnedAsync(reference, () => SettingSecretOwnership.ForIntegrationSetting(integrationId, key), cancellationToken).ConfigureAwait(false);
        }
        catch (SecretUnavailableException)
        {
            await RecordIntegrationRefusalAsync(integrationId, key, path).ConfigureAwait(false);
            throw new SettingSecretUnavailableException(key);
        }
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolvePlatformSettingAsync(
        string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            return await ResolveOwnedAsync(reference, () => SettingSecretOwnership.ForPlatformSetting(key), cancellationToken).ConfigureAwait(false);
        }
        catch (SecretUnavailableException)
        {
            await RecordPlatformRefusalAsync(key, path).ConfigureAwait(false);
            throw new SettingSecretUnavailableException(key);
        }
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolveDeploymentConfiguredAsync(string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // The one unchecked call in the setting-secret code, deliberately: deployment configuration is trusted input (P-22).
        var resolved = await _secretResolver.ResolveAsync(new SecretRef(reference.Trim()), cancellationToken).ConfigureAwait(false);
        return TextOf(resolved);
    }

    private async ValueTask<string> ResolveOwnedAsync(string? reference, Func<SecretOwner> ownerOf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new SecretUnavailableException();

        SecretOwner owner;
        try
        {
            owner = ownerOf();
        }
        catch (ArgumentException)
        {
            // A name the store cannot hold names no stored secret.
            throw new SecretUnavailableException();
        }

        var resolved = await _secretResolver.ResolveOwnedAsync(new SecretRef(reference.Trim()), owner, cancellationToken).ConfigureAwait(false);
        return TextOf(resolved);
    }

    private static string TextOf(ResolvedSecret resolved) => resolved.Material.OpaqueString ?? resolved.Material.Json ?? "";

    private async Task RecordIntegrationRefusalAsync(string integrationId, string key, SettingSecretResolutionPath path)
    {
        _logger.LogWarning(
            "A secret for setting {SettingKey} of integration {IntegrationId} was refused on path {Path}.",
            key, integrationId, PathName(path));
        try
        {
            var resourceContextId = await ResourceContextOfAsync(integrationId).ConfigureAwait(false);
            var actor = path == SettingSecretResolutionPath.Run ? AuditActor.Worker : await ManagementActorAsync().ConfigureAwait(false);
            await WriteAsync(AuditEvents.IntegrationSettingSecretResolutionRefused(integrationId, key, PathName(path), resourceContextId), actor, integrationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLost(integrationId, ex);
        }
    }

    private async Task RecordPlatformRefusalAsync(string key, SettingSecretResolutionPath path)
    {
        _logger.LogWarning("A secret for platform setting {SettingKey} was refused on path {Path}.", key, PathName(path));
        try
        {
            var actor = path == SettingSecretResolutionPath.Run ? AuditActor.System("platform-settings") : await ManagementActorAsync().ConfigureAwait(false);
            await WriteAsync(AuditEvents.PlatformSettingSecretResolutionRefused(key, PathName(path)), actor, key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLost(key, ex);
        }
    }

    /// <summary>Writes the W3 row. It never decides anything: the refusal was settled before this runs.</summary>
    private async Task WriteAsync(AuditEvent auditEvent, AuditActor actor, string subject)
    {
        using var timeout = new CancellationTokenSource(EvidenceTimeout);
        var written = await _auditEvents.TryWriteAsync(auditEvent, actor, timeout.Token).ConfigureAwait(false);
        if (!written)
            _logger.LogWarning("The refusal for {Subject} was not recorded in the audit trail.", subject);
    }

    private void LogLost(string subject, Exception ex) =>
        // The type only: a message is not needed to know evidence was lost, and none is trusted not to carry a value.
        _logger.LogWarning("The refusal for {Subject} could not be recorded in the audit trail ({ExceptionType}).", subject, ex.GetType().Name);

    /// <summary>The integration's owning resource context, for the row's related reference. Best effort: its absence only leaves the reference out.</summary>
    private async Task<string?> ResourceContextOfAsync(string integrationId)
    {
        if (_integrations is null)
            return null;
        try
        {
            using var timeout = new CancellationTokenSource(EvidenceTimeout);
            var integration = await _integrations.GetByIdAsync(new IntegrationId(integrationId), timeout.Token).ConfigureAwait(false);
            return integration?.ResourceContextId.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The signed-in caller when there is one. A refusal in a flow that has no signed-in principal (a recovery or invitation
    /// flow that sends mail) is attributed to an anonymous caller, so the row is always writable and never blames a person.
    /// </summary>
    private async Task<AuditActor> ManagementActorAsync()
    {
        if (_principals is null)
            return AuditActor.Anonymous;
        try
        {
            using var timeout = new CancellationTokenSource(EvidenceTimeout);
            return await _principals.GetCurrentActorAsync(timeout.Token).ConfigureAwait(false) is null
                ? AuditActor.Anonymous
                : AuditActor.CurrentPrincipal;
        }
        catch (Exception)
        {
            return AuditActor.Anonymous;
        }
    }

    private static string PathName(SettingSecretResolutionPath path) => path switch
    {
        SettingSecretResolutionPath.Run => "run",
        SettingSecretResolutionPath.Management => "management",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown resolution path."),
    };
}
