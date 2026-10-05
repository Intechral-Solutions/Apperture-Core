using AngleSharp.Dom;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Molecules;
using Bunit;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// UserRef presents one human platform user, or an unresolved historical actor string, consistently. These tests
/// assert the rendered DOM structure (visible text, the text content that supplies the accessible name, link,
/// focusability), not just CSS classes. They do not run a browser, so they do not prove browser-computed accessible
/// names, screen-reader output or theme appearance; those are checked at adoption (IAM-9.5).
/// </summary>
public sealed class UserRefRenderTests : BunitContext
{
    private static readonly UserReferenceDto Alice = new("user-7f3a", "alice", "Alice Example");
    private const string AliceName = "Alice Example (alice)";
    private const string AliceHref = "/config/governance/users/user-7f3a";

    private IRenderedComponent<UserRef> RenderResolved(UserRefMode? mode, bool canNavigate, UserReferenceDto? user = null) =>
        Render<UserRef>(p =>
        {
            p.Add(c => c.User, user ?? Alice);
            p.Add(c => c.CanNavigate, canNavigate);
            if (mode is not null)
                p.Add(c => c.Mode, mode.Value);
        });

    // ---------- Compact ----------

    [Fact]
    public void Compact_with_user_read_links_by_user_id_and_shows_only_the_username()
    {
        var cut = RenderResolved(UserRefMode.Compact, canNavigate: true);

        var link = cut.Find("a");
        link.GetAttribute("href").Should().Be(AliceHref, "the route uses UserId, never Username");
        link.ClassList.Should().Contain("ui-drill-in-link", "navigation reuses DrillInLink");
        UserRefDom.VisibleText(cut.Find(".ui-user-ref")).Should().Be("alice");
        UserRefDom.AccessibleName(link).Should().Be(AliceName);
    }

    [Fact]
    public void Compact_native_title_is_a_convenience_that_repeats_only_the_display_name()
    {
        var cut = RenderResolved(UserRefMode.Compact, canNavigate: true);

        var link = cut.Find("a");
        link.GetAttribute("title").Should().Be("Alice Example");
        link.HasAttribute("aria-label").Should().BeFalse("the name comes from content, title is not load-bearing");
        // The accessible name is complete without the title.
        UserRefDom.AccessibleName(link).Should().Be(AliceName);
        UserRefDom.VisibleText(link).Should().Be("alice");
    }

    [Fact]
    public void Compact_without_user_read_renders_the_same_identity_as_plain_text()
    {
        var cut = RenderResolved(UserRefMode.Compact, canNavigate: false);

        cut.FindAll("a").Should().BeEmpty("lack of user.read hides navigation, not identity");
        UserRefDom.VisibleText(cut.Find(".ui-user-ref")).Should().Be("alice");
        UserRefDom.AccessibleName(cut.Find(".ui-user-ref")).Should().Be(AliceName);
        UserRefDom.FocusableElements(cut).Should().BeEmpty("plain text must not become a tab stop");
        cut.FindAll("[disabled],[aria-disabled]").Should().BeEmpty("no disabled fake link");
        cut.Markup.Should().NotContain("href");
    }

    [Fact]
    public void Compact_is_the_default_mode()
    {
        var cut = RenderResolved(mode: null, canNavigate: false);

        cut.Find(".ui-user-ref").ClassList.Should().Contain("ui-user-ref--compact");
        UserRefDom.VisibleText(cut.Find(".ui-user-ref")).Should().Be("alice");
    }

    [Fact]
    public void Navigation_is_off_unless_the_caller_grants_it()
    {
        var cut = Render<UserRef>(p => p.Add(c => c.User, Alice));

        cut.FindAll("a").Should().BeEmpty();
    }

    // ---------- Rich ----------

