using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NotificationToastDeltaCursorTests
{
    [Fact]
    public void Empty_query_is_bootstrap_even_when_the_binder_invents_a_timestamp()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            new QueryCollection(),
            boundCreatedAtUtc: DateTimeOffset.UtcNow,
            boundAfterId: null,
            out var createdAtUtc,
            out var afterId,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        createdAtUtc.Should().BeNull();
        afterId.Should().BeNull();
    }

    [Fact]
    public void Empty_query_keys_are_bootstrap()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterCreatedAtUtc", ""), ("afterId", "")),
            boundCreatedAtUtc: DateTimeOffset.UnixEpoch,
            boundAfterId: "ignored",
            out var createdAtUtc,
            out var afterId,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        createdAtUtc.Should().BeNull();
        afterId.Should().BeNull();
    }

    [Fact]
    public void Both_components_resolve_to_the_bound_pair()
    {
        var time = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterCreatedAtUtc", "2026-06-01T10:00:00Z"), ("afterId", "inbox-1")),
            boundCreatedAtUtc: time,
            boundAfterId: "inbox-1",
            out var createdAtUtc,
            out var afterId,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        createdAtUtc.Should().Be(time);
        afterId.Should().Be("inbox-1");
    }

    [Fact]
    public void Timestamp_without_id_is_rejected()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterCreatedAtUtc", "2026-06-01T10:00:00Z")),
            boundCreatedAtUtc: DateTimeOffset.Parse("2026-06-01T10:00:00Z"),
            boundAfterId: null,
            out _,
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Be(NotificationToastDeltaCursor.MismatchedPairDetail);
    }

    [Fact]
    public void Id_without_timestamp_is_rejected()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterId", "inbox-1")),
            boundCreatedAtUtc: null,
            boundAfterId: "inbox-1",
            out _,
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Be(NotificationToastDeltaCursor.MismatchedPairDetail);
    }

    [Fact]
    public void Present_unparsed_timestamp_with_id_is_parsed()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterCreatedAtUtc", "2026-06-01T10:00:00Z"), ("afterId", "inbox-1")),
            boundCreatedAtUtc: null,
            boundAfterId: null,
            out var createdAtUtc,
            out var afterId,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        createdAtUtc.Should().Be(DateTimeOffset.Parse("2026-06-01T10:00:00Z"));
        afterId.Should().Be("inbox-1");
    }

    [Fact]
    public void Invalid_timestamp_with_id_is_rejected()
    {
        var ok = NotificationToastDeltaCursor.TryResolve(
            Query(("afterCreatedAtUtc", "not-a-timestamp"), ("afterId", "inbox-1")),
            boundCreatedAtUtc: null,
            boundAfterId: "inbox-1",
            out _,
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Be(NotificationToastDeltaCursor.InvalidTimestampDetail);
    }

    private static QueryCollection Query(params (string Key, string Value)[] pairs)
    {
        var store = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
            store[key] = value;
        return new QueryCollection(store);
    }
}
