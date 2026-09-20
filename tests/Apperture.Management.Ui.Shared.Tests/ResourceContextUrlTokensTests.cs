using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ResourceContextUrlTokensTests
{
    private const string DevId = "e51310001eab4e50a1b2c3d4e5f60718";
    private const string OpsId = "0a1b2c3d4e5f60718e51310001eab4e5";

    private static ResourceContextDto Ctx(string id, string slug, string kind = ResourceContextKinds.Application, bool archived = false) =>
        new(id, $"Name of {slug}", slug, kind,
            archived ? ResourceContextStates.Archived : ResourceContextStates.Active,
            IsReserved: kind == ResourceContextKinds.Shared, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

    private static ResourceContextDto Shared() => Ctx(ResourceContextIds.SharedValue, "shared", ResourceContextKinds.Shared);

    private static IReadOnlyList<ResourceContextDto> Directory() =>
    [
        Shared(),
        Ctx(DevId, "development"),
        Ctx(OpsId, "operations"),
        Ctx("1111aaaa2222bbbb3333cccc4444dddd", "retired-app", archived: true)
    ];

    // --- Resolve ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankToken_IsBlank(string? token)
    {
        ResourceContextUrlTokens.Resolve(token, Directory()).Kind.Should().Be(ResourceContextTokenKind.Blank);
    }

    [Fact]
    public void Resolve_AllToken_IsAll_NotAContext()
    {
        var resolution = ResourceContextUrlTokens.Resolve("all", Directory());

        resolution.Kind.Should().Be(ResourceContextTokenKind.All);
        resolution.Context.Should().BeNull();
        resolution.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void Resolve_AllToken_IsNeverResolvedAsASlug_EvenIfADirectoryEntryCarriesIt()
    {
        var directory = new List<ResourceContextDto> { Ctx("weird-id", "all") };

        ResourceContextUrlTokens.Resolve("all", directory).Kind.Should().Be(ResourceContextTokenKind.All);
    }

    [Fact]
    public void Resolve_ActiveContextById_ReturnsCanonicalId()
    {
        var resolution = ResourceContextUrlTokens.Resolve(DevId, Directory());

        resolution.Kind.Should().Be(ResourceContextTokenKind.Context);
        resolution.MatchedBy.Should().Be(ResourceContextTokenMatch.Id);
        resolution.ResourceContextId.Should().Be(DevId);
        resolution.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ActiveContextBySlug_ReturnsCanonicalId()
    {
        var resolution = ResourceContextUrlTokens.Resolve("development", Directory());

        resolution.Kind.Should().Be(ResourceContextTokenKind.Context);
        resolution.MatchedBy.Should().Be(ResourceContextTokenMatch.Slug);
        resolution.ResourceContextId.Should().Be(DevId);
    }

    [Fact]
    public void Resolve_SlugMatchIsCaseInsensitive_BecauseSlugsAreUniqueCaseInsensitively()
    {
        ResourceContextUrlTokens.Resolve("Development", Directory()).ResourceContextId.Should().Be(DevId);
    }

    [Fact]
    public void Resolve_IdMatchIsCaseSensitive()
    {
        ResourceContextUrlTokens.Resolve(DevId.ToUpperInvariant(), Directory()).Kind.Should().Be(ResourceContextTokenKind.Unknown);
    }

    [Fact]
    public void Resolve_Shared_ResolvesByItsNaturalTokenToTheSharedCanonicalId()
    {
        var resolution = ResourceContextUrlTokens.Resolve("shared", Directory());

        resolution.Kind.Should().Be(ResourceContextTokenKind.Context);
        resolution.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public void Resolve_ArchivedContext_ByIdAndBySlug_ResolvesAndIsFlaggedArchived()
    {
        var byId = ResourceContextUrlTokens.Resolve("1111aaaa2222bbbb3333cccc4444dddd", Directory());
        var bySlug = ResourceContextUrlTokens.Resolve("retired-app", Directory());

        byId.Kind.Should().Be(ResourceContextTokenKind.Context);
        byId.IsArchived.Should().BeTrue();
        bySlug.Kind.Should().Be(ResourceContextTokenKind.Context);
        bySlug.IsArchived.Should().BeTrue();
        bySlug.ResourceContextId.Should().Be(byId.ResourceContextId);
    }

    [Theory]
    [InlineData("no-such-slug")]
    [InlineData("ffffffffffffffffffffffffffffffff")]
    [InlineData("system")]
    [InlineData("notRecorded")]
    public void Resolve_UnknownToken_IsUnknown_IncludingInventoryOnlyPseudoValues(string token)
    {
        var resolution = ResourceContextUrlTokens.Resolve(token, Directory());

        resolution.Kind.Should().Be(ResourceContextTokenKind.Unknown);
        resolution.Context.Should().BeNull();
    }

    [Fact]
    public void Resolve_WhenATokenIsOneContextsIdAndAnotherContextsSlug_TheIdWins()
    {
        // Not expected in practice (ids are 32-hex guids), but the model does not forbid a slug of that
        // shape, so canonical id identity must stay authoritative rather than depend on the shapes.
        var directory = new List<ResourceContextDto>
        {
            Ctx(DevId, "development"),
            Ctx("other-id", DevId)
        };

        var resolution = ResourceContextUrlTokens.Resolve(DevId, directory);

        resolution.ResourceContextId.Should().Be(DevId);
        resolution.MatchedBy.Should().Be(ResourceContextTokenMatch.Id);
    }

    // --- TokenFor ---

    [Fact]
    public void TokenFor_ActiveApplication_EmitsItsSlug()
    {
        ResourceContextUrlTokens.TokenFor(DevId, Directory()).Should().Be("development");
    }

    [Fact]
    public void TokenFor_Shared_EmitsItsNaturalToken()
    {
        ResourceContextUrlTokens.TokenFor(ResourceContextIds.SharedValue, Directory()).Should().Be("shared");
    }

    [Fact]
    public void TokenFor_ArchivedApplication_StillEmitsItsSlug()
    {
        ResourceContextUrlTokens.TokenFor("1111aaaa2222bbbb3333cccc4444dddd", Directory()).Should().Be("retired-app");
    }

    [Fact]
    public void TokenFor_IdNotInTheDirectory_FallsBackToTheId()
    {
        ResourceContextUrlTokens.TokenFor("not-in-directory", Directory()).Should().Be("not-in-directory");
    }

    [Fact]
    public void TokenFor_BlankSlug_FallsBackToTheId()
    {
        var directory = new List<ResourceContextDto> { Ctx(DevId, "") };

        ResourceContextUrlTokens.TokenFor(DevId, directory).Should().Be(DevId);
    }

    [Fact]
    public void TokenFor_SlugThatWouldResolveToADifferentContext_FallsBackToTheId()
    {
        // The emitted token must round-trip to the same context. A slug that is another context's id
        // would resolve to that other context (id wins), so it is not usable as a URL token.
        var directory = new List<ResourceContextDto>
        {
            Ctx(DevId, "development"),
            Ctx("other-id", DevId)
        };

        ResourceContextUrlTokens.TokenFor("other-id", directory).Should().Be("other-id");
    }

    [Fact]
    public void TokenFor_AllScope_IsTheAllToken()
    {
        ResourceContextUrlTokens.TokenFor(ResourceContextViewScope.All, Directory()).Should().Be("all");
    }

    [Fact]
    public void TokenFor_SpecificScope_EmitsSlug()
    {
        ResourceContextUrlTokens.TokenFor(ResourceContextViewScope.Of(DevId), Directory()).Should().Be("development");
    }

    // --- Slug rename (no alias history) ---

    [Fact]
    public void AfterASlugRename_TheIdAndNewSlugResolve_AndTheOldSlugDoesNot()
    {
        var before = new List<ResourceContextDto> { Shared(), Ctx(DevId, "development") };
        var after = new List<ResourceContextDto> { Shared(), Ctx(DevId, "dev-app") };

        ResourceContextUrlTokens.Resolve("development", before).ResourceContextId.Should().Be(DevId);

        ResourceContextUrlTokens.Resolve(DevId, after).ResourceContextId.Should().Be(DevId);
        ResourceContextUrlTokens.Resolve("dev-app", after).ResourceContextId.Should().Be(DevId);
        ResourceContextUrlTokens.Resolve("development", after).Kind.Should().Be(ResourceContextTokenKind.Unknown);
        ResourceContextUrlTokens.TokenFor(DevId, after).Should().Be("dev-app");
    }

    [Fact]
    public void AfterASlugIsReassignedToAnotherContext_TheOldSlugResolvesToTheNewHolderNotSilentlyToTheOldOne()
    {
        // ADR-0041: no alias history. A slug is only ever matched against the current directory.
        var after = new List<ResourceContextDto> { Ctx(DevId, "dev-app"), Ctx(OpsId, "development") };

        ResourceContextUrlTokens.Resolve("development", after).ResourceContextId.Should().Be(OpsId);
    }

    // --- ResolveActiveCreateTarget (creation-flow preselection) ---

    [Theory]
    [InlineData("development", DevId)]
    [InlineData(DevId, DevId)]
    [InlineData("shared", "shared")]
    public void ResolveActiveCreateTarget_SlugOrLegacyIdOrShared_ReturnsCanonicalId(string token, string expected)
    {
        ResourceContextUrlTokens.ResolveActiveCreateTarget(token, Directory()).Should().Be(expected);
    }

    [Theory]
    [InlineData("retired-app")]
    [InlineData("1111aaaa2222bbbb3333cccc4444dddd")]
    [InlineData("nonexistent")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveActiveCreateTarget_ArchivedUnknownAllOrBlank_IsNotSelectable(string? token)
    {
        ResourceContextUrlTokens.ResolveActiveCreateTarget(token, Directory()).Should().BeNull();
    }

    [Fact]
    public void ResolveActiveCreateTarget_Shared_IsAcceptedEvenBeforeTheDirectoryListsIt()
    {
        // Preserves the pre-HSK-C rule that "shared" is always an acceptable create target.
        ResourceContextUrlTokens.ResolveActiveCreateTarget("shared", []).Should().Be(ResourceContextIds.SharedValue);
    }
}
