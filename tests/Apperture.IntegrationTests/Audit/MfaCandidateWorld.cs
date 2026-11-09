using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A user with an enrolled authenticator in a throwaway catalog, and the operations the replacement-candidate suites perform on it,
/// each on a context of its own the way separate requests do. Observations are made by plain SQL on a separate connection, so
/// they see what is committed and never what a test's own context believes.
/// </summary>
internal sealed class MfaCandidateWorld
{
    private readonly RoleAuthorityWorld _world;
    private readonly ManagementHost.IntegrationTestSecretProtector _protector = new();

    private MfaCandidateWorld(RoleAuthorityWorld world, UserId user)
    {
        _world = world;
        UserId = user;
    }

    public AuditSqlScratch Scratch => _world.Scratch;
    public RoleAuthorityWorld World => _world;
    public UserId UserId { get; }

    /// <summary>The recovery codes shown when the account was enrolled.</summary>
    public IReadOnlyList<string> EnrollmentCodes { get; private set; } = [];

    public static async Task<MfaCandidateWorld> CreateAsync(AuditIsolationCatalogs catalogs, bool rcsi, bool enrolled = true)
    {
        var world = new RoleAuthorityWorld(catalogs.For(rcsi));
        var created = new MfaCandidateWorld(world, new UserId(await world.NewUserAsync("mfa")));
        if (enrolled)
            created.EnrollmentCodes = await created.EnrollAsync(created.UserId);
        return created;
    }

    /// <summary>The same operations and observations over a user that already exists in <paramref name="world"/>, for example one made healthy for Recovery Access.</summary>
    public static MfaCandidateWorld Over(RoleAuthorityWorld world, UserId user) => new(world, user);

    /// <summary>Another user of the same catalog, enrolled.</summary>
    public async Task<UserId> OtherEnrolledUserAsync()
    {
        var other = new UserId(await _world.NewUserAsync("other"));
        await EnrollAsync(other);
        return other;
    }

    /// <summary>The production service over <paramref name="db"/>, with the gate and the viability reads its administrative operations take.</summary>
    public NativeMfaService Mfa(AppertureDbContext db) => NativeMfaComposition.Over(db, Scratch.Options(), _protector);

    public async Task<IReadOnlyList<string>> EnrollAsync(UserId id)
    {
        await using var db = Scratch.NewDb();
        var mfa = Mfa(db);
        var challenge = await mfa.BeginEnrollmentAsync(id, "emergency");
        var code = Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return await mfa.CompleteEnrollmentAsync(id, code, AuditActor.EmergencyCli);
    }

    // ---- Candidate operations ----

    /// <summary>A fresh secret, protected the way the service protects one.</summary>
    public async Task<(byte[] Plain, byte[] Protected)> NewSecretAsync()
    {
        var plain = Rfc6238Totp.GenerateSecret();
        return (plain, await _protector.ProtectAsync(plain, NativeMfaService.TotpProtectorKeyId));
    }

    public async Task<NativeMfaCandidateStageResult> TryStageAsync(UserId? user = null, TimeSpan? lifetime = null, byte[]? protectedSecret = null)
    {
        var secret = protectedSecret ?? (await NewSecretAsync()).Protected;
        await using var db = Scratch.NewDb();
        return await new SqlNativeMfaReplacementCandidateRepository(db)
            .StageAsync(user ?? UserId, secret, NativeMfaService.TotpProtectorKeyId, lifetime ?? TimeSpan.FromMinutes(10));
    }

    public async Task<NativeMfaReplacementCandidate> StageAsync(UserId? user = null, TimeSpan? lifetime = null, byte[]? protectedSecret = null)
    {
        var result = await TryStageAsync(user, lifetime, protectedSecret);
        result.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        return result.Candidate!;
    }

    public static long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

    public async Task<NativeMfaCandidateVerifyResult> VerifyAsync(string candidateId, int version, long? step = null, UserId? user = null)
    {
        await using var db = Scratch.NewDb();
        return await new SqlNativeMfaReplacementCandidateRepository(db).MarkVerifiedAsync(user ?? UserId, candidateId, version, step ?? CurrentStep());
    }

    /// <summary>Stages a candidate and verifies it; returns it with the version the switch must present.</summary>
    public async Task<(NativeMfaReplacementCandidate Candidate, int Version)> StageAndVerifyAsync(byte[]? protectedSecret = null)
    {
        var candidate = await StageAsync(protectedSecret: protectedSecret);
        var verified = await VerifyAsync(candidate.CandidateId, candidate.Version);
        verified.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        return (candidate, verified.Version!.Value);
    }

    public NativeMfaSwitchRequest Request(string candidateId, int version, string codePrefix = "sw", UserId? user = null)
    {
        var owner = user ?? UserId;
        var codes = Enumerable.Range(0, 10)
            .Select(i => new NativeMfaRecoveryCodeRecord($"{codePrefix}-{Guid.NewGuid():N}", owner, $"hash-{codePrefix}-{i}", $"salt-{i}", "test", null))
            .ToList();
        return new NativeMfaSwitchRequest(owner, candidateId, version, codes, DateTimeOffset.UtcNow);
    }

