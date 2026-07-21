using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unisa.CampusNexus.PaymentPlans.Persistence;

/// <summary>
/// Reads/writes <c>UnisaPaymentPlanArrangements</c> and related UNISA integration tables via <see cref="IDbConnection"/>.
/// </summary>
public sealed class UnisaPaymentPlanArrangementRepository
{
    private readonly ILogger<UnisaPaymentPlanArrangementRepository> _logger;

    public UnisaPaymentPlanArrangementRepository(ILogger<UnisaPaymentPlanArrangementRepository>? logger = null) =>
        _logger = logger ?? NullLogger<UnisaPaymentPlanArrangementRepository>.Instance;

    public async Task<UnisaPaymentPlanArrangementRow?> GetCurrentArrangementAsync(
        IDbConnection connection,
        string externalKey,
        string schoolFundId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1) ArrangementId, ExternalKey, SchoolFundId, CnsStudentId, StudentNumber,
                   InSchoolPaymentPlanId, OutOfSchoolPaymentPlanId, SourceKind, IsCurrent
            FROM UnisaPaymentPlanArrangements
            WHERE ExternalKey = @ek AND SchoolFundId = @sf AND IsCurrent = 1
            """;

        await using var cmd = SqlCommand(connection, sql);
        cmd.Parameters.AddWithValue("@ek", externalKey);
        cmd.Parameters.AddWithValue("@sf", schoolFundId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return MapRow(reader);
    }

    public async Task SupersedeCurrentArrangementsAsync(
        IDbConnection connection,
        string externalKey,
        string schoolFundId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE UnisaPaymentPlanArrangements
            SET IsCurrent = 0, SupersededAt = @now
            WHERE ExternalKey = @ek AND SchoolFundId = @sf AND IsCurrent = 1
            """;

        await using var cmd = SqlCommand(connection, sql);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("@ek", externalKey);
        cmd.Parameters.AddWithValue("@sf", schoolFundId);
        int n = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Superseded {Count} arrangement row(s) for ExternalKey={ExternalKey} SchoolFundId={SchoolFundId}", n, externalKey, schoolFundId);
    }

    public async Task InsertArrangementAsync(
        IDbConnection connection,
        UnisaPaymentPlanArrangementRow row,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO UnisaPaymentPlanArrangements (
                ArrangementId, ExternalKey, SchoolFundId, CnsStudentId, StudentNumber,
                InSchoolPaymentPlanId, OutOfSchoolPaymentPlanId, SourceKind, IsCurrent, CreatedAt, SupersededAt)
            VALUES (
                @id, @ek, @sf, @studentId, @studentNumber,
                @inPlan, @outPlan, @source, @current, SYSUTCDATETIME(), NULL)
            """;

        await using var cmd = SqlCommand(connection, sql);
        cmd.Parameters.AddWithValue("@id", row.ArrangementId);
        cmd.Parameters.AddWithValue("@ek", row.ExternalKey);
        cmd.Parameters.AddWithValue("@sf", row.SchoolFundId);
        cmd.Parameters.AddWithValue("@studentId", (object?)row.CnsStudentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@studentNumber", (object?)row.StudentNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inPlan", (object?)row.InSchoolPaymentPlanId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@outPlan", (object?)row.OutOfSchoolPaymentPlanId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", row.SourceKind);
        cmd.Parameters.AddWithValue("@current", row.IsCurrent);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SqlCommand SqlCommand(IDbConnection connection, string sql)
    {
        if (connection is not SqlConnection sqlConnection)
            throw new InvalidOperationException("UNISA payment plan persistence requires Microsoft SQL Server (SqlConnection).");
        return new SqlCommand(sql, sqlConnection);
    }

    private static UnisaPaymentPlanArrangementRow MapRow(SqlDataReader r) =>
        new(
            r.GetGuid(r.GetOrdinal("ArrangementId")),
            r.GetString(r.GetOrdinal("ExternalKey")),
            r.GetString(r.GetOrdinal("SchoolFundId")),
            r.IsDBNull(r.GetOrdinal("CnsStudentId")) ? null : r.GetInt32(r.GetOrdinal("CnsStudentId")),
            r.IsDBNull(r.GetOrdinal("StudentNumber")) ? null : r.GetString(r.GetOrdinal("StudentNumber")),
            r.IsDBNull(r.GetOrdinal("InSchoolPaymentPlanId")) ? null : r.GetInt32(r.GetOrdinal("InSchoolPaymentPlanId")),
            r.IsDBNull(r.GetOrdinal("OutOfSchoolPaymentPlanId")) ? null : r.GetInt32(r.GetOrdinal("OutOfSchoolPaymentPlanId")),
            r.GetString(r.GetOrdinal("SourceKind")),
            r.GetBoolean(r.GetOrdinal("IsCurrent")));
}

