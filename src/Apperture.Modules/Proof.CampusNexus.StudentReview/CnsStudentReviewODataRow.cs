using System.Text.Json.Serialization;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// OData projection for student review. Timestamps align with CNS <c>CreatedDateTime</c> / <c>LastModifiedDateTime</c> (camelCase JSON); adjust <see cref="JsonPropertyName"/> if the tenant differs.
/// </summary>
public sealed class CnsStudentReviewODataRow
{
    public int Id { get; set; }

    [JsonPropertyName("studentNumber")]
    public string? StudentNumber { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}
