using AngleSharp.Dom;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Real governance pages rendered by bUnit against a loose <see cref="IManagementClient"/> and a fixed capability set.
/// Register capabilities with <see cref="Grant"/> before the first render.
/// </summary>
public abstract class GovernancePageTestBase : BunitContext
{
    protected const string AliceId = "00112233445566778899aabb";
    protected const string BobId = "ffeeddccbbaa998877665544";

    protected GovernancePageTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Client.Object);
        Services.AddSingleton<IToastService>(Toasts);
        Services.AddSingleton(new ShellOrientationService());
        Services.AddOptions<ShellUiOptions>();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<ShellHostNavigation>();
        Services.AddSingleton<SessionStateService>();
        Services.AddSingleton<AccountAvatarService>();
        Services.AddSingleton<AuthSignInOptionsService>();
        Services.AddSingleton<TopBarMenuCoordinator>();
    }

    protected Mock<IManagementClient> Client { get; } = new(MockBehavior.Loose);

    protected ToastService Toasts { get; } = new();

    protected static OperatorCapabilitiesDto Caps(
        bool view = true,
        bool manageUsers = false,
        bool manageCredentials = false,
        bool assignRoles = false,
        bool manageRoles = false) =>
        new(false, false, false, false, false,
            CanViewGovernance: view,
            CanAssignRoles: assignRoles,
            CanManageUsers: manageUsers,
            CanManageCredentials: manageCredentials,
            CanManageRoles: manageRoles);

    protected void Grant(OperatorCapabilitiesDto caps)
    {
        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(caps);
        Services.AddSingleton(capabilities.Object);
    }

    protected static UserDetailDto Alice(
        string username = "alice",
        bool emergency = false,
        string displayName = "Alice Example") =>
        new(AliceId, username, displayName,
            [new IdentityLinkDto("Local", username, null)],
            [],
            HasLocalCredential: true,
            IsCredentialDisabled: false,
            EmergencyAccessAccount: emergency);

    /// <summary>A second ordinary user, for pages that move from one user to another.</summary>
    protected static UserDetailDto Bob(string username = "bob") =>
        new(BobId, username, "Bob Builder",
            [new IdentityLinkDto("Local", username, null)],
            [],
            HasLocalCredential: true,
            IsCredentialDisabled: false,
            EmergencyAccessAccount: false);

    /// <summary>What a sighted user reads: attributes are not text and screen-reader-only branches are skipped (aria-hidden text is visible).</summary>
    protected static string VisibleText(IElement element) => Collect(element, includeScreenReaderOnly: false);

    /// <summary>Visible text with the collapsed technical-details disclosures removed, where identifiers are allowed to live.</summary>
    protected static string VisibleTextOutsideTechnicalDetails(IElement element) =>
        Collect(element, includeScreenReaderOnly: false, skip: e => e.ClassList.Contains("ui-technical-details"));

    protected static string AccessibleText(IElement element) => Collect(element, includeScreenReaderOnly: true);

    private static string Collect(INode node, bool includeScreenReaderOnly, Func<IElement, bool>? skip = null)
    {
        if (node is IElement el)
        {
            if (skip?.Invoke(el) == true)
                return "";
            // Assistive technology skips aria-hidden branches; a sighted reader skips screen-reader-only branches.
            if (includeScreenReaderOnly ? el.GetAttribute("aria-hidden") == "true" : el.ClassList.Contains("sr-only"))
                return "";
        }

        return node.NodeType == NodeType.Text
            ? node.TextContent
            : string.Concat(node.ChildNodes.Select(c => Collect(c, includeScreenReaderOnly, skip)));
    }
}
