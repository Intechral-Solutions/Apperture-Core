using System.Text.Json;
using Apperture.Core.Application.Notifications.Cutover;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlExpectedRunMissingCutoverSignOffStore : IExpectedRunMissingCutoverSignOffStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppertureDbContext _db;

    public SqlExpectedRunMissingCutoverSignOffStore(AppertureDbContext db) => _db = db;

    public async ValueTask<ExpectedRunMissingCutoverSignOff?> GetLatestAsync(
        string integrationId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ExpectedRunMissingCutoverSignOffs.AsNoTracking()
            .Where(x => x.IntegrationId == integrationId && x.NotificationDefinitionId == notificationDefinitionId)
            .OrderByDescending(x => x.ReviewedAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToModel(row);
    }

    public async ValueTask<ExpectedRunMissingCutoverSignOff> AppendAsync(
        ExpectedRunMissingCutoverSignOff signOff,
        CancellationToken cancellationToken = default)
    {
        var entity = new ExpectedRunMissingCutoverSignOffEntity
        {
            Id = signOff.Id,
            IntegrationId = signOff.IntegrationId,
            NotificationDefinitionId = signOff.NotificationDefinitionId,
            Status = signOff.Status,
            ReviewedAtUtc = signOff.ReviewedAtUtc,
            ReviewedByActorId = signOff.ReviewedByActorId,
            ReviewedByDisplayName = signOff.ReviewedByDisplayName,
            ParityAcceptableAtReview = signOff.ParityAcceptableAtReview,
            ConfigurationWarningCodesJson = Serialize(signOff.ConfigurationWarningCodes),
            BlockingFindingsJson = Serialize(signOff.BlockingFindings),
            NonBlockingFindingsJson = Serialize(signOff.NonBlockingFindings),
            OperatorNote = signOff.OperatorNote,
            CreatedAtUtc = signOff.CreatedAtUtc,
            UpdatedAtUtc = signOff.UpdatedAtUtc,
        };

        _db.ExpectedRunMissingCutoverSignOffs.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return signOff;
    }

    private static ExpectedRunMissingCutoverSignOff ToModel(ExpectedRunMissingCutoverSignOffEntity e) =>
        new(
            e.Id,
            e.IntegrationId,
            e.NotificationDefinitionId,
            e.Status,
            e.ReviewedAtUtc,
            e.ReviewedByActorId,
            e.ReviewedByDisplayName,
            e.ParityAcceptableAtReview,
            Deserialize(e.ConfigurationWarningCodesJson),
            Deserialize(e.BlockingFindingsJson),
            Deserialize(e.NonBlockingFindingsJson),
            e.OperatorNote,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);

    private static string Serialize(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values, JsonOptions);

    private static IReadOnlyList<string> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
