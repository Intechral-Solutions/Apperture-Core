using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>What is known about the client secret an Entra registration needs.</summary>
public enum EntraClientSecretStatus
{
    /// <summary>A secret exists: the registration's stored secret resolved, or the deployment secret it asks for is present.</summary>
    Resolved,

    /// <summary>
    /// No secret exists, and that is established: the store answered that the reference names nothing it can serve (or the
    /// registration names none), and the registration has no deployment secret to use.
    /// </summary>
    Absent,

    /// <summary>
    /// The registration names a stored secret and the store could not be read, so whether the secret exists, and which secret the
    /// registration would use, is not established. <see cref="EntraClientSecretResolution.Secret"/> is still what the sign-in
    /// uses (the deployment secret, where the registration asks for it), because the sign-in has always fallen through.
    /// </summary>
    Unavailable,
}

/// <summary>What <see cref="EntraClientSecretAccess.ResolveAsync"/> does with a failure of the database the secret store reads from.</summary>
public enum EntraSecretFailureHandling
{
    /// <summary>
    /// A failure to read the store of any kind, a database failure included, is <see cref="EntraClientSecretStatus.Unavailable"/>, and
    /// the sign-in falls through as it always has. This is what the host's sign-in and every other caller use.
    /// </summary>
    Contain,

    /// <summary>
    /// A database failure (a <see cref="System.Data.Common.DbException"/>, directly or anywhere in the exception's chain: a deadlock
    /// victim, a timeout) is not an answer about the secret and propagates, so that the transaction the caller reads in can fail,
    /// roll back and be retried by its own machinery. Only the administrator-viability evaluation, which reads inside an audited
    /// unit, asks for this; a store that cannot be reached for any other reason is still <see cref="EntraClientSecretStatus.Unavailable"/>.
    /// </summary>
    PropagateDatabaseFailures,
}

/// <summary>Where a resolved client secret came from.</summary>
public enum EntraClientSecretSource
{
    /// <summary>No secret.</summary>
    None,

    /// <summary>The registration's own stored secret.</summary>
    SecretStore,

    /// <summary>The deployment's <c>Entra:ClientSecret</c>, which the registration asks for.</summary>
    DeploymentSecret,
}

/// <summary>
/// The outcome of resolving a registration's client secret, with the certainty of the answer. It carries the value the sign-in
/// uses (<see cref="Secret"/>) and a classification that never discloses the reference or the text of a resolver error.
/// </summary>
/// <param name="Status">How far the answer can be relied on.</param>
/// <param name="Source">Where <paramref name="Secret"/> came from.</param>
/// <param name="Secret">The value the sign-in uses, or null. Never written to a log, a response or an audit row.</param>
public sealed record EntraClientSecretResolution(EntraClientSecretStatus Status, EntraClientSecretSource Source, string? Secret)
{
    /// <summary>The classification only: the secret value is never part of the text.</summary>
    public override string ToString() => $"{nameof(EntraClientSecretResolution)} {{ Status = {Status}, Source = {Source} }}";
}

/// <summary>
/// Resolves the Entra confidential client secret from <c>ISecretStore</c>, then deployment config.
/// Never logs secret material.
/// </summary>
public static class EntraClientSecretAccess
{
    public static bool IsConfigured(string? clientSecretRef, bool usesDeploymentSecret, bool deploymentSecretPresent) =>
        !string.IsNullOrWhiteSpace(clientSecretRef)
        || (usesDeploymentSecret && deploymentSecretPresent);

    /// <summary>
    /// The secret the sign-in uses, or null. A failure to read the store falls through to the deployment secret, as it always
    /// has; <see cref="ResolveAsync"/> says whether that happened. Cancellation is not a failure and is not swallowed.
    /// </summary>
    public static async Task<string?> TryResolveAsync(
        EntraInstanceRegistration registration,
        ISecretResolver? secretResolver,
        string? deploymentSecret,
        CancellationToken cancellationToken = default) =>
        (await ResolveAsync(registration, secretResolver, deploymentSecret, cancellationToken).ConfigureAwait(false)).Secret;

    /// <summary>
    /// Resolves the secret and says how sure the answer is. The store's own refusal (<see cref="SecretNotFoundException"/>, or a
    /// secret with no value) is a confirmed absence; any other failure, or a stored reference with no resolver to ask, is
    /// <see cref="EntraClientSecretStatus.Unavailable"/> and is never read as absent. The deployment secret applies only where the
    /// registration asks for it.
    /// </summary>
    public static async ValueTask<EntraClientSecretResolution> ResolveAsync(
        EntraInstanceRegistration registration,
        ISecretResolver? secretResolver,
        string? deploymentSecret,
        CancellationToken cancellationToken = default,
        EntraSecretFailureHandling failureHandling = EntraSecretFailureHandling.Contain)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var storeUnreadable = false;
        if (!string.IsNullOrWhiteSpace(registration.ClientSecretRef))
        {
            if (secretResolver is null)
            {
                storeUnreadable = true;
            }
            else
            {
                try
                {
                    var resolved = await secretResolver
                        .ResolveAsync(new SecretRef(registration.ClientSecretRef.Trim()), cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(resolved.Material.OpaqueString))
                        return new EntraClientSecretResolution(EntraClientSecretStatus.Resolved, EntraClientSecretSource.SecretStore, resolved.Material.OpaqueString);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The caller cancelled: that is not a failure of the store, and not a reason to fall through.
                    throw;
                }
                catch (Exception ex) when (failureHandling == EntraSecretFailureHandling.PropagateDatabaseFailures && IsDatabaseFailure(ex))
                {
                    // The failure is the caller's transaction's, not an answer about the secret: it must reach that transaction's own
                    // rollback and retry, and must not become a reading ("unavailable") that a business decision is then made on.
                    throw;
                }
                catch (Exception ex) when (ex is not (SecretNotFoundException or SecretUnavailableException))
                {
                    // Neither a missing secret nor a refusal: the store could not be read. The caller logs at its own layer, without the text.
                    storeUnreadable = true;
                }
                catch (Exception)
                {
                    // The store answered that it holds no such secret for this registration: a confirmed absence.
                }
            }
        }

        var deployment = registration.UsesDeploymentSecret && !string.IsNullOrWhiteSpace(deploymentSecret)
            ? deploymentSecret.Trim()
            : null;
        var source = deployment is null ? EntraClientSecretSource.None : EntraClientSecretSource.DeploymentSecret;

        if (storeUnreadable)
            return new EntraClientSecretResolution(EntraClientSecretStatus.Unavailable, source, deployment);

        return deployment is null
            ? new EntraClientSecretResolution(EntraClientSecretStatus.Absent, EntraClientSecretSource.None, null)
            : new EntraClientSecretResolution(EntraClientSecretStatus.Resolved, EntraClientSecretSource.DeploymentSecret, deployment);
    }

    /// <summary>
    /// True when the exception, or any exception it wraps (an inner exception, or a member of an aggregate), is a database error.
    /// That is the same walk the database provider's retrying execution strategy makes to find a deadlock victim or a timeout it may
    /// retry, so what it would retry reaches it.
    /// </summary>
    private static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Data.Common.DbException)
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(IsDatabaseFailure))
                return true;
        }

        return false;
    }
}
