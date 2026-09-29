using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class UiDevCorsTests
{
    [Fact]
    public void The_development_policy_allows_only_the_configured_origin_and_narrow_verbs()
    {
        var policy = Build("http://localhost:9001");
        policy.Origins.Should().Equal("http://localhost:9001");
        policy.Methods.Should().BeEquivalentTo("GET", "POST", "PUT", "PATCH", "DELETE");
        policy.Headers.Should().BeEquivalentTo("Content-Type", BrowserRequest.HeaderName, BackgroundActivity.HeaderName);
        policy.SupportsCredentials.Should().BeTrue();
        policy.AllowAnyOrigin.Should().BeFalse();
    }

    [Fact]
    public void A_sibling_or_https_localhost_origin_is_not_in_the_policy()
    {
        var policy = Build("http://localhost:9001");
        policy.IsOriginAllowed("http://localhost:9002").Should().BeFalse();
        policy.IsOriginAllowed("https://localhost:9001").Should().BeFalse();
    }

    [Fact]
    public async Task A_development_preflight_from_the_configured_origin_is_allowed()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.Services.AddCors(options =>
            options.AddPolicy(UiDevCors.PolicyName, policy => UiDevCors.Configure(policy, "http://localhost:9001")));
        await using var app = builder.Build();
        app.UseCors(UiDevCors.PolicyName);
        app.MapPost("/api/x", () => Results.Ok());
        await app.StartAsync();

        var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/x");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:9001");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", BrowserRequest.HeaderName);
        using var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("http://localhost:9001");
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");
    }

    [Fact]
    public void Host_program_registers_and_applies_cors_only_in_development()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs"));
        program.Should().Contain("if (builder.Environment.IsDevelopment())");
        program.Should().Contain("if (app.Environment.IsDevelopment())");
        program.Should().Contain("app.UseCors(UiDevCors.PolicyName);");
        program.Should().NotContain("AllowAnyOrigin");
        program.Should().NotContain("SetIsOriginAllowed");
        program.Should().NotContain("https://localhost:9001");
    }

    private static CorsPolicy Build(string origin)
    {
        var builder = new CorsPolicyBuilder();
        UiDevCors.Configure(builder, origin);
        return builder.Build();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }
}
