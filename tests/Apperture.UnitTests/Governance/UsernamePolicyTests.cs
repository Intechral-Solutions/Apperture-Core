using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UsernamePolicyTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("admin")]
    [InlineData("jane.doe")]
    [InlineData("jane-doe")]
    [InlineData("jane_doe")]
    [InlineData("a1")] // shorter than 3: listed for the boundary, asserted below as invalid
    [InlineData("a.b-c_d9")]
    [InlineData("user-2")]
    [InlineData("abcdef0123456789abcdef0")] // 23 hex characters: not the user-id shape
    [InlineData("abcdefghabcdefghabcdefgh")] // 24 characters but not all hex
    [InlineData("new")] // route words are not reserved
    [InlineData("requests")]
    [InlineData("from-entra")]
    public void Valid_or_boundary_values_follow_the_format_contract(string value)
    {
        var expected = value.Length >= UsernamePolicy.MinLength;
        UsernamePolicy.IsValidFormat(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("ab")] // too short
    [InlineData("1abc")] // must start with a letter
    [InlineData(".abc")]
    [InlineData("_abc")]
    [InlineData("abc.")] // must end with a letter or digit
    [InlineData("abc-")]
    [InlineData("a..b")] // no two separators in a row
    [InlineData("a.-b")]
    [InlineData("a_-b")]
    [InlineData("ab c")] // no whitespace
    [InlineData("Abc")] // not canonical (upper case)
    [InlineData("jane@contoso.com")] // '@' excluded
    [InlineData("josé")] // non-ASCII rejected, not transliterated
    [InlineData("abcdef0123456789abcdef01")] // 24 lowercase hex: user-id shape
    [InlineData("")]
    public void Invalid_values_are_refused(string value)
    {
        UsernamePolicy.IsValidFormat(value).Should().BeFalse();
    }

    [Fact]
    public void Length_boundaries_are_3_to_64()
    {
        UsernamePolicy.IsValidFormat("a" + new string('b', 63)).Should().BeTrue("64 characters is the maximum");
        UsernamePolicy.IsValidFormat("a" + new string('b', 64)).Should().BeFalse("65 characters is too long");
        UsernamePolicy.IsValidFormat("abc").Should().BeTrue();
        UsernamePolicy.IsValidFormat("ab").Should().BeFalse();
    }

    [Theory]
    [InlineData("  Alice  ", "alice")]
    [InlineData("JANE.DOE", "jane.doe")]
    [InlineData("\tbob\n", "bob")]
    [InlineData("Jane@Contoso.com", "jane@contoso.com")]
    public void Normalization_is_trim_then_lower_invariant_and_is_the_one_shared_rule(string input, string expected)
    {
        UsernamePolicy.Normalize(input).Should().Be(expected);
        LocalAuthHelpers.NormalizeUsername(input).Should().Be(expected, "sign-in and assignment share one normalization");
    }

    [Fact]
    public void Reserved_names_are_exactly_the_identity_and_actor_names()
    {
        UsernamePolicy.ReservedNames.Should().BeEquivalentTo(
            "breakglass", "system", "anonymous", "recovery", "emergency-cli", "worker");
        UsernamePolicy.CheckNewAssignment("admin").Should().Be(UsernameAssignmentCheck.Valid, "admin is not reserved");
        UsernamePolicy.CheckNewAssignment("me").Should().Be(UsernameAssignmentCheck.Invalid, "too short, not reserved");
    }

    [Theory]
    [InlineData("breakglass")]
    [InlineData("System")]
    [InlineData(" ANONYMOUS ")]
    [InlineData("recovery")]
    [InlineData("emergency-cli")]
    [InlineData("worker")]
    public void Reserved_names_cannot_be_newly_assigned(string requested)
    {
        var act = () => UsernamePolicy.RequireNewAssignment(requested);

        act.Should().Throw<UsernameException>().Which.Code.Should().Be(UsernameErrorCodes.Reserved);
    }

    [Theory]
    [InlineData("jane@contoso.com")]
    [InlineData("a")]
    [InlineData("josé")]
    [InlineData("abcdef0123456789abcdef01")]
    public void Invalid_names_cannot_be_newly_assigned_including_grandfathered_shapes(string requested)
    {
        var act = () => UsernamePolicy.RequireNewAssignment(requested);

        act.Should().Throw<UsernameException>().Which.Code.Should().Be(UsernameErrorCodes.Invalid);
    }

    [Fact]
    public void RequireNewAssignment_returns_the_canonical_form()
    {
        UsernamePolicy.RequireNewAssignment("  Jane.Doe ").Should().Be("jane.doe");
    }

    [Fact]
    public void Grandfathered_stored_values_are_canonical_even_though_they_fail_the_format()
    {
        UsernamePolicy.IsCanonical("jane@contoso.com").Should().BeTrue();
        UsernamePolicy.IsValidFormat("jane@contoso.com").Should().BeFalse();
        UsernamePolicy.IsCanonical("Jane@contoso.com").Should().BeFalse();
    }

    [Theory]
    [InlineData("Jane.Doe@contoso.com", null, null, "jane.doe")]
    [InlineData("jane_contoso.com#EXT#@tenant.onmicrosoft.com", "jane@contoso.com", "Jane Guest", "jane")]
    [InlineData("jane_contoso.com#ext#@tenant.onmicrosoft.com", null, "Jane Guest", "jane.guest")]
    [InlineData("jane_contoso.com#EXT#@tenant.onmicrosoft.com", null, null, "user")]
    [InlineData(null, "J.Smith@contoso.com", "John Smith", "j.smith")]
    [InlineData(null, null, "José Álvarez", "jose.alvarez")]
    [InlineData(null, null, "  Mary   Ann  O'Brien ", "mary.ann.obrien")]
    [InlineData(null, null, "Zoë Ångström-Ünal", "zoe.angstrom-unal")]
    [InlineData(null, null, "王小明", "user")]
    [InlineData(null, null, null, "user")]
    [InlineData("", "  ", "", "user")]
    [InlineData("ab@contoso.com", null, "Bo", "user")] // every source too short
    [InlineData("worker@contoso.com", null, "Pat Lee", "pat.lee")] // reserved base is skipped
    [InlineData("abcdef0123456789abcdef01@contoso.com", null, "Kim", "kim")] // user-id shaped base is skipped
    [InlineData("007bond@contoso.com", null, null, "bond")] // leading digits trimmed
    [InlineData("a..--b@contoso.com", null, null, "a.b")] // separator runs collapse to the first
    [InlineData("-.-joe-@contoso.com", null, null, "joe")] // leading/trailing separators trimmed
    public void Generator_fixed_vectors(string? upn, string? mail, string? displayName, string expected)
    {
        UsernamePolicy.GenerateBase(new UsernameGenerationSources(upn, mail, displayName)).Should().Be(expected);
    }

    [Fact]
    public void Generator_truncates_long_names_to_56_without_a_trailing_separator()
    {
        var display = string.Join(' ', Enumerable.Repeat("abcdefg", 12)); // 95 characters
        var generated = UsernamePolicy.GenerateBase(new UsernameGenerationSources(null, null, display));

        generated.Length.Should().BeLessThanOrEqualTo(UsernamePolicy.GeneratedBaseMaxLength);
        generated.Should().NotEndWith(".");
        UsernamePolicy.IsValidFormat(generated).Should().BeTrue();
    }

    [Fact]
    public void Generator_never_uses_a_value_it_was_not_given()
    {
        // Only UPN, mail and display name are inputs; an object id cannot reach the generator.
        typeof(UsernameGenerationSources)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().BeEquivalentTo("UserPrincipalName", "Mail", "DisplayName");
    }

    [Fact]
    public void Collision_candidates_are_base_then_2_to_20_then_five_random_suffixes()
    {
        var suffixes = new Queue<string>(["a1b2c3", "d4e5f6", "g7h8i9", "j0k1l2", "m3n4o5"]);
        var candidates = UsernamePolicy.CollisionCandidates("alice", () => suffixes.Dequeue()).ToList();

        candidates.Should().HaveCount(1 + 19 + 5);
        candidates[0].Should().Be("alice");
        candidates[1].Should().Be("alice-2");
        candidates[19].Should().Be("alice-20");
        candidates.Skip(20).Should().Equal("alice-a1b2c3", "alice-d4e5f6", "alice-g7h8i9", "alice-j0k1l2", "alice-m3n4o5");
        candidates.Should().OnlyContain(c => UsernamePolicy.IsValidFormat(c));
    }

    [Fact]
    public void Default_random_suffix_is_six_lowercase_alphanumerics_and_the_longest_candidate_fits()
    {
        var longBase = "a" + new string('b', UsernamePolicy.GeneratedBaseMaxLength - 1);
        var random = UsernamePolicy.CollisionCandidates(longBase).Last();

        random.Length.Should().Be(UsernamePolicy.GeneratedBaseMaxLength + 7).And.BeLessThanOrEqualTo(UsernamePolicy.MaxLength);
        random[(longBase.Length + 1)..].Should().MatchRegex("^[a-z0-9]{6}$");
        UsernamePolicy.IsValidFormat(random).Should().BeTrue();
    }

    [Fact]
    public void Recovery_access_helper_delegates_to_the_policy()
    {
        EmergencyAccessIdentity.LocalUsername.Should().Be(UsernamePolicy.RecoveryAccessUsername);
        EmergencyAccessIdentity.IsReservedLocalUsername("  BreakGlass ").Should().BeTrue();
        EmergencyAccessIdentity.IsReservedLocalUsername("system").Should().BeFalse("only the Recovery Access name keeps its dedicated error code");
    }
}
