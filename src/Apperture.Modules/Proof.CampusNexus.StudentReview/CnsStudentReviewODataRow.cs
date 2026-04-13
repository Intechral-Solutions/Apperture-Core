using System.Text.Json.Serialization;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// OData projection for student review. Property names follow typical CNS JSON; use <see cref="JsonPropertyName"/> if tenant differs.
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

    [JsonPropertyName("dateAdded")]
    public DateTimeOffset? DateAdded { get; set; }

    [JsonPropertyName("dateLstMod")]
    public DateTimeOffset? DateLstMod { get; set; }
}
