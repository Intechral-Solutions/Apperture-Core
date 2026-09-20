using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ScopedListContextFilterTests
{
    private const string DevId = "e51310001eab4e50a1b2c3d4e5f60718";

    private static ResourceContextDto Ctx(string id, string slug, string kind = ResourceContextKinds.Application, bool archived = false) =>
        new(id, $"Name of {slug}", slug, kind,
            archived ? ResourceContextStates.Archived : ResourceContextStates.Active,
            IsReserved: kind == ResourceContextKinds.Shared, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

    private static IReadOnlyList<ResourceContextDto> Directory() =>
    [
        Ctx(ResourceContextIds.SharedValue, "shared", ResourceContextKinds.Shared),
        Ctx(DevId, "development"),
        Ctx("1111aaaa2222bbbb3333cccc4444dddd", "retired-app", archived: true)
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingOrWhitespaceQueryContext_ReturnsAwaitingContext(string? queryContext)
    {
        var resolution = ScopedListContextFilter.Resolve(queryContext, Directory());

        resolution.State.Should().Be(ScopedListContextState.AwaitingContext);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().BeNull();
        resolution.IsAll.Should().BeFalse();
    }

    [Fact]
    public void Resolve_All_ReturnsReadyWithNullApiFilter()
    {
        var resolution = ScopedListContextFilter.Resolve("all", Directory());

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().Be("all");
        resolution.IsAll.Should().BeTrue();
    }

    [Fact]
    public void Resolve_Slug_SendsTheCanonicalIdToTheApi_AndKeepsTheSlugAsTheUrlToken()
    {
        var resolution = ScopedListContextFilter.Resolve("development", Directory());

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().Be(DevId);
        resolution.QueryValue.Should().Be("development");
        resolution.IsAll.Should().BeFalse();
    }

    [Fact]
    public void Resolve_LegacyId_SendsThatIdToTheApi_AndReportsTheSlugAsTheCanonicalUrlToken()
    {
        var resolution = ScopedListContextFilter.Resolve(DevId, Directory());

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().Be(DevId);
        resolution.QueryValue.Should().Be("development");
    }

    [Fact]
    public void Resolve_SlugAndLegacyIdForm_ShareOneLoadKey_SoCanonicalizingTheUrlDoesNotReload()
    {
        var bySlug = ScopedListContextFilter.Resolve("development", Directory());
        var byId = ScopedListContextFilter.Resolve(DevId, Directory());

        byId.LoadKey.Should().Be(bySlug.LoadKey);
        ScopedListContextFilter.Resolve("all", Directory()).LoadKey.Should().NotBe(bySlug.LoadKey);
        ScopedListContextFilter.Resolve("shared", Directory()).LoadKey.Should().NotBe(bySlug.LoadKey);
    }

    [Fact]
    public void Resolve_Shared_UsesItsNaturalTokenAndTheSharedCanonicalId()
    {
        var resolution = ScopedListContextFilter.Resolve("shared", Directory());

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().Be(ResourceContextIds.SharedValue);
        resolution.QueryValue.Should().Be("shared");
    }

    [Fact]
    public void Resolve_ArchivedContext_BySlugAndById_IsReadyForHistoricalNavigation()
    {
        var bySlug = ScopedListContextFilter.Resolve("retired-app", Directory());
        var byId = ScopedListContextFilter.Resolve("1111aaaa2222bbbb3333cccc4444dddd", Directory());

        bySlug.State.Should().Be(ScopedListContextState.Ready);
        bySlug.ApiFilter.Should().Be("1111aaaa2222bbbb3333cccc4444dddd");
        byId.State.Should().Be(ScopedListContextState.Ready);
        byId.QueryValue.Should().Be("retired-app");
    }

    [Theory]
    [InlineData("ghost-slug")]
    [InlineData("ffffffffffffffffffffffffffffffff")]
    public void Resolve_UnknownToken_ReturnsUnknownContextWithNullApiFilter_AndKeepsTheRawToken(string token)
    {
        var resolution = ScopedListContextFilter.Resolve(token, Directory());

        resolution.State.Should().Be(ScopedListContextState.UnknownContext);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().Be(token);
        resolution.IsAll.Should().BeFalse();
    }
}
