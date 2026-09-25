using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.JSInterop;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A2a: the per-tab marker that stops an automatic redirect to Microsoft from repeating within
/// <see cref="LoginAutoRedirectPolicy.AttemptWindow"/>. It must survive the full-page navigations it guards
/// (a new instance over the same storage), tolerate garbage and a future stamp, and never let broken storage block sign-in.
/// </summary>
public sealed class SessionStorageLoginAutoRedirectMarkerTests
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

    [Fact]
    public async Task No_stamp_means_no_recent_attempt()
    {
        var marker = new SessionStorageLoginAutoRedirectMarker(new FakeSessionStorage(), new ManualTimeProvider());

        (await marker.WasRecentAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task An_attempt_is_recent_across_documents_until_the_window_passes()
    {
        var storage = new FakeSessionStorage();
        var clock = new ManualTimeProvider();
        await new SessionStorageLoginAutoRedirectMarker(storage, clock).MarkAsync();

        var nextDocument = new SessionStorageLoginAutoRedirectMarker(storage, clock);
        clock.Advance(LoginAutoRedirectPolicy.AttemptWindow - TimeSpan.FromSeconds(1));
        (await nextDocument.WasRecentAsync()).Should().BeTrue("the return from Microsoft is a new document, within the window");

        clock.Advance(TimeSpan.FromSeconds(2));
        (await nextDocument.WasRecentAsync()).Should().BeFalse("after the window a fresh attempt is allowed again");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("99999999999999999999999")]
    public async Task Garbage_is_ignored_not_thrown(string raw)
    {
        var storage = new FakeSessionStorage();
        storage.Items[SessionStorageLoginAutoRedirectMarker.StorageKey] = raw;

        (await new SessionStorageLoginAutoRedirectMarker(storage, new ManualTimeProvider()).WasRecentAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_stamp_from_the_future_does_not_count()
    {
        var storage = new FakeSessionStorage();
        var clock = new ManualTimeProvider();
        await new SessionStorageLoginAutoRedirectMarker(storage, clock).MarkAsync();
        var rewound = new ManualTimeProvider();
        rewound.Advance(TimeSpan.Zero - TimeSpan.FromMinutes(5));

        (await new SessionStorageLoginAutoRedirectMarker(storage, rewound).WasRecentAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Blocked_storage_degrades_to_memory_for_this_document_and_never_throws()
    {
        var clock = new ManualTimeProvider();
        var marker = new SessionStorageLoginAutoRedirectMarker(new FakeSessionStorage { Broken = true }, clock);

        var mark = () => marker.MarkAsync();
        await mark.Should().NotThrowAsync();
        (await marker.WasRecentAsync()).Should().BeTrue();
    }
}
