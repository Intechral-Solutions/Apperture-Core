using Apperture.Core.Application.Notifications.Subscriptions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

public sealed class NotificationRecipientAddressParserTests
{
    private readonly NotificationRecipientAddressParser _sut = new();

    [Theory]
    [InlineData("user@example.com", "user@example.com", null)]
    [InlineData("  User@Example.COM  ", "user@example.com", null)]
    public void Parses_bare_email(string input, string expectedEmail, string? expectedName)
    {
        var ok = _sut.TryParse(input, out var parsed, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        parsed!.Email.Should().Be(expectedEmail);
        parsed.DisplayName.Should().Be(expectedName);
    }

    [Fact]
    public void Parses_display_name_angle_address()
    {
        var ok = _sut.TryParse("Full Name <user@example.com>", out var parsed, out _);
        ok.Should().BeTrue();
        parsed!.Email.Should().Be("user@example.com");
        parsed.DisplayName.Should().Be("Full Name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("bad>\n@example.com")]
    public void Rejects_invalid_input(string input)
    {
        var ok = _sut.TryParse(input, out _, out var error);
        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
