using Apperture.Management.Ui.Client.Services;
using FluentAssertions;
using Microsoft.JSInterop;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A1: the per-tab sessionStorage behind the loop breaker. It must round-trip across "documents" (a new
/// instance over the same storage), tolerate garbage, and never let a broken storage block a recovery.
/// </summary>
public sealed class SessionStorageRecoveryHistoryTests
{
    private sealed class FakeSessionStorage : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = new();

        public bool Broken { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (Broken)
                throw new JSException("SecurityError: storage is blocked");

            switch (identifier)
            {
                case "sessionStorage.getItem":
                    return ValueTask.FromResult((TValue)(object?)(Items.TryGetValue((string)args![0]!, out var v) ? v : null)!);
                case "sessionStorage.setItem":
                    Items[(string)args![0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new InvalidOperationException($"Unexpected JS interop call in test: {identifier}");
            }
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stamps_round_trip_across_documents_through_the_same_tab_storage()
    {
        var storage = new FakeSessionStorage();
        await new SessionStorageRecoveryHistory(storage).SaveAsync([T0, T0.AddSeconds(5)]);

        var reloaded = await new SessionStorageRecoveryHistory(storage).LoadAsync();

        reloaded.Should().Equal(T0, T0.AddSeconds(5));
    }

    [Fact]
    public async Task Empty_storage_loads_as_no_history()
    {
        (await new SessionStorageRecoveryHistory(new FakeSessionStorage()).LoadAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not,numbers,here")]
    [InlineData("-5,0,,   ")]
    [InlineData("99999999999999999999999")]
    public async Task Garbage_is_ignored_not_thrown(string raw)
    {
        var storage = new FakeSessionStorage();
        storage.Items[SessionStorageRecoveryHistory.StorageKey] = raw;

        var stamps = await new SessionStorageRecoveryHistory(storage).LoadAsync();

        stamps.Should().BeEmpty();
    }

    [Fact]
    public async Task Storage_keeps_only_the_most_recent_stamps()
    {
        var storage = new FakeSessionStorage();
        var history = new SessionStorageRecoveryHistory(storage);

        await history.SaveAsync(Enumerable.Range(0, 25).Select(i => T0.AddSeconds(i)).ToArray());

        (await new SessionStorageRecoveryHistory(storage).LoadAsync()).Should().HaveCount(10).And.ContainInOrder(T0.AddSeconds(24));
    }

    [Fact]
    public async Task Blocked_storage_degrades_to_memory_for_this_document_and_never_throws()
    {
        var storage = new FakeSessionStorage { Broken = true };
        var history = new SessionStorageRecoveryHistory(storage);

        var save = () => history.SaveAsync([T0]);
        await save.Should().NotThrowAsync();
        (await history.LoadAsync()).Should().Equal(T0);
    }
}
