using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.UnitTests.Accounts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UserReferenceResolverTests
{
    private static User UserOf(string id, string username, string displayName) =>
        new(new UserId(id), displayName, [new IdentityLink(IdentitySource.Local, username, null)], [], false) { Username = username };

    [Fact]
    public async Task Resolves_every_known_user_in_one_call_with_username_and_display_name()
    {
        var repo = new InMemoryUserRepository(
            UserOf("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace"),
            UserOf("aaaaaaaaaaaaaaaaaaaaaaa2", "grace", "Grace Hopper"));
        var resolver = new UserReferenceResolver(repo);

        var references = await resolver.ResolveAsync([new UserId("aaaaaaaaaaaaaaaaaaaaaaa1"), new UserId("aaaaaaaaaaaaaaaaaaaaaaa2")]);

        references.Should().HaveCount(2);
        references[new UserId("aaaaaaaaaaaaaaaaaaaaaaa1")].Should().Be(new UserReferenceDto("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace"));
        references[new UserId("aaaaaaaaaaaaaaaaaaaaaaa2")].Should().Be(new UserReferenceDto("aaaaaaaaaaaaaaaaaaaaaaa2", "grace", "Grace Hopper"));
    }

    [Fact]
    public async Task Duplicate_ids_resolve_once_and_unknown_ids_are_omitted_not_invented()
    {
        var repo = new InMemoryUserRepository(UserOf("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace"));
        var resolver = new UserReferenceResolver(repo);

        var references = await resolver.ResolveAsync([
            new UserId("aaaaaaaaaaaaaaaaaaaaaaa1"),
            new UserId("aaaaaaaaaaaaaaaaaaaaaaa1"),
            new UserId("ffffffffffffffffffffffff")]);

        references.Keys.Should().Equal(new UserId("aaaaaaaaaaaaaaaaaaaaaaa1"));
    }

    [Fact]
    public async Task No_ids_resolves_to_an_empty_map() =>
        (await new UserReferenceResolver(new InMemoryUserRepository()).ResolveAsync([])).Should().BeEmpty();
}
