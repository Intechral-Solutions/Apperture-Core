using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real SQL Server proof for user search and user references: username prefix and case-insensitive display-name
/// substring matching, escaping of LIKE wildcards, exact-username-first ordering, Recovery Access exclusion,
/// and the fields search deliberately does not read (email, user id).
/// </summary>
/// <remarks>
/// Without a reachable SQL endpoint the tests fail (see <see cref="SqlTestExecution"/>).
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class UserSearchSqlServerTests
{
    private readonly ITestOutputHelper _output;

    public UserSearchSqlServerTests(ITestOutputHelper output) => _output = output;

    private static UserEntity Person(string id, string username, string displayName, string? email = null, bool recovery = false) =>
        new()
        {
            Id = id,
            Username = username,
            DisplayName = displayName,
            EmailAddress = email,
            EmailVerifiedAtUtc = email is null ? null : DateTimeOffset.UtcNow,
            IsBreakGlassEligible = recovery,
        };

    private const string AliceId = "a00000000000000000000001";
    private const string AliciaId = "a00000000000000000000002";
    private const string BobId = "a00000000000000000000003";
    private const string AaronId = "a00000000000000000000004";
    private const string RecoveryId = "a00000000000000000000005";

    private async Task<(SqlServerScratchDatabase Scratch, SqlUserRepository Repo, AppertureDbContext Db)> SeedAsync(params UserEntity[] extra)
    {
        // Throws when no SQL Server endpoint is reachable.
        var scratch = await SqlServerScratchDatabase.CreateAsync("Apperture_Iam93Search_");

        var db = scratch.NewDb();
        // Ids are deliberately not in username order, so ordering by Id would be visibly wrong.
        db.AddUsersWithCurrentUsernames(
            Person(AliceId, "alice", "Alice Smith", email: "alice@example.com"),
            Person(AliciaId, "alicia", "Alicia Keys"),
            Person(BobId, "bob", "Bob Alison"),
            Person(AaronId, "aaron", "The Alice Fan"),
            Person(RecoveryId, "breakglass", "Recovery Access", recovery: true));
        if (extra.Length > 0)
            db.AddUsersWithCurrentUsernames(extra);
        await db.SaveChangesAsync();
        _output.WriteLine("EXECUTED against SQL Server scratch catalog");
        return (scratch, new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance), db);
    }

    private static async Task<string[]> UsernamesAsync(SqlUserRepository repo, string? q, int skip = 0, int take = 20)
    {
        var (page, _) = await repo.SearchPageAsync(UserSearchCriteria.Create(q, skip, take));
        return page.Select(u => u.Username).ToArray();
    }

    [Fact]
    public async Task Username_prefix_and_display_name_substring_both_match_and_are_ordered_by_username()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        // alice, alicia: username prefix. bob: display name "Bob Alison" contains "ali". aaron: "The Alice Fan".
        (await UsernamesAsync(seeded.Repo, "ali")).Should().Equal("aaron", "alice", "alicia", "bob");
    }

    [Fact]
    public async Task An_exact_username_match_sorts_before_other_matches_that_come_earlier_alphabetically()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        // "aaron" matches only through its display name and sorts before "alice", yet the exact username wins.
        (await UsernamesAsync(seeded.Repo, "alice")).Should().Equal("alice", "aaron");
    }

    [Fact]
    public async Task Display_name_matching_is_case_insensitive_and_username_matching_is_a_prefix_not_a_substring()
    {
        var seeded = await SeedAsync(Person("a00000000000000000000020", "qz.mid", "Plain Name"));
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        (await UsernamesAsync(seeded.Repo, "SMI")).Should().Equal("alice");
        (await UsernamesAsync(seeded.Repo, "PLAIN")).Should().Equal("qz.mid");
        (await UsernamesAsync(seeded.Repo, "qz")).Should().Equal("qz.mid");
        (await UsernamesAsync(seeded.Repo, "z.m")).Should().BeEmpty("a username matches by prefix only");
    }

    [Fact]
    public async Task Like_wildcards_in_the_query_are_literal_characters()
    {
        var seeded = await SeedAsync(
            Person("a00000000000000000000010", "a_b", "Underscore User"),
            Person("a00000000000000000000011", "axb", "Letter User"),
            Person("a00000000000000000000012", "pct", "100% Legit"),
            Person("a00000000000000000000013", "thousand", "1000 Legit"),
            Person("a00000000000000000000014", "slash", "Back\\Slash"));
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        (await UsernamesAsync(seeded.Repo, "a_b")).Should().Equal("a_b");
        (await UsernamesAsync(seeded.Repo, "100%")).Should().Equal("pct");
        (await UsernamesAsync(seeded.Repo, "%")).Should().Equal("pct");
        (await UsernamesAsync(seeded.Repo, "_")).Should().BeEmpty("'_' is a literal, not 'any one character'");
        (await UsernamesAsync(seeded.Repo, "back\\sl")).Should().Equal("slash");
        (await UsernamesAsync(seeded.Repo, "[a-z]")).Should().BeEmpty();
    }

    [Fact]
    public async Task Email_and_user_id_are_never_searched()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        (await UsernamesAsync(seeded.Repo, "example.com")).Should().BeEmpty();
        (await UsernamesAsync(seeded.Repo, "alice@example.com")).Should().BeEmpty();
        (await UsernamesAsync(seeded.Repo, AliceId)).Should().BeEmpty();
        (await UsernamesAsync(seeded.Repo, "a0000000")).Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_access_is_excluded_from_results_and_the_total()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        (await UsernamesAsync(seeded.Repo, "break")).Should().BeEmpty();
        (await UsernamesAsync(seeded.Repo, "recovery")).Should().BeEmpty();
        var (page, total) = await seeded.Repo.SearchPageAsync(UserSearchCriteria.Create(null, 0, 100));
        page.Select(u => u.Username).Should().NotContain("breakglass");
        total.Should().Be(4);
    }

    [Fact]
    public async Task The_unfiltered_list_is_ordered_by_username_and_pages_with_a_filtered_total()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;
        var repo = seeded.Repo;

        (await UsernamesAsync(repo, null)).Should().Equal("aaron", "alice", "alicia", "bob");

        var (second, total) = await repo.SearchPageAsync(UserSearchCriteria.Create("ali", skip: 1, take: 2));
        second.Select(u => u.Username).Should().Equal("alice", "alicia");
        total.Should().Be(4, "the total counts every match, not just the page");

        var (legacy, legacyTotal) = await repo.GetPageAsync(0, 10);
        legacy.Select(u => u.Username).Should().Equal("aaron", "alice", "alicia", "bob");
        legacyTotal.Should().Be(4);
    }

    [Fact]
    public async Task Results_carry_identity_links_and_roles_like_the_plain_list()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = "lnk0000000000000000001",
            UserId = AliceId,
            Source = (int)IdentitySource.Local,
            ExternalId = "alice",
        });
        await db.SaveChangesAsync();

        var (page, _) = await seeded.Repo.SearchPageAsync(UserSearchCriteria.Create("alice", 0, 5));

        page.Should().ContainSingle(u => u.Username == "alice")
            .Which.IdentityLinks.Should().ContainSingle(l => l.Source == IdentitySource.Local && l.ExternalId == "alice");
    }

    [Fact]
    public async Task References_resolve_in_one_query_including_recovery_access_and_omit_unknown_ids()
    {
        var seeded = await SeedAsync();
        await using var scratch = seeded.Scratch;
        await using var db = seeded.Db;

        var references = await seeded.Repo.GetReferencesAsync(
            [new UserId(AliceId), new UserId(RecoveryId), new UserId("ffffffffffffffffffffffff")]);

        references.Should().BeEquivalentTo(new[]
        {
            new UserReferenceDto(AliceId, "alice", "Alice Smith"),
            new UserReferenceDto(RecoveryId, "breakglass", "Recovery Access"),
        });
    }
}