    [Fact]
    public void Rich_with_user_read_links_by_user_id_with_display_name_primary_and_username_secondary()
    {
        var cut = RenderResolved(UserRefMode.Rich, canNavigate: true);

        var link = cut.Find("a");
        link.GetAttribute("href").Should().Be(AliceHref);
        link.GetAttribute("href").Should().NotContain("alice");
        UserRefDom.AccessibleName(link).Should().Be(AliceName);

        cut.Find(".ui-user-ref__display-name").TextContent.Should().Be("Alice Example");
        var username = cut.Find(".ui-user-ref__username");
        username.TextContent.Should().Be("(alice)");
        username.ClassList.Should().Contain("text-muted", "the username is visually secondary");
    }

    [Fact]
    public void Rich_without_user_read_shows_the_same_visible_identity_without_a_link()
    {
        var cut = RenderResolved(UserRefMode.Rich, canNavigate: false);

        cut.FindAll("a").Should().BeEmpty();
        UserRefDom.VisibleText(cut.Find(".ui-user-ref")).Should().Be(AliceName);
        UserRefDom.AccessibleName(cut.Find(".ui-user-ref")).Should().Be(AliceName);
        UserRefDom.FocusableElements(cut).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rich_username_is_ordinary_visible_content_not_title_aria_or_visually_hidden(bool canNavigate)
    {
        var cut = RenderResolved(UserRefMode.Rich, canNavigate);

        var username = cut.Find(".ui-user-ref__username");
        username.TextContent.Should().Contain("alice");
        username.ClosestAncestorOrSelf(n => n.ClassList.Contains("sr-only")).Should().BeNull();
        username.ClosestAncestorOrSelf(n => n.GetAttribute("aria-hidden") == "true").Should().BeNull();
        UserRefDom.VisibleText(cut.Find(".ui-user-ref")).Should().Contain("alice");
        cut.FindAll("[title]").Should().BeEmpty("rich mode needs no hover affordance at all");
        cut.FindAll("[aria-label]").Should().BeEmpty("the DOM text already yields the accessible name");
    }

    // ---------- Unresolved ----------

    [Fact]
    public void Unresolved_shows_the_label_and_the_original_string_in_monospace_without_a_link()
    {
        const string original = "  Old.Provider\\Value@legacy  ";
        var cut = Render<UserRef>(p => p.Add(c => c.UnresolvedActor, original));

        var root = cut.Find(".ui-user-ref");
        root.ClassList.Should().Contain("ui-user-ref--unresolved");
        cut.Find(".ui-user-ref__unresolved-label").TextContent.Should().Be("Unrecognized actor");

        var actor = cut.Find(".ui-user-ref__actor");
        actor.TextContent.Should().Be(original, "the original value is preserved exactly");
        actor.ClassList.Should().Contain("font-mono");

        UserRefDom.AccessibleName(root).Should().Be("Unrecognized actor " + original);
        cut.FindAll("a").Should().BeEmpty();
        UserRefDom.FocusableElements(cut).Should().BeEmpty("an unresolved actor is not an interactive user");
        cut.Markup.Should().NotContain("href");
    }

    [Fact]
    public void Unresolved_ignores_navigation_even_when_the_viewer_may_read_users()
    {
        var cut = Render<UserRef>(p =>
        {
            p.Add(c => c.UnresolvedActor, "old-provider-value");
            p.Add(c => c.CanNavigate, true);
        });

        cut.FindAll("a").Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserRefMode.Compact)]
    [InlineData(UserRefMode.Rich)]
    public void Unresolved_looks_the_same_in_both_modes(UserRefMode mode)
    {
        var cut = Render<UserRef>(p =>
        {
            p.Add(c => c.UnresolvedActor, "old-provider-value");
            p.Add(c => c.Mode, mode);
        });

        cut.Find(".ui-user-ref__unresolved-label").TextContent.Should().Be("Unrecognized actor");
        cut.Find(".ui-user-ref__actor").TextContent.Should().Be("old-provider-value");
    }

    // ---------- Contract invariants ----------

