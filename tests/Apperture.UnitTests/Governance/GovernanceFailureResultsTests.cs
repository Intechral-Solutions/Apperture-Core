using System.Text.Json;
using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The HTTP contract of the typed governance outcomes (IAM-10 gate P-37, A10.2.13): one status and one stable code per type, a body
/// that carries <c>code</c> and <c>message</c>, the roles routes' legacy <c>error</c> property, and no business meaning drawn from
/// <see cref="InvalidOperationException"/> in the touched actions. The same codes are shown through the real host in
/// <c>RoleAuthorityHostHttpTests</c>.
/// </summary>
public sealed class GovernanceFailureResultsTests
{
    private static (int Status, JsonElement Body) Render(Exception exception, bool legacy = false)
    {
        var result = GovernanceFailureResults.Map(exception, legacy);
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        var json = JsonSerializer.Serialize(objectResult.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (objectResult.StatusCode!.Value, JsonDocument.Parse(json).RootElement.Clone());
    }

    public static IEnumerable<object[]> Outcomes()
    {
        var missing = new[] { new PermissionDescriptor("connection.update", new ResourceDescriptor("Connection", "instance")) };
        yield return [new AuthorizationRefusalException(AuthorizationRefusalCodes.GrantExceedsAuthority, "m", missing), 403, "grant_exceeds_authority"];
        yield return [new AuthorizationRefusalException(AuthorizationRefusalCodes.AssignmentExceedsAuthority, "m", missing), 403, "assignment_exceeds_authority"];
        yield return [new AuthorizationRefusalException(AuthorizationRefusalCodes.RemovalExceedsAuthority, "m", missing), 403, "removal_exceeds_authority"];
        yield return [new AuthorizationRefusalException(AuthorizationRefusalCodes.PlatformAdministratorRestricted, "m"), 403, "platform_administrator_restricted"];
        yield return [new RoleLifecycleException(RoleLifecycleErrorCodes.SystemRole, "m"), 409, "system_role"];
        yield return [new RoleLifecycleException(RoleLifecycleErrorCodes.AssignmentsRemain, "m", 3), 409, "assignments_remain"];
        yield return [new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "m"), 409, "duplicate_name"];
        yield return [new GovernanceConflictException(GovernanceConflictCodes.RolePermissionsUnreadable, "m"), 409, "role_permissions_unreadable"];
        yield return [new GovernanceConflictException(GovernanceConflictCodes.LastViableAdministrator, "m"), 409, "last_viable_administrator"];
        yield return [new GovernanceBusyException(), 409, "governance_operation_busy"];
        yield return [new GovernanceNotFoundException(GovernanceNotFoundKinds.User, "u"), 404, "user_not_found"];
        yield return [new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, "r"), 404, "role_not_found"];
        yield return [new GovernanceValidationException(GovernanceValidationCodes.InvalidPermission, "m"), 400, "invalid_permission"];
        yield return [new GovernanceValidationException(GovernanceValidationCodes.RoleNameRequired, "m"), 400, "role_name_required"];
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void Each_typed_outcome_has_one_status_and_a_stable_code(Exception exception, int status, string code)
    {
        foreach (var legacy in new[] { false, true })
        {
            var (actualStatus, body) = Render(exception, legacy);

            actualStatus.Should().Be(status);
            body.GetProperty("code").GetString().Should().Be(code);
            body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            body.TryGetProperty("error", out var error).Should().Be(legacy);
            if (legacy)
                error.GetString().Should().Be(code, "the roles routes keep their error property, with the same value");
            GovernanceFailureResults.CodeOf(exception).Should().Be(code);
            GovernanceFailureResults.IsGovernanceOutcome(exception).Should().BeTrue();
        }
    }

