using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sample.ApiPolling;
using Xunit;

namespace Apperture.UnitTests.Sample.ApiPolling;

public sealed class ConnectionBackedSampleDirectoryApiClientTests
{
    [Fact]
    public void Resolve_without_binding_or_designation_throws_instead_of_using_embedded_fallback()
    {
        var services = new ServiceCollection();
        services.AddOptions<SampleApiPollingOptions>();
        var provider = services.BuildServiceProvider();

        var client = new ConnectionBackedSampleDirectoryApiClient(provider);

        var act = () => client.ListSnapshotIds();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Embedded fallback is disabled*");
    }

    [Fact]
    public void Resolve_with_environment_flag_allows_embedded_fallback()
    {
        var previous = Environment.GetEnvironmentVariable(SampleApiPollingOptions.AllowEmbeddedFallbackEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                SampleApiPollingOptions.AllowEmbeddedFallbackEnvironmentVariable,
                "true");

            var services = new ServiceCollection();
            services.AddOptions<SampleApiPollingOptions>();
            var provider = services.BuildServiceProvider();

            var client = new ConnectionBackedSampleDirectoryApiClient(provider);
            var ids = client.ListSnapshotIds();

            ids.Should().NotBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                SampleApiPollingOptions.AllowEmbeddedFallbackEnvironmentVariable,
                previous);
        }
    }

    [Fact]
    public void Resolve_with_options_flag_allows_embedded_fallback()
    {
        var services = new ServiceCollection();
        services.AddOptions<SampleApiPollingOptions>().Configure(o => o.AllowEmbeddedFallback = true);
        var provider = services.BuildServiceProvider();

        var client = new ConnectionBackedSampleDirectoryApiClient(provider);
        var ids = client.ListSnapshotIds();

        ids.Should().NotBeEmpty();
    }
}