    [Fact]
    public void Resolved_and_unresolved_cannot_be_supplied_together()
    {
        var act = () => Render<UserRef>(p =>
        {
            p.Add(c => c.User, Alice);
            p.Add(c => c.UnresolvedActor, "old-provider-value");
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void Neither_a_user_nor_an_unresolved_actor_is_rejected()
    {
        var act = () => Render<UserRef>(p => p.Add(c => c.CanNavigate, true));

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one*");
    }

    [Theory]
    [InlineData("", "alice", "Alice Example")]
    [InlineData("user-7f3a", " ", "Alice Example")]
    [InlineData("user-7f3a", "alice", "")]
    public void A_partially_missing_user_reference_is_rejected(string userId, string username, string displayName)
    {
        var act = () => RenderResolved(UserRefMode.Compact, canNavigate: true, new UserReferenceDto(userId, username, displayName));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_unsupported_mode_is_rejected()
    {
        var act = () => RenderResolved((UserRefMode)99, canNavigate: false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Only_compact_and_rich_modes_exist()
    {
        Enum.GetNames<UserRefMode>().Should().BeEquivalentTo("Compact", "Rich");
    }

    // ---------- Status seam ----------

    [Theory]
    [InlineData(UserRefMode.Compact, true)]
    [InlineData(UserRefMode.Compact, false)]
    [InlineData(UserRefMode.Rich, true)]
    [InlineData(UserRefMode.Rich, false)]
    public void No_lifecycle_status_is_rendered_yet(UserRefMode mode, bool canNavigate)
    {
        var cut = RenderResolved(mode, canNavigate);

        var text = cut.Markup.ToLowerInvariant();
        text.Should().NotContain("status");
        text.Should().NotContain("active");
        text.Should().NotContain("disabled");
        text.Should().NotContain("decommissioned");
        cut.FindAll("[class*='pill'],[class*='badge'],[class*='status']").Should().BeEmpty();
    }

    [Fact]
    public void Identity_content_is_not_encoded_twice_or_trusted_as_markup()
    {
        var user = new UserReferenceDto("user-1", "mallory", "<b>Mallory</b> & Co");
        var cut = RenderResolved(UserRefMode.Rich, canNavigate: true, user);

        cut.FindAll("b").Should().BeEmpty();
        cut.Find(".ui-user-ref__display-name").TextContent.Should().Be("<b>Mallory</b> & Co");
    }

    [Fact]
    public void The_user_id_is_escaped_in_the_route()
    {
        var user = new UserReferenceDto("user/with space", "weird", "Weird Id");
        var cut = RenderResolved(UserRefMode.Compact, canNavigate: true, user);

        cut.Find("a").GetAttribute("href").Should().Be("/config/governance/users/user%2Fwith%20space");
    }
}

/// <summary>
/// Text-structure approximation used by these tests: the text a name would be built from, taken from aria-label
/// if present, otherwise from the subtree text with <c>aria-hidden</c> branches removed (screen-reader-only text
/// still counts). It is not the browser's accessible-name algorithm and says nothing about screen-reader behavior.
/// </summary>
internal static class UserRefDom
{
    public static string AccessibleName(IElement element)
    {
        var label = element.GetAttribute("aria-label");
        return label is not null ? label : Collect(element, includeScreenReaderOnly: true);
    }

    /// <summary>Text a sighted user sees: aria-hidden and screen-reader-only branches are excluded.</summary>
    public static string VisibleText(IElement element) => Collect(element, includeScreenReaderOnly: false);

    public static IReadOnlyList<IElement> FocusableElements(IRenderedComponent<UserRef> cut) =>
        cut.FindAll("a[href],button,input,select,textarea,[tabindex]").ToList();

    private static string Collect(INode node, bool includeScreenReaderOnly)
    {
        if (node is IElement el)
        {
            if (el.GetAttribute("aria-hidden") == "true")
                return "";
            if (!includeScreenReaderOnly && el.ClassList.Contains("sr-only"))
                return "";
        }

        if (node.NodeType == NodeType.Text)
            return node.TextContent;

        return string.Concat(node.ChildNodes.Select(c => Collect(c, includeScreenReaderOnly)));
    }

    public static IElement? ClosestAncestorOrSelf(this IElement element, Func<IElement, bool> predicate)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (predicate(current))
                return current;
        }

        return null;
    }
}
