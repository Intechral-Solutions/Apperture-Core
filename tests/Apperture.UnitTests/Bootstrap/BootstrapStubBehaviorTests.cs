using Apperture.Bootstrap.Stubs;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

/// <summary>
/// Stub behavior: read paths are graceful; mutation paths throw with clear messages.
/// </summary>
public sealed class BootstrapStubBehaviorTests
{
    [Fact]
    public async Task StubBootstrapCredentialManager_ValidateAsync_returns_failed_result_without_throwing()
    {
        var stub = new StubBootstrapCredentialManager();
        var result = await stub.ValidateAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Contains("not implemented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StubSqlConnectionBootstrapProfileManager_GetProfileAsync_returns_null_without_throwing()
    {
        var stub = new StubSqlConnectionBootstrapProfileManager();
        var result = await stub.GetProfileAsync();

        result.Should().BeNull();
    }
}
