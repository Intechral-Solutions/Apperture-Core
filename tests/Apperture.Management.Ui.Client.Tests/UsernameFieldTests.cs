using Apperture.Core.Domain.Identity;
using Apperture.Management.Ui.Client.Molecules;
using Bunit;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>The shared username field: live format feedback in visible text, with the server's own wording.</summary>
public sealed class UsernameFieldTests : BunitContext
{
    private IRenderedComponent<UsernameField> RenderField(string? value, string? serverError = null, bool disabled = false) =>
        Render<UsernameField>(p =>
        {
            p.Add(c => c.Id, "u-name");
            p.Add(c => c.Label, "Username");
            p.Add(c => c.Value, value);
            p.Add(c => c.Disabled, disabled);
            if (serverError is not null)
                p.Add(c => c.ServerError, serverError);
        });

    [Fact]
    public void A_blank_field_shows_the_format_guidance_as_ordinary_visible_text_and_is_not_marked_invalid()
    {
        var cut = RenderField("");

        var input = cut.Find("input#u-name");
        input.HasAttribute("aria-invalid").Should().BeFalse();
        input.GetAttribute("autocomplete").Should().Be("username");
        input.GetAttribute("autocapitalize").Should().Be("none");
        cut.Find("label[for='u-name']").TextContent.Should().Be("Username");
        cut.Find("#u-name-hint").TextContent.Should().Contain("3 to 64 characters");
    }

    [Fact]
    public void A_valid_name_is_not_flagged_and_keeps_the_guidance()
    {
        var cut = RenderField("alice.example");

        cut.Find("input#u-name").HasAttribute("aria-invalid").Should().BeFalse();
        cut.Find("#u-name-hint").TextContent.Should().Contain("3 to 64 characters");
    }

    [Theory]
    [InlineData("alice.example")]
    [InlineData("Bad Name!")]
    [InlineData("")]
    public void The_field_adds_no_extra_tab_stop_in_any_state(string value)
    {
        // The shell's validity icon is a focusable button that renders at 0x0 in the shipped styles; this field must not add one.
        var cut = RenderField(value);

        cut.FindAll("button, a[href], [tabindex]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Bad Name!", "3 to 64 characters")]
    [InlineData("jane@contoso.com", "3 to 64 characters")]
    [InlineData("ab", "3 to 64 characters")]
    [InlineData("system", "reserved")]
    public void An_invalid_name_is_flagged_and_explained_in_visible_text_the_input_points_at(string value, string expected)
    {
        var cut = RenderField(value);

        var input = cut.Find("input#u-name");
        input.GetAttribute("aria-invalid").Should().Be("true");
        input.GetAttribute("aria-describedby").Should().Be("u-name-hint");
        var hint = cut.Find("#u-name-hint");
        hint.TextContent.Should().Contain(expected);
        hint.GetAttribute("aria-live").Should().Be("polite", "feedback is announced without moving focus");
    }

    [Fact]
    public void A_server_refusal_replaces_the_hint_and_marks_the_field_invalid()
    {
        var cut = RenderField("alice.taken", serverError: "That username belongs, or once belonged, to another user.");

        cut.Find("input#u-name").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("#u-name-hint").TextContent.Should().Contain("once belonged, to another user");
    }

    [Fact]
    public void Typing_reports_the_raw_value_and_clears_the_server_refusal_through_the_owner()
    {
        string? received = null;
        var cut = Render<UsernameField>(p =>
        {
            p.Add(c => c.Id, "u-name");
            p.Add(c => c.Label, "Username");
            p.Add(c => c.Value, "");
            p.Add(c => c.ValueChanged, (string? v) => received = v);
        });

        cut.Find("input#u-name").Input("Alice.Example");

        received.Should().Be("Alice.Example", "normalization is the server's concern and the typed text is preserved");
    }

    [Fact]
    public void A_disabled_field_cannot_be_edited()
    {
        RenderField("alice", disabled: true).Find("input#u-name").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void The_validation_state_comes_from_the_shared_domain_rule()
    {
        // The same inputs the domain rule rejects are the ones flagged here, so client and server cannot drift.
        foreach (var candidate in new[] { "ok.name", "ab", "system", "has space" })
        {
            var expectedInvalid = UsernameRules.ToFieldValidation(candidate).Kind == FieldValidationKind.Invalid;
            var cut = RenderField(candidate);
            cut.Find("input#u-name").HasAttribute("aria-invalid").Should().Be(expectedInvalid, candidate);
        }
    }
}
