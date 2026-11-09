using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4B-C service suites against real SQL Server: the Recovery Access and authenticator services as the emergency command line
/// composes them, over one context each (the real audited unit, the real refusal writer, the real gate, the real facts reader, the real
/// attested configuration reader and attestation store), and the arrangements and observations those suites share. Everything a case
/// concludes is read back with SQL it sends itself, on a connection of its own.
/// </summary>
internal sealed class ServiceViabilityWorld
{
    public const string Admin = RoleAuthorityWorld.AdminRoleId;
    public const string Tenant = "11111111-1111-1111-1111-111111111111";

    private ServiceViabilityWorld(RoleAuthorityWorld world) => World = world;

    public RoleAuthorityWorld World { get; }

    public AuditSqlScratch Scratch => World.Scratch;

    /// <summary>
    /// A shared catalog cleared of what these suites arrange: no attestation, no authenticator, no candidate, no administrator role
    /// holder, nobody designated, no registration. Users and audit rows stay; every case makes users and reads rows of its own.
    /// </summary>
    /// <remarks>
    /// A designation through the service gives the account the reserved Recovery Access username, and the username ledger never gives a
    /// name to a second account. So that every case of a shared throwaway catalog can designate through the real service, the account
    /// that took the name in an earlier case gets a unique one back and the ledger forgets the reserved name. This is arrangement of a
    /// throwaway catalog only; nothing in the product frees a name.
    /// </remarks>
    public static async Task<ServiceViabilityWorld> FreshAsync(AuditIsolationCatalogs catalogs, bool rcsi)
    {
        var scratch = catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM HostConfigurationAttestations;
            DELETE FROM UserTotpReplacementCandidates;
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            UPDATE IdentityLinks SET ExternalId = CONCAT(N'freed', LEFT(UserId, 20)) WHERE Source = 1 AND ExternalId = N'{EmergencyAccessIdentity.LocalUsername}';
            UPDATE Users SET Username = CONCAT(N'freed', LEFT(Id, 20)) WHERE Username = N'{EmergencyAccessIdentity.LocalUsername}';
            DELETE FROM UsernameLedger WHERE Username = N'{EmergencyAccessIdentity.LocalUsername}';
            """);
        return new ServiceViabilityWorld(new RoleAuthorityWorld(scratch));
    }

    // ---- People ----

    /// <summary>An ordinary administrator who can sign in without Microsoft: the role, a Local link and an enabled Local credential.</summary>
    public Task<string> EntraIndependentAdminAsync(string stem = "admin") => World.NewUserAsync(stem, Admin);

    /// <summary>An administrator who can sign in only through a proven Microsoft link for <see cref="Tenant"/>.</summary>
    public async Task<string> EntraOnlyAdminAsync(string stem = "entra")
    {
        var id = await World.NewUserAsync(stem, Admin);
        await Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{id}';");
        await World.AddEntraLinkAsync(id, Tenant);
        return id;
    }

    /// <summary>A designated Recovery Access account with a healthy posture (role, credential, enrolled authenticator, one unused code).</summary>
    public async Task<string> HealthyRecoveryAsync(string stem = "recovery")
    {
        var id = await World.NewUserAsync(stem, Admin);
        await World.MakeRecoveryAccessHealthyAsync(id);
        return id;
    }

    // ---- What the Management Host attested ----

    private static AttestedEntraOptions UsableEntra() =>
        new(true, "https://login.microsoftonline.com/" + Tenant, "client", true, Tenant, "/signin-oidc", ["openid", "profile"], null, false);

    /// <summary>What a host attests at start: Local established, its Microsoft sign-in options not built yet (Microsoft unknown).</summary>
    public static AttestedAuthenticationPayload AtStart(bool local = true) => new(local, local, UsableEntra(), EntraNow: null);

    /// <summary>What a host attests once real traffic built its options: Local and Microsoft both established, for <see cref="Tenant"/>.</summary>
    public static AttestedAuthenticationPayload Established() =>
        new(true, true, UsableEntra(), new AttestedEntraNow(
            UsableEntra(),
            EntraOptionsBuildQuality.Complete,
            new AttestedHandler(RunningHandlerState.Bound, "https://login.microsoftonline.com/" + Tenant + "/v2.0", "client", true, EntraOptionsBuildQuality.Complete)));

    /// <summary>The host writes its attestation in a session of its own; with <paramref name="registration"/> the active registration for <see cref="Tenant"/> too.</summary>
    public async Task AttestAsync(AttestedAuthenticationPayload payload, bool registration = false)
    {
        if (registration)
            await World.SetActiveRegistrationAsync(Tenant);
        await using var db = Scratch.NewDb();
        await new SqlHostConfigurationAttestationStore(db).ClaimAsync(
            new HostConfigurationAttestationWrite("host-a", "p1", DateTimeOffset.UtcNow, AttestedAuthenticationPayload.SchemaVersion, payload.Serialize()));
    }

    /// <summary>Makes the attestation older than the freshness window by the database's own clock.</summary>
    public Task AgeAttestationAsync(int seconds = 300) =>
        Scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -{seconds}, SYSUTCDATETIME());");

    /// <summary>The host stopped gracefully: its row is marked stopped.</summary>
    public async Task StopHostAsync()
    {
        await using var db = Scratch.NewDb();
        (await new SqlHostConfigurationAttestationStore(db).MarkStoppedAsync("host-a", "p1")).Should().BeTrue();
    }

    // ---- The services, as the emergency command line composes them ----

    /// <summary>The coordinator of the emergency command line: it reads the deployment's configuration only from the attestation.</summary>
    public static AdministratorViabilityCoordinator Coordinator(AppertureDbContext db) =>
        new(
            new SqlPlatformAdministratorSetGate(db),
            new AdministratorViabilityService(
                new SqlAdministratorFactsReader(db),
                new EffectiveSignInConfigurationProvider(
                    new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
                    new SqlEntraInstanceRegistrationRepository(db))));

    public SqlAuditEventWriter Events() => new(Scratch.Options(), null, HarnessCatalog, null, null, null);

    public NativeMfaService Mfa(AppertureDbContext db, IAuditedUnitOfWork? unit = null, IAdministratorViabilityCoordinator? coordinator = null, TimeProvider? time = null) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new ManagementHost.IntegrationTestSecretProtector(),
            time ?? TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            unit ?? NewUnit(db, null));

    public EmergencyAccessService Emergency(AppertureDbContext db, IAuditedUnitOfWork? unit = null, IAdministratorViabilityCoordinator? coordinator = null) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            Mfa(db, unit, coordinator),
            TimeProvider.System,
            NullLogger<EmergencyAccessService>.Instance,
            unit ?? NewUnit(db, null),
            coordinator ?? Coordinator(db),
            Events());

    // ---- Observations ----

    public Task<bool> DesignatedAsync(string userId) =>
        Scratch.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{userId}';");

    public Task<int> DesignatedCountAsync() => Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE IsBreakGlassEligible = 1;");

    public Task<bool> CredentialDisabledAsync(string userId) =>
        Scratch.ScalarAsync<bool>($"SELECT IsDisabled FROM LocalCredentials WHERE UserId = N'{userId}';");

    public Task<string> PasswordHashAsync(string userId) =>
        Scratch.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{userId}';");

    /// <summary>The rows of <paramref name="action"/> whose target is exactly <paramref name="targetId"/> (compared in binary, so the case counts).</summary>
    public async Task<IReadOnlyList<AuditRow>> RowsAsync(string action, string targetId)
    {
        var rows = await Scratch.RowsAsync($"""
            SELECT Action, TargetId, Outcome, Actor, Details FROM AuditLogs
             WHERE Action = N'{action}' AND TargetId COLLATE Latin1_General_BIN2 = N'{targetId}' ORDER BY Id;
            """);
        return rows.Select(r => new AuditRow(
            (string)r["Action"]!,
            (string)r["TargetId"]!,
            r["Outcome"] as string,
            r["Actor"] as string,
            Fields((string?)r["Details"]))).ToList();
    }

    /// <summary>Every row of any action with this exact target.</summary>
    public Task<int> AnyRowsAsync(string targetId) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId COLLATE Latin1_General_BIN2 = N'{targetId}';");

    private static IReadOnlyDictionary<string, string?> Fields(string? details)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(details))
            return fields;
        using var document = JsonDocument.Parse(details);
        foreach (var property in document.RootElement.EnumerateObject())
            fields[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        return fields;
    }

    public sealed record AuditRow(string Action, string TargetId, string? Outcome, string? Actor, IReadOnlyDictionary<string, string?> Fields);
}
