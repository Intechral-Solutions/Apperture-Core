using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Disposable fixture identities. Every identity is fictitious, lives only in the harness's private SQL
/// database and uses a throwaway passphrase that protects nothing.
/// </summary>
public sealed class HarnessSeeding(IServiceProvider liveServices)
{
    /// <summary>Fictitious, harness-only passphrase (satisfies the local password policy). Not a real credential.</summary>
    public const string Passphrase = "harbor-lantern-quiet-cedar-77";

    /// <summary>Creates a Platform Administrator with a local credential and returns its user id.</summary>
    public async Task<string> CreateLocalAdministratorAsync(string username, string displayName)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<ILocalPasswordHasher>();

        // Same rule as every production creation path: the sign-in name is a policy-conforming username.
        var normalized = UsernamePolicy.RequireNewAssignment(username);
        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName,
            [new IdentityLink(IdentitySource.Local, normalized, LastAuthenticatedAt: null)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: false)
        {
            Username = normalized
        };
        await users.AddAsync(user).ConfigureAwait(false);

        var (hash, salt) = hasher.HashPassword(Passphrase);
        await credentials.SetPasswordAsync(userId, hash, salt, hasher.AlgorithmName).ConfigureAwait(false);
        return userId.Value;
    }

    /// <summary>
    /// Registers the stub provider as the active Entra registration. The association and step-up services
    /// require an active registration to know which tenant to accept; this is the same row an operator
    /// creates in the Entra tenants UI, pointing at the stub authority instead of Microsoft.
    /// </summary>
    public async Task CreateActiveEntraRegistrationAsync(StubOidcSettings oidc)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var registrations = scope.ServiceProvider.GetRequiredService<IEntraInstanceRegistrationRepository>();
        var now = DateTimeOffset.UtcNow;
        await registrations.AddAsync(new EntraInstanceRegistration
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            DisplayName = "Test Directory",
            Authority = oidc.Authority,
            AzureTenantId = oidc.TenantId,
            ClientId = oidc.ClientId,
            UsesDeploymentSecret = true,
            Enabled = true,
            IsActive = true,
            AuthenticationContextId = oidc.AuthenticationContextId,
            CreatedUtc = now,
            UpdatedUtc = now,
        }).ConfigureAwait(false);
    }

    /// <summary>Creates a Platform Administrator whose only identity is a proven link to the stub directory.</summary>
    public async Task<string> CreateEntraAdministratorAsync(StubOidcSettings oidc, string objectId, string displayName)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var now = DateTimeOffset.UtcNow;
        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        await new UsernameService(users).AddWithGeneratedUsernameAsync(
            username => new User(
                userId,
                displayName,
                [new IdentityLink(IdentitySource.Entra, objectId, LastAuthenticatedAt: now, TenantId: oidc.TenantId, LinkedAt: now)],
                [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
                IsBreakGlassEligible: false)
            {
                Username = username
            },
            new UsernameGenerationSources(UserPrincipalName: null, Mail: null, DisplayName: displayName)).ConfigureAwait(false);
        return userId.Value;
    }

    /// <summary>
    /// Gives an existing LOCAL operator a proven Entra link to a stub-directory identity (an operator who signs in with a
    /// password and steps up with Microsoft). Idempotent: does nothing when that (tenant, object id) is already linked.
    /// Returns the operator's user id.
    /// </summary>
    public async Task<string> LinkLocalOperatorToEntraAsync(string username, StubOidcSettings oidc, string objectId)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdentityLinkAsync(IdentitySource.Local, username).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No local operator '{username}'.");
        var key = ProviderIdentityKey.Entra(oidc.TenantId, objectId);
        if (await users.GetByIdentityLinkAsync(key).ConfigureAwait(false) is null)
        {
            var now = DateTimeOffset.UtcNow;
            await users.AddIdentityLinkAsync(
                user.Id,
                new IdentityLink(IdentitySource.Entra, objectId, LastAuthenticatedAt: now, TenantId: oidc.TenantId, LinkedAt: now)).ConfigureAwait(false);
        }

        return user.Id.Value;
    }

    /// <summary>Sets the persisted instance sign-in policy flag the login page reads through <c>GET /api/auth/sign-in-options</c>.</summary>
    public async Task SetAutoRedirectToEntraAsync(bool enabled)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInstanceAuthUiPolicyRepository>();
        var policy = await repository.GetAsync().ConfigureAwait(false);
        policy.AutoRedirectToEntra = enabled;
        await repository.SaveAsync(policy, policy.RowVersion).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the user's identity links so the still-valid session cookie no longer maps to a usable
    /// Apperture user (<c>GET /api/auth/me</c> then answers 403).
    /// </summary>
    public async Task UnlinkUserAsync(string userId)
    {
        await using var scope = liveServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        await db.IdentityLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync().ConfigureAwait(false);
    }
}
