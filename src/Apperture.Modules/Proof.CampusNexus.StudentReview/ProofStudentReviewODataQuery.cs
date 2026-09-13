namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Builds OData query segments for <see cref="Apperture.Connectors.CampusNexus.Models.ApiQuery"/> (unit-testable).
/// </summary>
public static class ProofStudentReviewODataQuery
{
    public const string DefaultEntitySet = "Students";

    public const string DefaultOrderBy = "LastModifiedDateTime desc";

    public const int DefaultTop = 10;

    public const string DefaultSelect =
        "Id,StudentNumber,FirstName,LastName,CreatedDateTime,LastModifiedDateTime";

    /// <summary>
    /// Returns the query string after the leading <c>?</c> (e.g. <c>$orderby=...&amp;$top=10&amp;$select=...</c>).
    /// </summary>
    public static string Build(
        string? orderBy = null,
        int? top = null,
        string? select = null)
    {
        orderBy ??= DefaultOrderBy;
        top ??= DefaultTop;
        select ??= DefaultSelect;

        var parts = new List<string>
        {
            "$orderby=" + Uri.EscapeDataString(orderBy),
            "$top=" + top.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(select))
            parts.Add("$select=" + Uri.EscapeDataString(select));

        return string.Join("&", parts);
    }
}
