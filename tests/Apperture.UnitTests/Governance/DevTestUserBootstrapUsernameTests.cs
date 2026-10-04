using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class DevTestUserBootstrapUsernameTests
{
    [Fact]
    public async Task Dev_test_user_gets_its_normalized_sign_in_name_as_username()
    {
        var users = new UsernameServiceTests.LedgerUserRepository();

        await StartAsync(users, "  Tenny ");

        var user = (await users.GetPageAsync(0, 10)).Page.Should().ContainSingle().Subject;
        user.Username.Should().Be("tenny");
        user.IdentityLinks.Single(l => l.Source == IdentitySource.Local).ExternalId.Should().Be("tenny");
    }

    [Theory]
    [InlineData("dev@local")]
    [InlineData("system")]
    public async Task Dev_test_user_is_not_created_with_a_name_that_fails_the_username_policy(string username)
    {
        var users = new UsernameServiceTests.LedgerUserRepository();

        await StartAsync(users, username);

        (await users.GetUserCountAsync()).Should().Be(0);
    }

    private static async Task StartAsync(IUserRepository users, string username)
    {
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.IsAny<RoleId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role(new RoleId(BootstrapConstants.PlatformAdminRoleId), "Platform Administrator", null, Array.Empty<PermissionDescriptor>(), true));

        var services = new ServiceCollection()
            .AddSingleton(users)
            .AddSingleton(roles.Object)
            .BuildServiceProvider();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(Environments.Development);

        var hosted = new DevTestUserBootstrapHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            environment.Object,
            Options.Create(new DevTestUserOptions { Enabled = true, Username = username, DisplayName = "Dev User" }));

        await hosted.StartAsync(CancellationToken.None);
    }
}
