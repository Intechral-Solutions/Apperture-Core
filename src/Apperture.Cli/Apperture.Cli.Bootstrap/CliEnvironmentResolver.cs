namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Resolves the effective environment name for the CLI host.
/// Used so bootstrap credential handling follows production vs development rules.
/// </summary>
public static class CliEnvironmentResolver
{
    /// <summary>
    /// Environment variable names checked in order. Same as ASP.NET Core when not in a web context.
    /// </summary>
    public const string DotnetEnvironmentVariable = "DOTNET_ENVIRONMENT";
    public const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";

    /// <summary>
    /// Gets the effective environment name from DOTNET_ENVIRONMENT or ASPNETCORE_ENVIRONMENT.
    /// Returns "Production" when unset so production safeguards apply by default.
    /// </summary>
    public static string GetEnvironmentName()
    {
        var value = Environment.GetEnvironmentVariable(DotnetEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable(AspNetCoreEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? "Production" : value.Trim();
    }
}
