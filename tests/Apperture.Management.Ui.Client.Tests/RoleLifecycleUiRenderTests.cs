using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Governance;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Renders the governance role pages and drives the same handlers their lifecycle controls call.
/// </summary>
public sealed class RoleLifecycleUiRenderTests
{
    private const string RoleId = "role-abc";
    private const string RoleName = "Operators";

    [Fact]
    public async Task Role_list_offers_create_when_the_operator_can_manage_roles()
    {
        await using var rig = await RenderListAsync(canManage: true);

        rig.Html.Should().Contain("href=\"/config/governance/roles/new\"");
        rig.Html.Should().Contain(">Create role<");
    }

    [Fact]
    public async Task Role_list_hides_create_without_role_manage()
    {
        await using var rig = await RenderListAsync(canManage: false);

        rig.Html.Should().NotContain("/config/governance/roles/new");
        rig.Html.Should().NotContain(">Create role<");
    }

    [Fact]
    public void Create_role_route_is_literal_and_detail_stays_parameterized()
    {
        var create = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Governance/RoleNew.razor");
        var detail = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Governance/RoleDetail.razor");

        create.Should().Contain("@page \"/config/governance/roles/new\"");
        detail.Should().Contain("@page \"/config/governance/roles/{RoleId}\"");
    }

