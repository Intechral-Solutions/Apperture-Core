namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Query arguments for secret audit retrieval.
/// </summary>
public sealed record SecretAuditQuery(
    SecretId? SecretId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int PageSize = 50,
    string? ContinuationToken = null);