    [Fact]
    public void An_authorization_refusal_body_lists_the_missing_identities_and_the_full_count()
    {
        var missing = Enumerable.Range(0, 25).Select(i => new PermissionDescriptor($"x.{i:00}", new ResourceDescriptor("Thing", "instance"))).ToList();

        var (_, body) = Render(new AuthorizationRefusalException(AuthorizationRefusalCodes.GrantExceedsAuthority, "m", missing));

        body.GetProperty("missingCount").GetInt32().Should().Be(25);
        body.GetProperty("missing").GetArrayLength().Should().Be(20);
        body.GetProperty("missing")[0].GetString().Should().Be("x.00@Thing:instance");
    }

    [Fact]
    public void An_assignments_remain_body_carries_the_count()
    {
        var (_, body) = Render(new RoleLifecycleException(RoleLifecycleErrorCodes.AssignmentsRemain, "m", 3));

        body.GetProperty("assignmentCount").GetInt32().Should().Be(3);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(AuditInvariantException))]
    public void An_InvalidOperationException_is_never_a_governance_outcome_so_it_reaches_the_host_as_an_internal_error(Type type)
    {
        var exception = (Exception)Activator.CreateInstance(type, "Role r not found.")!;

        GovernanceFailureResults.IsGovernanceOutcome(exception).Should().BeFalse();
        var map = () => GovernanceFailureResults.Map(exception);
        map.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(typeof(AuditOutcomeUnknownException))]
    [InlineData(typeof(AuditCommittedResultUnavailableException))]
    public void The_audit_outcome_exceptions_are_not_business_outcomes_and_do_not_derive_from_InvalidOperationException(Type type)
    {
        typeof(InvalidOperationException).IsAssignableFrom(type).Should().BeFalse();
        var instance = type == typeof(AuditOutcomeUnknownException)
            ? (Exception)new AuditOutcomeUnknownException("op")
            : new AuditCommittedResultUnavailableException("op", new Exception("inner"));
        GovernanceFailureResults.IsGovernanceOutcome(instance).Should().BeFalse();
    }

    // ---- The touched actions contain no catch of InvalidOperationException ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Controller(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Api", "Controllers", name));

    [Fact]
    public void The_roles_controller_reads_no_business_meaning_from_InvalidOperationException()
    {
        var source = Regex.Replace(Controller("RolesController.cs"), @"^\s*//.*$", "", RegexOptions.Multiline);

        source.Should().NotContain("catch (InvalidOperationException");
        source.Should().NotContain("InvalidOperationException invalid");
        source.Should().NotContain("Contains(\"not found\"");
    }

    [Theory]
    [InlineData("AssignRole")]
    [InlineData("RemoveRole")]
    public void The_user_role_actions_catch_only_the_typed_outcomes(string action)
    {
        var source = Controller("UsersController.cs");
        var start = source.IndexOf($"public async Task<IActionResult> {action}(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        var body = source[start..(end < 0 ? source.Length : end)];

        body.Should().NotContain("InvalidOperationException");
        body.Should().Contain("GovernanceFailureResults.IsGovernanceOutcome");
    }

    [Fact]
    public void The_service_methods_the_controllers_call_throw_no_InvalidOperationException_for_a_business_outcome()
    {
        var path = Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "UserManagementService.cs");
        var text = File.ReadAllText(path);
        var roleSection = text[text.IndexOf("public async ValueTask SetRolePermissionsAsync(", StringComparison.Ordinal)..text.IndexOf("public async ValueTask<UserId> CreateUserAsync(", StringComparison.Ordinal)];
        var assignSection = text[text.IndexOf("public async ValueTask AssignRoleAsync(", StringComparison.Ordinal)..text.IndexOf("public async ValueTask SetLocalPasswordAsync(", StringComparison.Ordinal)];

        // The repository's duplicate-name signal is caught here and turned into the typed conflict; nothing else is thrown or read.
        Regex.Matches(roleSection, "throw new InvalidOperationException").Should().BeEmpty();
        Regex.Matches(assignSection, "throw new InvalidOperationException").Should().BeEmpty();
        assignSection.Should().NotContain("not found\")");
    }
}