    [Fact]
    public async Task Create_custom_role_navigates_to_the_new_role()
    {
        await using var rig = await RenderCreateAsync(canManage: true, canAssignPermissions: true);
        rig.Client.Setup(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto("role-created", RoleName, system: false, assignmentCount: 0, description: "Day shift"));
        rig.Page.SetCreateDraft(RoleName, "Day shift");

        await rig.Do(rig.Page.SubmitCreateAsync);

        rig.Client.Verify(c => c.CreateRoleAsync(
            It.Is<CreateRoleRequest>(r => r.Name == RoleName && r.Description == "Day shift" && r.Permissions == null),
            It.IsAny<CancellationToken>()), Times.Once);
        rig.Nav.Navigations.Should().Contain(n => n.Uri.EndsWith("/config/governance/roles/role-created", StringComparison.Ordinal));
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Success && t.Message == "Role created.");
    }

    [Fact]
    public async Task Create_with_initial_permission_sends_that_grant()
    {
        await using var rig = await RenderCreateAsync(canManage: true, canAssignPermissions: true);
        rig.Html.Should().Contain("user.read");
        rig.Page.ToggleInitialPermission("user.read|User|instance");
        rig.Client.Setup(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto("role-created", RoleName, system: false, assignmentCount: 0));
        rig.Page.SetCreateDraft(RoleName, null);

        await rig.Do(rig.Page.SubmitCreateAsync);

        rig.Client.Verify(c => c.CreateRoleAsync(
            It.Is<CreateRoleRequest>(r =>
                r.Permissions != null
                && r.Permissions.Count == 1
                && r.Permissions[0].Action == "user.read"
                && r.Permissions[0].ResourceType == "User"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_without_permission_assign_sends_no_grants()
    {
        await using var rig = await RenderCreateAsync(canManage: true, canAssignPermissions: false);
        rig.Html.Should().Contain("id=\"role-create-empty-grants\"");
        rig.Html.Should().Contain("without permissions");
        rig.Html.Should().NotContain("user.read");
        rig.Client.Setup(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto("role-readers", "Readers", system: false, assignmentCount: 0));
        rig.Page.SetCreateDraft("Readers", "   ");

        await rig.Do(rig.Page.SubmitCreateAsync);

        rig.Client.Verify(c => c.CreateRoleAsync(
            It.Is<CreateRoleRequest>(r => r.Name == "Readers" && r.Description == null && r.Permissions == null),
            It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Verify(c => c.GetGovernanceDescriptorsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_duplicate_name_shows_conflict_and_stays()
    {
        await using var rig = await RenderCreateAsync(canManage: true, canAssignPermissions: false);
        rig.Client.Setup(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RoleLifecycleConflictException(RoleLifecycleConflictException.DuplicateName, "duplicate"));
        rig.Page.SetCreateDraft(RoleName, null);

        await rig.Do(rig.Page.SubmitCreateAsync);

        rig.Html.Should().Contain("A role with this name already exists.");
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
    }

    [Fact]
    public async Task Create_requires_a_name()
    {
        await using var rig = await RenderCreateAsync(canManage: true, canAssignPermissions: false);
        rig.Page.SetCreateDraft("   ", null);

        await rig.Do(rig.Page.SubmitCreateAsync);

        rig.Html.Should().Contain("Role name is required.");
        rig.Client.Verify(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_page_forbids_an_operator_who_cannot_manage_roles()
    {
        await using var rig = await RenderCreateAsync(canManage: false, canAssignPermissions: true);

        rig.Html.Should().Contain("You do not have permission to create roles.");
        await rig.Do(rig.Page.SubmitCreateAsync);
        rig.Client.Verify(c => c.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_custom_role_metadata_saves_name_and_description()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true);
        rig.Html.Should().Contain("id=\"role-name\"");
        rig.Html.Should().Contain("for=\"role-name\"");
        rig.Html.Should().Contain("for=\"role-description\"");
        rig.Client.Setup(c => c.UpdateRoleMetadataAsync(RoleId, It.IsAny<UpdateRoleMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto(RoleId, "Night operators", system: false, assignmentCount: 0, description: "After hours"));
        rig.Page.SetMetadataDraft("  Night operators  ", "  After hours  ");

        await rig.Do(rig.Page.SaveMetadataAsync);

        rig.Client.Verify(c => c.UpdateRoleMetadataAsync(
            RoleId,
            It.Is<UpdateRoleMetadataRequest>(r => r.Name == "Night operators" && r.Description == "After hours"),
            It.IsAny<CancellationToken>()), Times.Once);
        Heading(rig.Html).Should().Contain("Night operators");
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Success && t.Message == "Role details saved.");
    }

    [Fact]
    public async Task Rename_duplicate_name_shows_conflict_and_keeps_the_saved_name()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true);
        rig.Client.Setup(c => c.UpdateRoleMetadataAsync(RoleId, It.IsAny<UpdateRoleMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RoleLifecycleConflictException(RoleLifecycleConflictException.DuplicateName, "duplicate"));
        rig.Page.SetMetadataDraft("Taken name", null);

        await rig.Do(rig.Page.SaveMetadataAsync);

        rig.Html.Should().Contain("A role with this name already exists.");
        Heading(rig.Html).Should().Contain(RoleName);
        Heading(rig.Html).Should().NotContain("Taken name");
        rig.Client.Verify(c => c.UpdateRoleMetadataAsync(RoleId, It.IsAny<UpdateRoleMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task System_role_metadata_and_permissions_stay_read_only()
    {
        await using var rig = await RenderDetailAsync(system: true, canManage: true, canAssignPermissions: true);

        rig.Html.Should().Contain("This built-in role cannot be changed or deleted.");
        rig.Html.Should().Contain("id=\"role-system-notice\"");
        rig.Html.Should().NotContain("id=\"role-name\"");
        rig.Html.Should().NotContain("Save details");
        rig.Html.Should().NotContain("Edit permissions");
        rig.Html.Should().NotContain("isSystemRole");

        await rig.Do(rig.Page.SaveMetadataAsync);
        await rig.Do(rig.Page.StartEdit);

        rig.Html.Should().NotContain("Saving replaces the role's entire permission set");
        rig.Client.Verify(c => c.UpdateRoleMetadataAsync(It.IsAny<string>(), It.IsAny<UpdateRoleMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Client.Verify(c => c.GetGovernanceDescriptorsAsync(It.IsAny<CancellationToken>()), Times.Never);
        rig.Client.Verify(c => c.SetRolePermissionsAsync(It.IsAny<string>(), It.IsAny<SetRolePermissionsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Detail_shows_assignment_count_and_holders()
    {
        await using var rig = await RenderDetailAsync(
            system: false,
            canManage: false,
            assignmentCount: 1,
            holders: [new RoleAssignmentHolderDto("user-ada", "Ada Lovelace")]);

        rig.Html.Should().Contain("Assigned to 1 person.");
        rig.Html.Should().Contain("Ada Lovelace");
        rig.Html.Should().Contain("href=\"/config/governance/users/user-ada\"");
        rig.Html.Should().NotContain("id=\"role-delete-trigger\"");
    }

    [Fact]
    public async Task Unassigned_role_shows_an_empty_holder_state()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);

        rig.Html.Should().Contain("id=\"role-assignments\"");
        rig.Html.Should().Contain("id=\"role-assignments-empty\"");
        rig.Html.Should().Contain("No one holds this role.");
    }

    [Fact]
    public async Task Delete_unassigned_custom_role_confirms_and_returns_to_the_list()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);
        rig.Client.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        var dialog = Dialog(rig.Html);
        dialog.Should().Contain("role=\"dialog\"").And.Contain("aria-modal=\"true\"");
        dialog.Should().Contain("Delete role?");
        dialog.Should().Contain(RoleName);
        dialog.Should().Contain("permanently removes");
        dialog.Should().Contain("cannot be restored");
        dialog.Should().Contain(">Delete role<");

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Client.Verify(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()), Times.Once);
        rig.Nav.Navigations.Should().Contain(n => n.Uri.EndsWith("/config/governance/roles", StringComparison.Ordinal));
        rig.Nav.Navigations.Should().NotContain(n => n.Uri.Contains("/config/governance/roles/" + RoleId, StringComparison.Ordinal));
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Success && t.Message == "Role deleted.");
    }

    [Fact]
    public async Task Assigned_custom_role_does_not_call_delete()
    {
        await using var rig = await RenderDetailAsync(
            system: false,
            canManage: true,
            assignmentCount: 2,
            holders:
            [
                new RoleAssignmentHolderDto("user-ada", "Ada Lovelace"),
                new RoleAssignmentHolderDto("user-grace", "Grace Hopper")
            ]);

        rig.Html.Should().Contain("Assigned to 2 people.");
        rig.Html.Should().Contain("Deletion does not remove assignments.");
        rig.Html.Should().Contain("Ada Lovelace");
        rig.Html.Should().Contain("Grace Hopper");

        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        var dialog = Dialog(rig.Html);
        dialog.Should().Contain("Role cannot be deleted");
        dialog.Should().Contain("Deletion does not remove assignments.");
        Button(dialog, "data-testid=\"confirm-accept\"").Should().Contain("disabled");

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Client.Verify(c => c.DeleteRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Assignments_remain_race_refreshes_holders_instead_of_a_generic_failure()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);
        rig.Client.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RoleLifecycleConflictException(RoleLifecycleConflictException.AssignmentsRemain, "still assigned", 2));
        rig.Client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto(RoleId, RoleName, system: false, assignmentCount: 2));
        rig.Client.Setup(c => c.GetRoleAssignmentsAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleAssignmentsDto(2,
            [
                new RoleAssignmentHolderDto("user-ada", "Ada Lovelace"),
                new RoleAssignmentHolderDto("user-grace", "Grace Hopper")
            ]));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        var act = () => rig.Do(rig.Page.ConfirmDeleteAsync);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("assigned to 2 people");
        ex.Which.Message.Should().Contain("Deletion does not remove assignments.");
        rig.Html.Should().Contain("Ada Lovelace");
        rig.Html.Should().Contain("Grace Hopper");
        rig.Html.Should().Contain("Role cannot be deleted");
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
        rig.Client.Verify(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()), Times.Once);

        await rig.Do(rig.Page.ConfirmDeleteAsync);
        rig.Client.Verify(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task System_role_has_no_usable_delete_action()
    {
        await using var rig = await RenderDetailAsync(system: true, canManage: true, canAssignPermissions: true);

        rig.Html.Should().NotContain("id=\"role-delete-trigger\"");
        rig.Html.Should().NotContain("Delete role?");
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Html.Should().NotContain("role=\"dialog\"");
        rig.Client.Verify(c => c.DeleteRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_system_role_conflict_refreshes_to_the_read_only_notice()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);
        rig.Client.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RoleLifecycleConflictException(RoleLifecycleConflictException.SystemRole, "built-in"));
        rig.Client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto(RoleId, "Platform Administrator", system: true, assignmentCount: 1));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Html.Should().Contain("This built-in role cannot be changed or deleted.");
        rig.Html.Should().NotContain("id=\"role-delete-trigger\"");
        rig.Html.Should().NotContain("Delete role?");
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Error && t.Message == "This built-in role cannot be changed or deleted.");
    }

    [Fact]
    public async Task Delete_not_found_shows_the_role_is_gone()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);
        rig.Client.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Role {RoleId} not found."));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Html.Should().Contain("Role not found.");
        rig.Html.Should().NotContain($"Role {RoleId} not found.");
        rig.Html.Should().NotContain("Delete role?");
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Warning && t.Message == "This role no longer exists.");
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_forbidden_keeps_the_role()
    {
        await using var rig = await RenderDetailAsync(system: false, canManage: true, assignmentCount: 0);
        rig.Client.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Not authorized to manage roles.", null, HttpStatusCode.Forbidden));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        var act = () => rig.Do(rig.Page.ConfirmDeleteAsync);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.Message.Should().Be("You do not have permission to delete this role.");
        ex.Which.Message.Should().NotContain(RoleId);
        rig.Html.Should().Contain(RoleName);
        rig.Html.Should().Contain("Delete role?");
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
    }

    private static RoleDetailDto DetailDto(string id, string name, bool system, int assignmentCount, string? description = "Handles operations") =>
        new(id, name, description, system, [], assignmentCount);

    private static OperatorCapabilitiesDto Caps(bool canView, bool canManage, bool canAssignPermissions) =>
        new(false, false, false, false, false, CanViewGovernance: canView, CanAssignPermissions: canAssignPermissions, CanManageRoles: canManage);

    private static async Task<ListRig> RenderListAsync(bool canManage)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleDto> { new(RoleId, RoleName, "Handles operations", false) });
        var (renderer, nav, toasts, readHtml) = await StartRendererAsync(
            client,
            Caps(canView: true, canManage, canAssignPermissions: false),
            "https://localhost/config/governance/roles");
        RolesList? page = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host<RolesList>>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(Host<RolesList>.OnCapture)] = (Action<RolesList>)(p => page = p) })));
        return new ListRig(client, renderer, page!, nav, toasts, () => renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult());
    }

    private static async Task<CreateRig> RenderCreateAsync(bool canManage, bool canAssignPermissions)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetGovernanceDescriptorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceDescriptorResponse(
                [],
                [new PermissionCatalogEntryDto("user.read", "User", "instance", "Core", "platform", null)]));
        var (renderer, nav, toasts, _) = await StartRendererAsync(
            client,
            Caps(canView: true, canManage, canAssignPermissions),
            "https://localhost/config/governance/roles/new");
        RoleNew? page = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host<RoleNew>>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(Host<RoleNew>.OnCapture)] = (Action<RoleNew>)(p => page = p) })));
        return new CreateRig(client, renderer, page!, nav, toasts, () => renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult());
    }

    private static async Task<DetailRig> RenderDetailAsync(
        bool system,
        bool canManage,
        bool canAssignPermissions = false,
        int assignmentCount = 0,
        IReadOnlyList<RoleAssignmentHolderDto>? holders = null)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailDto(RoleId, system ? "Platform Administrator" : RoleName, system, assignmentCount));
        client.Setup(c => c.GetRoleAssignmentsAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleAssignmentsDto(assignmentCount, holders ?? []));
        var (renderer, nav, toasts, _) = await StartRendererAsync(
            client,
            Caps(canView: true, canManage, canAssignPermissions),
            $"https://localhost/config/governance/roles/{RoleId}");
        RoleDetail? page = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DetailHost>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(DetailHost.RoleId)] = RoleId,
                [nameof(DetailHost.OnCapture)] = (Action<RoleDetail>)(p => page = p)
            })));
        return new DetailRig(client, renderer, page!, nav, toasts, () => renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult());
    }

    private static Task<(HtmlRenderer Renderer, TestNavigationManager Nav, ToastService Toasts, Func<string> ReadHtml)> StartRendererAsync(
        Mock<IManagementClient> client,
        OperatorCapabilitiesDto caps,
        string uri)
    {
        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(caps);
        var nav = new TestNavigationManager("https://localhost/", uri);
        var toasts = new ToastService();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(capabilities.Object)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new FakeJsRuntime())
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(toasts)
            .BuildServiceProvider();
        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return Task.FromResult((renderer, nav, toasts, (Func<string>)(() => "")));
    }

    private static string Dialog(string html)
    {
        var marker = html.IndexOf("role=\"dialog\"", StringComparison.Ordinal);
        marker.Should().BeGreaterThanOrEqualTo(0);
        var start = html.LastIndexOf("<div", marker, StringComparison.Ordinal);
        var end = html.IndexOf("data-testid=\"confirm-accept\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var close = html.IndexOf("</button>", end, StringComparison.Ordinal);
        return html[start..(close + "</button>".Length)];
    }

    private static string Button(string html, string marker)
    {
        var markerAt = html.IndexOf(marker, StringComparison.Ordinal);
        markerAt.Should().BeGreaterThanOrEqualTo(0, $"expected markup containing {marker}");
        var start = html.LastIndexOf("<button", markerAt, StringComparison.Ordinal);
        var end = html.IndexOf("</button>", markerAt, StringComparison.Ordinal);
        return html[start..(end + "</button>".Length)];
    }

    private static string Heading(string html)
    {
        var start = html.IndexOf("<h1", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf("</h1>", start, StringComparison.Ordinal);
        return html[start..(end + "</h1>".Length)];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private sealed class Host<T> : ComponentBase where T : ComponentBase
    {
        [Parameter] public Action<T>? OnCapture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<T>(0);
            builder.AddComponentReferenceCapture(1, component => OnCapture?.Invoke((T)component));
            builder.CloseComponent();
        }
    }

    private sealed class DetailHost : ComponentBase
    {
        [Parameter] public string RoleId { get; set; } = "";
        [Parameter] public Action<RoleDetail>? OnCapture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<RoleDetail>(0);
            builder.AddAttribute(1, nameof(RoleDetail.RoleId), RoleId);
            builder.AddComponentReferenceCapture(2, component => OnCapture?.Invoke((RoleDetail)component));
            builder.CloseComponent();
        }
    }

    private abstract class Rig : IAsyncDisposable
    {
        private readonly HtmlRenderer _renderer;
        private readonly Func<string> _readHtml;
        public Mock<IManagementClient> Client { get; }
        public TestNavigationManager Nav { get; }
        public ToastService Toasts { get; }
        public string Html => _readHtml();
        public Task Do(Func<Task> action) => _renderer.Dispatcher.InvokeAsync(action);

        protected Rig(Mock<IManagementClient> client, HtmlRenderer renderer, TestNavigationManager nav, ToastService toasts, Func<string> readHtml)
        {
            Client = client;
            _renderer = renderer;
            Nav = nav;
            Toasts = toasts;
            _readHtml = readHtml;
        }

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            Toasts.Dispose();
        }
    }

    private sealed class ListRig : Rig
    {
        public RolesList Page { get; }
        public ListRig(Mock<IManagementClient> client, HtmlRenderer renderer, RolesList page, TestNavigationManager nav, ToastService toasts, Func<string> readHtml)
            : base(client, renderer, nav, toasts, readHtml) => Page = page;
    }

    private sealed class CreateRig : Rig
    {
        public RoleNew Page { get; }
        public CreateRig(Mock<IManagementClient> client, HtmlRenderer renderer, RoleNew page, TestNavigationManager nav, ToastService toasts, Func<string> readHtml)
            : base(client, renderer, nav, toasts, readHtml) => Page = page;
    }

    private sealed class DetailRig : Rig
    {
        public RoleDetail Page { get; }
        public DetailRig(Mock<IManagementClient> client, HtmlRenderer renderer, RoleDetail page, TestNavigationManager nav, ToastService toasts, Func<string> readHtml)
            : base(client, renderer, nav, toasts, readHtml) => Page = page;
    }
}
