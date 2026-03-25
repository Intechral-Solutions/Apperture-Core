using Apperture.Management.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Apperture.IntegrationTests;

/// <summary>
/// Hosts the standalone <see cref="Apperture.Management.Api"/> assembly for stub-level smoke tests only.
/// </summary>
public sealed class ManagementApiStubWebApplicationFactory : WebApplicationFactory<ManagementApiWebApplicationFactoryEntryPoint>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Testing");
}
