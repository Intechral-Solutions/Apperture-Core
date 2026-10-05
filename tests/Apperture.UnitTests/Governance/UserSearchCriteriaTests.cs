using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UserSearchCriteriaTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("  Alice  ", "alice")]
    [InlineData("ALICE.SMITH", "alice.smith")]
    [InlineData("Ünal", "ünal")]
    public void Query_is_trimmed_and_lower_cased(string raw, string expected) =>
        UserSearchCriteria.Create(raw, 0, 20).Query.Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_query_means_an_unfiltered_list(string? raw) =>
        UserSearchCriteria.Create(raw, 0, 20).Query.Should().BeNull();

    [Theory]
    [InlineData(20, 20)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public void Take_is_clamped_to_one_through_one_hundred(int requested, int expected) =>
        UserSearchCriteria.Create(null, 0, requested).Take.Should().Be(expected);

    [Fact]
    public void Take_defaults_to_twenty_for_callers_that_do_not_choose() =>
        UserSearchCriteria.DefaultTake.Should().Be(20);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(-1, 0)]
    public void Skip_is_never_negative(int requested, int expected) =>
        UserSearchCriteria.Create(null, requested, 20).Skip.Should().Be(expected);

    [Fact]
    public void Wildcards_in_the_query_are_kept_verbatim_for_the_store_to_escape() =>
        UserSearchCriteria.Create(" 50%_off ", 0, 20).Query.Should().Be("50%_off");
}
