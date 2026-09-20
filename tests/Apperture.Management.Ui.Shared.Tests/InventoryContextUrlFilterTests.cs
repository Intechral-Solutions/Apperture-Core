using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class InventoryContextUrlFilterTests
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
    [InlineData("  ")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Resolve_BlankOrAll_IsAll_WithNoApiFilter(string? token)
    {
        var resolution = InventoryContextUrlFilter.Resolve(token, Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.All);
        resolution.ApiValue.Should().BeEmpty();
    }

    [Theory]
    [InlineData("system")]
    [InlineData("System")]
    public void Resolve_System_IsAPseudoContext_NeverLookedUpInTheDirectory(string token)
    {
        // Even a (hypothetical) directory entry carrying the slug must not capture the pseudo token.
        var directory = new List<ResourceContextDto> { Ctx("weird-id", "system") };

        var resolution = InventoryContextUrlFilter.Resolve(token, directory);

        resolution.Kind.Should().Be(InventoryContextFilterKind.System);
        resolution.ApiValue.Should().Be("system");
    }

    [Theory]
    [InlineData("notRecorded")]
    [InlineData("notrecorded")]
    public void Resolve_NotRecorded_IsAPseudoContext_AndReachesTheApiAsTheServersToken(string token)
    {
        var resolution = InventoryContextUrlFilter.Resolve(token, Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.NotRecorded);
        resolution.ApiValue.Should().Be("notRecorded");
    }

    [Fact]
    public void Resolve_Slug_SendsTheCanonicalIdAndReportsTheSlugAsTheCanonicalUrlToken()
    {
        var resolution = InventoryContextUrlFilter.Resolve("development", Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.Context);
        resolution.ApiValue.Should().Be(DevId);
        resolution.CanonicalUrlToken.Should().Be("development");
    }

    [Fact]
    public void Resolve_LegacyId_StillWorks_AndTheCanonicalUrlTokenIsTheSlug()
    {
        var resolution = InventoryContextUrlFilter.Resolve(DevId, Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.Context);
        resolution.ApiValue.Should().Be(DevId);
        resolution.CanonicalUrlToken.Should().Be("development");
    }

    [Fact]
    public void Resolve_Shared_IsAPersistedContextWhoseTokenIsShared()
    {
        var resolution = InventoryContextUrlFilter.Resolve("shared", Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.Context);
        resolution.ApiValue.Should().Be(ResourceContextIds.SharedValue);
        resolution.CanonicalUrlToken.Should().Be("shared");
    }

    [Fact]
    public void Resolve_ArchivedApplication_IsAcceptedForHistoricalInspection()
    {
        var resolution = InventoryContextUrlFilter.Resolve("retired-app", Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.Context);
        resolution.ApiValue.Should().Be("1111aaaa2222bbbb3333cccc4444dddd");
    }

    [Fact]
    public void Resolve_UnknownToken_IsUnknown_AndPassesTheRawTokenSoTheServerKeepsRejectingIt()
    {
        var resolution = InventoryContextUrlFilter.Resolve("made-up-slug", Directory());

        resolution.Kind.Should().Be(InventoryContextFilterKind.Unknown);
        resolution.ApiValue.Should().Be("made-up-slug");
        resolution.CanonicalUrlToken.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnApplicationWhoseSlugLooksLikeAPseudoToken_IsStillAddressableByIdAndCanonicalizesToTheId()
    {
        // The pseudo tokens are checked first (mirroring the server), so slug "notrecorded" cannot be
        // used as a URL token on Inventory; the canonical id keeps working and is what gets emitted.
        var directory = new List<ResourceContextDto> { Ctx(DevId, "notrecorded") };

        var byId = InventoryContextUrlFilter.Resolve(DevId, directory);

        byId.Kind.Should().Be(InventoryContextFilterKind.Context);
        byId.ApiValue.Should().Be(DevId);
        byId.CanonicalUrlToken.Should().Be(DevId);
        InventoryContextUrlFilter.Resolve("notrecorded", directory).Kind.Should().Be(InventoryContextFilterKind.NotRecorded);
    }
}
