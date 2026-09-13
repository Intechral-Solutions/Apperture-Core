using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Serializes SQL-backed integration tests that share a server and create/migrate test databases.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection
{
    public const string Name = "SqlServerIntegration";
}