    public async Task<NativeMfaSwitchResult> SwitchAsync(NativeMfaSwitchRequest request)
    {
        await using var db = Scratch.NewDb();
        return await SwitchInTransactionAsync(db, request);
    }

    /// <summary>
    /// The persistence switch on a plain transaction of the caller's own, committed after it (rolled back when the switch throws): the
    /// switch requires the caller's open transaction and never commits on its own (S-1). The transaction runs under the context's
    /// execution strategy, as the repository's own transaction did before S-1 was enforced, so a context that retries still retries.
    /// </summary>
    public static async Task<NativeMfaSwitchResult> SwitchInTransactionAsync(
        AppertureDbContext db,
        NativeMfaSwitchRequest request,
        CancellationToken cancellationToken = default) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });

    public async Task<NativeMfaReplacementCandidate?> GetAsync(string candidateId, UserId? user = null)
    {
        await using var db = Scratch.NewDb();
        return await new SqlNativeMfaReplacementCandidateRepository(db).GetAsync(user ?? UserId, candidateId);
    }

    // ---- Observations (committed state, by plain SQL) ----

    /// <summary>Everything about the active authenticator and its recovery codes that a failed replacement must leave exactly as it was.</summary>
    public sealed record ActiveState(string Credential, string Codes);

    public async Task<ActiveState> ActiveAsync(UserId? user = null)
    {
        var id = (user ?? UserId).Value;
        var credential = await Scratch.ScalarAsync<string>($"""
            SELECT CONCAT_WS(N'|', CredentialId, CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2), ProtectorKeyId,
                   CONVERT(nvarchar(5), IsEnrolled), CONVERT(nvarchar(40), LastUsedStep), CONVERT(nvarchar(40), EnrolledAtUtc, 127),
                   CONVERT(nvarchar(40), LastVerifiedAtUtc, 127), CONVERT(nvarchar(40), CreatedAtUtc, 127))
              FROM UserTotpCredentials WHERE UserId = N'{id}';
            """);
        var codes = await Scratch.ScalarAsync<string>($"""
            SELECT STRING_AGG(CONCAT_WS(N':', Id, CodeHash, Salt, CONVERT(nvarchar(40), ConsumedAtUtc, 127)), N',') WITHIN GROUP (ORDER BY Id)
              FROM UserMfaRecoveryCodes WHERE UserId = N'{id}';
            """);
        return new ActiveState(credential ?? "<no credential>", codes ?? "<no codes>");
    }

    public Task<string> ActiveCredentialIdAsync(UserId? user = null) =>
        Scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{(user ?? UserId).Value}';");

    public Task<string> ActiveSecretHashAsync(UserId? user = null) =>
        Scratch.ScalarAsync<string>($"SELECT CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2) FROM UserTotpCredentials WHERE UserId = N'{(user ?? UserId).Value}';");

    public static string HashOf(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>The candidate rows of the user: id, status, version and the size of the stored secret (zero once superseded or consumed).</summary>
    public Task<List<Dictionary<string, object?>>> CandidateRowsAsync(UserId? user = null) =>
        Scratch.RowsAsync($"""
            SELECT CandidateId, Status, Version, DATALENGTH(ProtectedSecret) AS SecretBytes, MatchedStep
              FROM UserTotpReplacementCandidates WHERE UserId = N'{(user ?? UserId).Value}' ORDER BY CreatedUtc, CandidateId;
            """);

    public Task<int> LiveCandidateCountAsync(UserId? user = null) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpReplacementCandidates WHERE UserId = N'{(user ?? UserId).Value}' AND Status < 2;");

    public Task<int> CandidateRowCountAsync(UserId? user = null) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpReplacementCandidates WHERE UserId = N'{(user ?? UserId).Value}';");

    /// <summary>The recovery code ids currently stored, comma separated in order.</summary>
    public Task<string?> CodeIdsAsync(UserId? user = null) =>
        Scratch.ScalarAsync<string?>($"SELECT STRING_AGG(Id, N',') WITHIN GROUP (ORDER BY Id) FROM UserMfaRecoveryCodes WHERE UserId = N'{(user ?? UserId).Value}';");

    /// <summary>Makes the candidate expired by the database's clock, with one statement on the server.</summary>
    public Task ExpireAsync(string candidateId) =>
        Scratch.ExecuteAsync($"UPDATE UserTotpReplacementCandidates SET ExpiresAtUtc = DATEADD(SECOND, -2, SYSUTCDATETIME()) WHERE CandidateId = N'{candidateId}';");

    public Task<int> AuditRowsAsync(string action) => _world.AuditRowsAsync(action, UserId.Value);
}
