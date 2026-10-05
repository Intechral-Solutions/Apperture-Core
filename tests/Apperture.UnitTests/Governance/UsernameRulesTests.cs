using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The username rules live in the domain so the browser client validates with exactly the rule the server enforces.
/// <see cref="UsernamePolicy"/> delegates to them; these tests pin that there is one rule, not two.
/// </summary>
public sealed class UsernameRulesTests
{
    public static TheoryData<string> Candidates => new()
    {
        "alice", "alice.example", "a-b_c", "ab", "a", "", "Alice", "alice-", "-alice", "al--ice", "al..ice", "1alice",
        "alice@contoso.com", "alice bob", "ålice", "system", "breakglass", "worker", "001122334455667788990011",
        "00112233445566778899aabb", new string('a', 64), new string('a', 65),
    };

    [Theory]
    [MemberData(nameof(Candidates))]
    public void The_server_policy_and_the_client_rules_agree_on_format(string candidate) =>
        UsernameRules.IsValidFormat(candidate).Should().Be(UsernamePolicy.IsValidFormat(candidate));

    [Theory]
    [MemberData(nameof(Candidates))]
    public void The_server_policy_and_the_client_rules_agree_on_reserved_names(string candidate) =>
        UsernameRules.IsReserved(candidate).Should().Be(UsernamePolicy.IsReserved(candidate));

    [Fact]
    public void There_is_one_reserved_list_and_it_is_the_one_the_policy_exposes()
    {
        UsernamePolicy.ReservedNames.Should().BeSameAs(UsernameRules.ReservedNames);
        UsernameRules.ReservedNames.Should().BeEquivalentTo("breakglass", "system", "anonymous", "recovery", "emergency-cli", "worker");
    }

    [Fact]
    public void The_guidance_shown_beside_the_field_is_the_message_the_server_returns()
    {
        var act = () => UsernamePolicy.RequireNewAssignment("Bad Name!");

        act.Should().Throw<UsernameException>().Which.Message.Should().Be(UsernameRules.FormatGuidance);
    }

    [Theory]
    [InlineData("  Alice  ", "alice")]
    [InlineData("ALICE.Example", "alice.example")]
    public void Normalization_is_trim_then_lower_case(string input, string expected)
    {
        UsernameRules.Normalize(input).Should().Be(expected);
        UsernamePolicy.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Length_bounds_are_shared()
    {
        UsernameRules.MinLength.Should().Be(UsernamePolicy.MinLength).And.Be(3);
        UsernameRules.MaxLength.Should().Be(UsernamePolicy.MaxLength).And.Be(64);
    }
}
