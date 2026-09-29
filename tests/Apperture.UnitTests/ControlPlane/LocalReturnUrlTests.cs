using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Table-driven pins for the one shared return-target policy (SEC-1 design gate §14, refined by the SEC-1.0 browser
/// evidence in §38). Values are what the policy receives: the <c>returnUrl</c> query value after ONE percent-decode.
/// These are necessary, not sufficient: browser navigation evidence lives in Apperture.BrowserTests.
/// </summary>
public sealed class LocalReturnUrlTests
{
    // ---- accepted: the legitimate corpus (mirrors SEC-1.0 ReturnTargetCorpus.Legitimate) ------------------------

    [Theory]
    [InlineData("/control/center")]
    [InlineData("/")]
    [InlineData("/connections/0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("/connections/0f8fad5bd9cb469fa16570867728950e?foo=bar")]
    [InlineData("/connections/0f8fad5bd9cb469fa16570867728950e?foo=bar#notes")]
    [InlineData("/connections/abc#section")]
    [InlineData("/connections/abc?x=1&y=%2F")]
    [InlineData("/connections/abc?status=active&context=all")]
    [InlineData("/components/connector/Apperture.Platform.Sql")]
    [InlineData("/config/notifications/definitions/platform.schedule.expected_run_missing")]
    [InlineData("/modules/Sample.HelloWorld")]
    [InlineData("/runs?status=failed&q=a%20b%2Fc")]
    [InlineData("/connections?next=%2Fcontrol%2Fcenter")]
    [InlineData("/runs?q=%E2%9C%93")]
    [InlineData("/connections/caf%C3%A9")]
    [InlineData("/connections/a%2Fb")]
    [InlineData("/connections/a%2Eb")]
    [InlineData("/runs?status=failed")]
    [InlineData("/connections/")]
    [InlineData("/account/security")]
    [InlineData("/?context=all")]
    [InlineData("/#top")]
    public void Legitimate_local_targets_are_accepted_unchanged(string candidate)
    {
        LocalReturnUrl.Evaluate(candidate).Should().Be(LocalReturnUrlVerdict.Valid);
        LocalReturnUrl.Validate(candidate).Should().BeSameAs(candidate, "an accepted value is never rewritten");
        LocalReturnUrl.ResolveOrFallback(candidate).Should().Be(candidate);
    }

    [Fact]
    public void A_target_at_the_length_limit_is_accepted_and_one_over_is_refused()
    {
        var atLimit = "/x?q=" + new string('a', LocalReturnUrl.MaxLength - "/x?q=".Length);
        var overLimit = atLimit + "a";

        atLimit.Length.Should().Be(LocalReturnUrl.MaxLength);
        LocalReturnUrl.IsValid(atLimit).Should().BeTrue();
        LocalReturnUrl.Evaluate(overLimit).Should().Be(LocalReturnUrlVerdict.TooLong);
    }

    // ---- refused: the SEC-1.0 hostile corpus (mirrors ReturnTargetCorpus.Hostile) --------------------------------

    public static TheoryData<string, LocalReturnUrlVerdict> Hostile => new()
    {
        // absolute and scheme-relative
        { "https://evil.example/x", LocalReturnUrlVerdict.NotPathAbsolute },
        { "http://evil.example", LocalReturnUrlVerdict.NotPathAbsolute },
        { "//evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "//evil.example/deep/path", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "javascript:alert(1)", LocalReturnUrlVerdict.NotPathAbsolute },
        { "data:text/html,<b>x</b>", LocalReturnUrlVerdict.NotPathAbsolute },
        { "evil.example", LocalReturnUrlVerdict.NotPathAbsolute },

        // backslash forms: a browser reads '\' as '/', .NET Uri does not (F5)
        { "/\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\\\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\\/evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "\\/evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "\\\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\\@evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },

        // control characters and whitespace: browsers strip TAB/CR/LF and trim a leading space
        { " /\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { " /control/center", LocalReturnUrlVerdict.IllegalCharacter },
        { "/control/center ", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\t/evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\t\\evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\r\n/evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/\n/evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections\0", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections/a\u007Fb", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections/a b", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections/a b", LocalReturnUrlVerdict.IllegalCharacter },
        { "/connections/café", LocalReturnUrlVerdict.IllegalCharacter },
        { "/＼evil.example", LocalReturnUrlVerdict.IllegalCharacter },
        { "/∕evil.example", LocalReturnUrlVerdict.IllegalCharacter },

        // an encoded separator where a second decoder could turn it into an authority
        { "/%5Cevil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%2F%2Fevil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%2fevil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%255Cevil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%09/evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%0d%0a/evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/%2E%2E/%2E%2E/evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },

        // dot and empty segments: a browser rewrites them, so what was validated is not what it navigates to
        { "/.//evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/./control/center", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/connections/../../evil", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections/./abc", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections/%2E%2E//evil.example", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections/%2e/abc", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections/.%2E/abc", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections//evil.example", LocalReturnUrlVerdict.NonCanonicalPath },
        { "/connections//", LocalReturnUrlVerdict.NonCanonicalPath },

        // first-segment punctuation
        { "/@evil.example", LocalReturnUrlVerdict.AuthorityShapedStart },
        { "/:evil", LocalReturnUrlVerdict.AuthorityShapedStart },
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Hostile_targets_are_refused_and_never_rewritten(string candidate, LocalReturnUrlVerdict expected)
    {
        LocalReturnUrl.Evaluate(candidate).Should().Be(expected);
        LocalReturnUrl.Validate(candidate).Should().BeNull();
        LocalReturnUrl.ResolveOrFallback(candidate).Should().Be(LocalReturnUrl.Fallback, "a refused target is replaced, never repaired");
    }

    // ---- refused: auth surfaces and nested-returnUrl chains -----------------------------------------------------

    [Theory]
    [InlineData("/login")]
    [InlineData("/login/")]
    [InlineData("/LOGIN")]
    [InlineData("/login/local")]
    [InlineData("/login?returnUrl=%2Fconnections")]
    [InlineData("/login?returnUrl=%2F%5Cevil.example")]
    [InlineData("/login?returnUrl=%2F%2Fevil.example")]
    [InlineData("/login?returnUrl=%2Flogin")]
    [InlineData("/login#x")]
    [InlineData("/access-request")]
    [InlineData("/signin")]
    [InlineData("/signin/link")]
    [InlineData("/signin/step-up")]
    [InlineData("/signin?returnUrl=/dashboard")]
    [InlineData("/signin-oidc")]
    [InlineData("/signout")]
    [InlineData("/signout-callback-oidc")]
    [InlineData("/signout-oidc")]
    public void Authentication_surfaces_are_never_a_return_target(string candidate)
    {
        LocalReturnUrl.Evaluate(candidate).Should().Be(LocalReturnUrlVerdict.ReservedAuthPath);
        LocalReturnUrl.ResolveOrFallback(candidate).Should().Be(LocalReturnUrl.Fallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_targets_fall_back(string? candidate)
    {
        LocalReturnUrl.Evaluate(candidate).Should().Be(LocalReturnUrlVerdict.Empty);
        LocalReturnUrl.ResolveOrFallback(candidate).Should().Be(LocalReturnUrl.Fallback);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("?x=1")]
    [InlineData("#frag")]
    [InlineData("connections/abc")]
    public void A_target_that_is_not_path_absolute_is_refused(string candidate) =>
        LocalReturnUrl.Evaluate(candidate).Should().Be(LocalReturnUrlVerdict.NotPathAbsolute);

    // ---- one decode, no second decode -----------------------------------------------------------------------

    [Theory]
    [InlineData("/connections/a%2Fb")]
    [InlineData("/connections/a%5Cb")]
    [InlineData("/runs?q=%2F%2Fevil.example")]
    [InlineData("/runs?next=%5Cevil.example")]
    [InlineData("/connections/a%252Fb")]
    public void Encoded_separators_after_the_first_segment_are_data_and_are_kept(string candidate) =>
        LocalReturnUrl.Validate(candidate).Should().Be(candidate);

    [Fact]
    public void The_policy_does_not_decode_an_encoded_hostile_value_into_a_different_valid_one()
    {
        // Encoded once too many by a caller: the '%' is literal data and the leading segment is not a plain name.
        LocalReturnUrl.Validate("%2F%5Cevil.example").Should().BeNull();
        LocalReturnUrl.Validate("/%2F%5Cevil.example").Should().BeNull();
    }

    // ---- structural check for trusted host-built paths ----------------------------------------------------------

    [Theory]
    [InlineData("/login", true)]
    [InlineData("/login?error=oidc", true)]
    [InlineData("/account?linked=1", true)]
    [InlineData("/access-request", true)]
    [InlineData("/control/center", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("account", false)]
    [InlineData("", false)]
    public void IsLocalPath_is_the_structure_check_without_the_reserved_rule(string? candidate, bool expected) =>
        LocalReturnUrl.IsLocalPath(candidate).Should().Be(expected);

    [Theory]
    [InlineData("/signin", true)]
    [InlineData("/signin/link?x=1", true)]
    [InlineData("/login/local", true)]
    [InlineData("/Signout/", true)]
    [InlineData("/connections", false)]
    [InlineData("/signing-off", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void IsReservedAuthPath_matches_by_first_segment_and_ignores_query_and_fragment(string path, bool expected) =>
        LocalReturnUrl.IsReservedAuthPath(path).Should().Be(expected);

    // ---- capture from an absolute URI ---------------------------------------------------------------------------

    [Theory]
    [InlineData("http://localhost:9001/connections/abc?x=1#notes", "/connections/abc?x=1#notes")]
    [InlineData("http://localhost:9001/connections/abc#notes", "/connections/abc#notes")]
    [InlineData("http://localhost:9001/", "/")]
    [InlineData("http://localhost:9001", "/")]
    [InlineData("http://localhost:9001?x=1", "/?x=1")]
    [InlineData("http://localhost:9001#top", "/#top")]
    [InlineData("https://ui.example.com/runs?q=a%20b%2Fc", "/runs?q=a%20b%2Fc")]
    [InlineData("http://localhost:9001/connections/a%2Fb", "/connections/a%2Fb")]
    public void FromAbsoluteUri_returns_path_query_and_fragment_exactly_as_serialized(string absolute, string expected) =>
        LocalReturnUrl.FromAbsoluteUri(absolute).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/connections")]
    [InlineData("localhost:9001/connections")]
    public void FromAbsoluteUri_is_null_without_a_scheme_and_authority(string? absolute) =>
        LocalReturnUrl.FromAbsoluteUri(absolute).Should().BeNull();
}
