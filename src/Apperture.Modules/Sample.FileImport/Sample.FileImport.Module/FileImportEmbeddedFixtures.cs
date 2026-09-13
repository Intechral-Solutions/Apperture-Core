using System.Reflection;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public static class FileImportEmbeddedFixtures
{
    private static readonly Assembly ModuleAssembly = typeof(FileImportEmbeddedFixtures).Assembly;
    private const string ResourcePrefix = "Sample.FileImport.Fixtures.";

    public static IReadOnlyList<string> ListFixtureIds() => FileImportFixtureIds.All;

    public static bool TryLoadCsv(string fixtureId, out string csv, out string? error)
    {
        csv = "";
        error = null;

        if (string.IsNullOrWhiteSpace(fixtureId))
        {
            error = "Fixture id is required.";
            return false;
        }

        var id = fixtureId.Trim();
        if (!FileImportFixtureIds.IsKnown(id))
        {
            error = $"Unknown fixture id '{id}'. Known: {string.Join(", ", FileImportFixtureIds.All)}.";
            return false;
        }

        var resourceName = ResourcePrefix + id + ".csv";
        using var stream = ModuleAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var available = string.Join(", ", ModuleAssembly.GetManifestResourceNames());
            error = $"Embedded fixture resource '{resourceName}' was not found. Available: {available}";
            return false;
        }

        using var reader = new StreamReader(stream);
        csv = reader.ReadToEnd();
        return true;
    }
}
