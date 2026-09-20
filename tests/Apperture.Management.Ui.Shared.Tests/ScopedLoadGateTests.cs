using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ScopedLoadGateTests
{
    private const string DevId = "e51310001eab4e50a1b2c3d4e5f60718";
    private const string OpsId = "0a1b2c3d4e5f60718e51310001eab4e5";

    private static ResourceContextDto Ctx(string id, string slug) =>
        new(id, $"Name of {slug}", slug, ResourceContextKinds.Application, ResourceContextStates.Active,
            IsReserved: false, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

    private static readonly IReadOnlyList<ResourceContextDto> Directory = [Ctx(DevId, "development"), Ctx(OpsId, "operations")];

    /// <summary>Mirrors <c>RunsList.BuildLoadKey</c>: sibling filters plus the canonical Context load key.</summary>
    private static string RunsKey(string? queryContext, string status = "")
    {
        var context = ScopedListContextFilter.Resolve(queryContext, Directory);
        context.State.Should().Be(ScopedListContextState.Ready);
        return $"{status}{context.LoadKey}";
    }

    [Fact]
    public void FirstKey_Begins()
    {
        new ScopedLoadGate().TryBegin(RunsKey(DevId)).Should().BeTrue();
    }

    [Fact]
    public void LegacyIdUrlThenItsSlugRewrite_ProduceExactlyOneLoad_EvenWhileTheFirstLoadIsStillInFlight()
    {
        var gate = new ScopedLoadGate();
        var loads = 0;

        // In-app navigation to a legacy-id Runs URL: load 1 starts (and has not finished).
        if (gate.TryBegin(RunsKey(DevId))) loads++;
        // The shell canonicalizes the URL to the slug while load 1 is in flight: OnParametersSet runs again.
        if (gate.TryBegin(RunsKey("development"))) loads++;

        loads.Should().Be(1);
    }

    [Fact]
    public void NavigatingToAGenuinelyDifferentContext_StillReloads()
    {
        var gate = new ScopedLoadGate();

        gate.TryBegin(RunsKey("development")).Should().BeTrue();
        gate.TryBegin(RunsKey("operations")).Should().BeTrue();
        gate.TryBegin(RunsKey("all")).Should().BeTrue();
    }

    [Fact]
    public void ChangingASiblingFilterForTheSameContext_StillReloads()
    {
        var gate = new ScopedLoadGate();

        gate.TryBegin(RunsKey("development", status: "")).Should().BeTrue();
        gate.TryBegin(RunsKey("development", status: "blocked")).Should().BeTrue();
        gate.TryBegin(RunsKey("development", status: "blocked")).Should().BeFalse();
    }

    [Fact]
    public void AfterReset_TheSameKeyLoadsAgain_SoAContextThatBecameUnresolvableAndReturnsIsFresh()
    {
        var gate = new ScopedLoadGate();
        gate.TryBegin(RunsKey("development")).Should().BeTrue();

        gate.Reset();

        gate.TryBegin(RunsKey("development")).Should().BeTrue();
    }

    [Fact]
    public void MarkBegun_RecordsADirectLoad_SoTheFollowingParameterPassDoesNotLoadAgain()
    {
        var gate = new ScopedLoadGate();

        gate.MarkBegun(RunsKey("development", status: "failed"));

        gate.TryBegin(RunsKey("development", status: "failed")).Should().BeFalse();
        gate.TryBegin(RunsKey("development", status: "")).Should().BeTrue();
    }
}
