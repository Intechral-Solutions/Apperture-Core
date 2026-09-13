namespace Sample.FileImport.Contracts;

public static class FileImportFixtureIds
{
    public const string CustomersValid = "customers-valid";
    public const string CustomersMixed = "customers-mixed";
    public const string CustomersInvalidStructure = "customers-invalid-structure";
    public const string CustomersAllBad = "customers-all-bad";

    public static IReadOnlyList<string> All { get; } =
    [
        CustomersValid,
        CustomersMixed,
        CustomersInvalidStructure,
        CustomersAllBad,
    ];

    public static bool IsKnown(string? fixtureId) =>
        !string.IsNullOrWhiteSpace(fixtureId)
        && All.Contains(fixtureId.Trim(), StringComparer.OrdinalIgnoreCase);
}
