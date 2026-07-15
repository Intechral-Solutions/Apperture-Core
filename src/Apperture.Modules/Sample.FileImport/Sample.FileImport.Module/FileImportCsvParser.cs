using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public sealed record ParsedCustomerRow(
    int RowNumber,
    bool StructurallyValid,
    string? ExternalId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Category,
    string? Status,
    string? EffectiveDate,
    string? ParseErrorMessage);

public sealed record FixtureParseResult(
    bool StructureValid,
    string? StructureError,
    IReadOnlyList<ParsedCustomerRow> Rows);

public static class FileImportCsvParser
{
    private static readonly string[] RequiredHeaders =
    [
        "externalId",
        "firstName",
        "lastName",
        "email",
        "category",
        "status",
        "effectiveDate",
    ];

    public static FixtureParseResult Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new FixtureParseResult(false, "Fixture CSV is empty.", []);

        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
            return new FixtureParseResult(false, "Fixture CSV has no lines.", []);

        var headerCells = SplitCsvLine(lines[0]);
        if (!HeadersMatch(headerCells))
        {
            return new FixtureParseResult(
                false,
                "Fixture CSV header is invalid. Expected: " + string.Join(",", RequiredHeaders),
                []);
        }

        var rows = new List<ParsedCustomerRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            var rowNumber = i; // data row 1..n matching file line after header conceptually as 1-based data index
            var cells = SplitCsvLine(lines[i]);
            if (cells.Length != RequiredHeaders.Length)
            {
                rows.Add(new ParsedCustomerRow(
                    RowNumber: rowNumber,
                    StructurallyValid: false,
                    ExternalId: null,
                    FirstName: null,
                    LastName: null,
                    Email: null,
                    Category: null,
                    Status: null,
                    EffectiveDate: null,
                    ParseErrorMessage: $"Expected {RequiredHeaders.Length} columns, found {cells.Length}."));
                continue;
            }

            rows.Add(new ParsedCustomerRow(
                RowNumber: rowNumber,
                StructurallyValid: true,
                ExternalId: NullIfWhiteSpace(cells[0]),
                FirstName: NullIfWhiteSpace(cells[1]),
                LastName: NullIfWhiteSpace(cells[2]),
                Email: NullIfWhiteSpace(cells[3]),
                Category: NullIfWhiteSpace(cells[4]),
                Status: NullIfWhiteSpace(cells[5]),
                EffectiveDate: NullIfWhiteSpace(cells[6]),
                ParseErrorMessage: null));
        }

        return new FixtureParseResult(true, null, rows);
    }

    private static bool HeadersMatch(string[] headerCells)
    {
        if (headerCells.Length != RequiredHeaders.Length)
            return false;

        for (var i = 0; i < RequiredHeaders.Length; i++)
        {
            if (!string.Equals(headerCells[i].Trim(), RequiredHeaders[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string[] SplitCsvLine(string line) =>
        line.Split(',', StringSplitOptions.None)
            .Select(c => c.Trim())
            .ToArray();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
