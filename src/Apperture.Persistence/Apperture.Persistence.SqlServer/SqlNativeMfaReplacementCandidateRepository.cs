using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="INativeMfaReplacementCandidateRepository" />
/// <remarks>
/// <para><b>Every decision is made by the database as it writes.</b> A candidate becomes verified, or the active secret is replaced,
/// by one statement whose condition names the candidate's id, owner, state, version, expiry (by <c>SYSUTCDATETIME()</c>) and the
/// credential it was staged against. A read made earlier by the caller is only ever advice: nothing here trusts it.</para>
/// <para><b>Concurrency.</b> Staging, verifying and switching first take the user's credential row with
/// <c>UPDLOCK, HOLDLOCK</c> (which takes locks under READ_COMMITTED_SNAPSHOT as well), so two of them for one user never
/// interleave their reads and writes; the candidate row is then taken the same way. Operator input happens between calls, never
/// inside one. The filtered unique index on live candidates remains the last arbiter if a lock were ever bypassed.</para>
/// <para><b>Failure writes nothing.</b> When a conditional write changes no row, the cause is read under the same locks and
/// reported; no statement after the failed one runs, so a refused switch leaves the active credential and its recovery codes
/// untouched even inside a caller's transaction that goes on to commit.</para>
/// </remarks>
public sealed class SqlNativeMfaReplacementCandidateRepository : INativeMfaReplacementCandidateRepository
{
    /// <summary>The longest a candidate may live. A candidate is an operator's few minutes of work, not a standing credential.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    private const int Staged = (int)NativeMfaCandidateStatus.Staged;
    private const int Verified = (int)NativeMfaCandidateStatus.Verified;
    private const int Consumed = (int)NativeMfaCandidateStatus.Consumed;

    private const string ReadColumns =
        """
        [CandidateId], [UserId], [ProtectedSecret], [ProtectorKeyId], [StagedAgainstCredentialId], [Status], [Version],
        [CreatedUtc], [ExpiresAtUtc], [VerifiedUtc], [MatchedStep],
        CAST(CASE WHEN [ExpiresAtUtc] <= SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS [IsExpired]
        """;

    private readonly AppertureDbContext _db;

    public SqlNativeMfaReplacementCandidateRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<NativeMfaCandidateStageResult> StageAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        if (protectedSecret.Length == 0)
            throw new ArgumentException("A candidate needs a protected secret.", nameof(protectedSecret));
        ArgumentException.ThrowIfNullOrWhiteSpace(protectorKeyId);
        if (protectorKeyId.Length > 128)
            throw new ArgumentException("A protector key id is at most 128 characters.", nameof(protectorKeyId));
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > MaxLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "A candidate lives between one second and a day.");

        var seconds = (int)lifetime.TotalSeconds;
        var candidateId = NewId();
        var user = userId.Value;

        return await InTransactionAsync(
            async () =>
            {
                var active = await LockActiveCredentialAsync(user, cancellationToken);
                if (active is null)
                    return new NativeMfaCandidateStageResult(NativeMfaCandidateOutcome.NoActiveEnrollment, null);

                // Lazy purge of history, then the live candidate (if any) is superseded and loses its secret: it can never be switched.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM [UserTotpReplacementCandidates] WHERE [UserId] = {user} AND [Status] >= 2;",
                    cancellationToken);
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [UserTotpReplacementCandidates]
                       SET [Status] = 2, [Version] = [Version] + 1, [ProtectedSecret] = 0x
                     WHERE [UserId] = {user} AND [Status] < 2;
                    """,
                    cancellationToken);
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DECLARE @now datetime2 = SYSUTCDATETIME();
                    INSERT INTO [UserTotpReplacementCandidates]
                        ([CandidateId], [UserId], [ProtectedSecret], [ProtectorKeyId], [StagedAgainstCredentialId], [Status], [Version],
                         [CreatedUtc], [ExpiresAtUtc], [VerifiedUtc], [MatchedStep])
                    VALUES
                        ({candidateId}, {user}, {protectedSecret}, {protectorKeyId}, {active}, 0, 1,
                         @now, DATEADD(SECOND, {seconds}, @now), NULL, NULL);
                    """,
                    cancellationToken);

                var row = await ReadAsync(user, candidateId, locking: false, cancellationToken)
                    ?? throw new InvalidOperationException("The staged candidate could not be read back.");
                return new NativeMfaCandidateStageResult(NativeMfaCandidateOutcome.Applied, ToDomain(row));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaReplacementCandidate?> GetAsync(UserId userId, string candidateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        var row = await ReadAsync(userId.Value, candidateId, locking: false, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaCandidateVerifyResult> MarkVerifiedAsync(
        UserId userId,
        string candidateId,
        int expectedVersion,
        long matchedStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        var user = userId.Value;

        return await InTransactionAsync(
            async () =>
            {
                var active = await LockActiveCredentialAsync(user, cancellationToken);
                if (active is null)
                    return new NativeMfaCandidateVerifyResult(NativeMfaCandidateOutcome.NoActiveEnrollment, null);

                var changed = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [UserTotpReplacementCandidates]
                       SET [Status] = 1, [Version] = [Version] + 1, [VerifiedUtc] = SYSUTCDATETIME(), [MatchedStep] = {matchedStep}
                     WHERE [CandidateId] = {candidateId} AND [UserId] = {user}
                       AND [Status] = 0 AND [Version] = {expectedVersion}
                       AND [ExpiresAtUtc] > SYSUTCDATETIME()
                       AND [StagedAgainstCredentialId] = {active};
                    """,
                    cancellationToken);
                if (changed == 1)
                {
                    var row = await ReadAsync(user, candidateId, locking: false, cancellationToken)
                        ?? throw new InvalidOperationException("The verified candidate could not be read back.");
                    return new NativeMfaCandidateVerifyResult(NativeMfaCandidateOutcome.Applied, row.Version);
                }

                return new NativeMfaCandidateVerifyResult(
                    await DiagnoseAsync(user, candidateId, expectedVersion, active, switching: false, cancellationToken),
                    null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaSwitchResult> SwitchAsync(NativeMfaSwitchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CandidateId);
        ArgumentNullException.ThrowIfNull(request.RecoveryCodes);
        if (request.RecoveryCodes.Count == 0)
            throw new ArgumentException("A switch issues a new set of recovery codes.", nameof(request));
        var user = request.UserId.Value;
        if (request.RecoveryCodes.Any(c => !string.Equals(c.UserId.Value, user, StringComparison.Ordinal)))
            throw new ArgumentException("Every recovery code belongs to the user whose authenticator is switched.", nameof(request));

        var newCredentialId = NewId();
        return await InTransactionAsync(
            async () =>
            {
                var active = await LockActiveCredentialAsync(user, cancellationToken);
                if (active is null)
                    return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.NoActiveEnrollment, null);

                // One statement decides: it reads the candidate (locked) and the active row, and writes only when every condition holds.
                var switched = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE t
                       SET t.[ProtectedSecret] = c.[ProtectedSecret], t.[ProtectorKeyId] = c.[ProtectorKeyId],
                           t.[LastUsedStep] = c.[MatchedStep], t.[EnrolledAtUtc] = {request.SwitchedAtUtc},
                           t.[LastVerifiedAtUtc] = {request.SwitchedAtUtc}, t.[CredentialId] = {newCredentialId}
                      FROM [UserTotpCredentials] AS t
                     INNER JOIN [UserTotpReplacementCandidates] AS c WITH (UPDLOCK, HOLDLOCK) ON c.[UserId] = t.[UserId]
                     WHERE t.[UserId] = {user} AND t.[IsEnrolled] = 1 AND t.[CredentialId] = {active}
                       AND c.[CandidateId] = {request.CandidateId}
                       AND c.[Status] = 1 AND c.[Version] = {request.ExpectedVersion}
                       AND c.[ExpiresAtUtc] > SYSUTCDATETIME()
                       AND c.[StagedAgainstCredentialId] = t.[CredentialId];
                    """,
                    cancellationToken);
                if (switched != 1)
                {
                    return new NativeMfaSwitchResult(
                        await DiagnoseAsync(user, request.CandidateId, request.ExpectedVersion, active, switching: true, cancellationToken),
                        null);
                }

                // The same transaction consumes the candidate (it keeps no secret) and replaces the recovery codes. The locks taken
                // above are still held, so a row count other than the expected one is not a race: it fails the whole unit.
                var consumed = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [UserTotpReplacementCandidates]
                       SET [Status] = 3, [Version] = [Version] + 1, [ProtectedSecret] = 0x
                     WHERE [CandidateId] = {request.CandidateId} AND [UserId] = {user} AND [Status] = 1;
                    """,
                    cancellationToken);
                if (consumed != 1)
                    throw new InvalidOperationException("The switched candidate could not be consumed.");

                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM [UserMfaRecoveryCodes] WHERE [UserId] = {user};",
                    cancellationToken);
                foreach (var code in request.RecoveryCodes)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO [UserMfaRecoveryCodes] ([Id], [UserId], [CodeHash], [Salt], [Algorithm], [CreatedAtUtc], [ConsumedAtUtc])
                        VALUES ({code.Id}, {user}, {code.CodeHash}, {code.Salt}, {code.Algorithm}, {request.SwitchedAtUtc}, {code.ConsumedAtUtc});
                        """,
                        cancellationToken);
                }

                return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.Applied, newCredentialId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> DiscardAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [UserTotpReplacementCandidates] WHERE [UserId] = {userId.Value};",
            cancellationToken);

    /// <summary>
    /// Takes the user's enrolled credential row (<c>UPDLOCK, HOLDLOCK</c>) and returns its identity, or null when the user has none.
    /// First in the lock order, so every candidate operation of one user is serialized on it.
    /// </summary>
    private async Task<string?> LockActiveCredentialAsync(string user, CancellationToken cancellationToken)
    {
        var ids = await _db.Database
            .SqlQuery<string>($"SELECT [CredentialId] AS [Value] FROM [UserTotpCredentials] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {user} AND [IsEnrolled] = 1")
            .ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }

    private async Task<CandidateRow?> ReadAsync(string user, string candidateId, bool locking, CancellationToken cancellationToken)
    {
        // The column list is text, not a value: it is joined in as text and only the ids are parameters.
        var sql = "SELECT " + ReadColumns + " FROM [UserTotpReplacementCandidates]"
                  + (locking ? " WITH (UPDLOCK, HOLDLOCK)" : "")
                  + " WHERE [CandidateId] = @candidateId AND [UserId] = @user";
        var rows = await _db.Database
            .SqlQueryRaw<CandidateRow>(
                sql,
                new Microsoft.Data.SqlClient.SqlParameter("@candidateId", candidateId),
                new Microsoft.Data.SqlClient.SqlParameter("@user", user))
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>
    /// Names why a conditional write changed no row, from the locked current state. Nothing is written. The order is fixed: a
    /// candidate that is gone or superseded or consumed is reported as that first; then expiry; then a stale version; then the
    /// state the operation needed (staged to verify, verified to switch); last, the credential it was staged against.
    /// </summary>
    private async Task<NativeMfaCandidateOutcome> DiagnoseAsync(
        string user,
        string candidateId,
        int expectedVersion,
        string activeCredentialId,
        bool switching,
        CancellationToken cancellationToken)
    {
        var row = await ReadAsync(user, candidateId, locking: true, cancellationToken);
        if (row is null)
            return NativeMfaCandidateOutcome.NotFound;
        if (row.Status == (int)NativeMfaCandidateStatus.Superseded)
            return NativeMfaCandidateOutcome.Superseded;
        if (row.Status == Consumed)
            return NativeMfaCandidateOutcome.Consumed;
        if (row.IsExpired)
            return NativeMfaCandidateOutcome.Expired;
        if (row.Version != expectedVersion)
            return NativeMfaCandidateOutcome.VersionMismatch;
        if (switching && row.Status == Staged)
            return NativeMfaCandidateOutcome.NotVerified;
        if (!switching && row.Status == Verified)
            return NativeMfaCandidateOutcome.AlreadyVerified;
        if (!string.Equals(row.StagedAgainstCredentialId, activeCredentialId, StringComparison.Ordinal))
            return NativeMfaCandidateOutcome.ActiveEnrollmentChanged;

        // Every condition the statement named holds now. Under the locks held that cannot follow a statement that changed no row.
        throw new InvalidOperationException("A conditional candidate write changed no row although its conditions hold.");
    }

    /// <summary>Joins the caller's transaction when there is one; otherwise runs as one transaction (with the retry strategy production configures).</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
            return await work();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static NativeMfaReplacementCandidate ToDomain(CandidateRow row) => new(
        row.CandidateId,
        new UserId(row.UserId),
        row.ProtectedSecret,
        row.ProtectorKeyId,
        row.StagedAgainstCredentialId,
        (NativeMfaCandidateStatus)row.Status,
        row.Version,
        Utc(row.CreatedUtc),
        Utc(row.ExpiresAtUtc),
        row.VerifiedUtc is null ? null : Utc(row.VerifiedUtc.Value),
        row.MatchedStep,
        row.IsExpired);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>The shape of a candidate row read by raw SQL; the columns are named exactly as the table's.</summary>
    private sealed class CandidateRow
    {
        public string CandidateId { get; set; } = "";
        public string UserId { get; set; } = "";
        public byte[] ProtectedSecret { get; set; } = [];
        public string ProtectorKeyId { get; set; } = "";
        public string StagedAgainstCredentialId { get; set; } = "";
        public int Status { get; set; }
        public int Version { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? VerifiedUtc { get; set; }
        public long? MatchedStep { get; set; }
        public bool IsExpired { get; set; }
    }
}
