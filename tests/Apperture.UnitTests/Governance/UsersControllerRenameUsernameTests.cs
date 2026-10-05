using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary><c>PUT /api/users/{id}/username</c>: status codes and bodies for every outcome of an administrator rename.</summary>
public sealed class UsersControllerRenameUsernameTests
{
    private const string TargetUserId = "usrrenametest00000001";

    private static (UsersController Controller, Mock<IUserManagementService> Service) NewController()
    {
        var service = new Mock<IUserManagementService>(MockBehavior.Strict);
        var controller = new UsersController(
            service.Object,
            Mock.Of<ILocalCredentialRepository>(),
            Mock.Of<IRoleProvider>());
        return (controller, service);
    }

    private static void Returns(Mock<IUserManagementService> service, UsernameRenameResult result) =>
        service.Setup(s => s.RenameUsernameAsync(new UserId(TargetUserId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(result));

    private static void Throws(Mock<IUserManagementService> service, Exception exception) =>
        service.Setup(s => s.RenameUsernameAsync(new UserId(TargetUserId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(exception);

    [Theory]
    [InlineData(UsernameChangeOutcome.Assigned, "assigned")]
    [InlineData(UsernameChangeOutcome.Reclaimed, "reclaimed")]
    [InlineData(UsernameChangeOutcome.Unchanged, "unchanged")]
    public async Task Rename_returns_200_with_the_canonical_username_and_outcome(UsernameChangeOutcome outcome, string expected)
    {
        var (controller, service) = NewController();
        Returns(service, new UsernameRenameResult(outcome, "alice", "alice.renamed"));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("Alice.Renamed"), CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RenameUsernameResponse>().Subject;
        body.UserId.Should().Be(TargetUserId);
        body.Username.Should().Be("alice.renamed");
        body.Outcome.Should().Be(expected);
        service.Verify(s => s.RenameUsernameAsync(new UserId(TargetUserId), "Alice.Renamed", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_without_a_username_is_400_invalid_and_never_reaches_the_service(string? username)
    {
        var (controller, service) = NewController();

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest(username!), CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString().Should().Contain(UsernameErrorCodes.Invalid);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rename_without_user_manage_is_forbidden()
    {
        var (controller, service) = NewController();
        Throws(service, new UnauthorizedAccessException("Not authorized to manage users."));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("alice.renamed"), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(UsernameErrorCodes.Taken)]
    [InlineData(UsernameErrorCodes.ConcurrentChange)]
    public async Task Rename_to_a_name_that_is_taken_or_changed_underneath_is_409_with_its_code(string code)
    {
        var (controller, service) = NewController();
        Throws(service, new UsernameException(code, "refused"));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("bob"), CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>().Subject.Value!.ToString().Should().Contain(code);
    }

    [Theory]
    [InlineData(UsernameErrorCodes.Invalid)]
    [InlineData(UsernameErrorCodes.Reserved)]
    public async Task Rename_to_an_invalid_or_reserved_name_is_400_with_its_code(string code)
    {
        var (controller, service) = NewController();
        Throws(service, new UsernameException(code, "refused"));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("system"), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value!.ToString().Should().Contain(code);
    }

    [Fact]
    public async Task Rename_of_recovery_access_is_409_unsafe_operation()
    {
        var (controller, service) = NewController();
        Throws(service, new EmergencyAccessException(EmergencyAccessErrorCodes.UnsafeOperation, "Recovery Access is system-managed."));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("someone.else"), CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>().Subject.Value!.ToString()
            .Should().Contain(EmergencyAccessErrorCodes.UnsafeOperation);
    }

    [Fact]
    public async Task Rename_of_a_missing_user_is_404()
    {
        var (controller, service) = NewController();
        Throws(service, new InvalidOperationException($"User {TargetUserId} not found."));

        var result = await controller.RenameUsername(TargetUserId, new RenameUsernameRequest("someone.else"), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
