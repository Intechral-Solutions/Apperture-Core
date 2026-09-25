using Apperture.Management.Ui.Client.Services;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

public sealed class BackgroundRequestTests
{
    [Fact]
    public void A_scope_is_active_only_until_it_is_disposed()
    {
        BackgroundRequest.IsActive.Should().BeFalse();
        var scope = BackgroundRequest.Begin();
        BackgroundRequest.IsActive.Should().BeTrue();
        scope.Dispose();
        BackgroundRequest.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Nested_scopes_stay_active_until_the_outer_scope_exits()
    {
        using (BackgroundRequest.Begin())
        {
            using (BackgroundRequest.Begin())
                BackgroundRequest.IsActive.Should().BeTrue();
            BackgroundRequest.IsActive.Should().BeTrue();
        }

        BackgroundRequest.IsActive.Should().BeFalse();
    }

    [Fact]
    public void An_exception_still_clears_the_scope()
    {
        var act = () =>
        {
            using (BackgroundRequest.Begin())
                throw new InvalidOperationException("boom");
        };

        act.Should().Throw<InvalidOperationException>();
        BackgroundRequest.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_still_clears_the_scope()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(async () =>
        {
            using (BackgroundRequest.Begin())
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
        }, CancellationToken.None);

        await started.Task;
        cts.Cancel();
        await task.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        BackgroundRequest.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_background_scope_does_not_mark_an_overlapping_foreground_flow()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var background = Task.Run(async () =>
        {
            using (BackgroundRequest.Begin())
            {
                entered.SetResult();
                await release.Task;
                BackgroundRequest.IsActive.Should().BeTrue();
            }
        });

        await entered.Task;
        BackgroundRequest.IsActive.Should().BeFalse("the test flow itself was never inside the scope");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://app.test/api/probe");
        var handler = new BrowserRequestHeadersHandler { InnerHandler = new StubHandler() };
        using var client = new HttpClient(handler);
        await client.SendAsync(request);
        request.Headers.Contains(BackgroundActivity.HeaderName).Should().BeFalse();

        release.SetResult();
        await background;
        BackgroundRequest.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task The_handler_adds_the_marker_only_inside_a_background_scope()
    {
        using var foreground = new HttpRequestMessage(HttpMethod.Get, "https://app.test/api/probe");
        var handler = new BrowserRequestHeadersHandler { InnerHandler = new StubHandler() };
        using var client = new HttpClient(handler);
        await client.SendAsync(foreground);
        foreground.Headers.Contains(BackgroundActivity.HeaderName).Should().BeFalse();

        using (BackgroundRequest.Begin())
        {
            using var background = new HttpRequestMessage(HttpMethod.Get, "https://app.test/api/probe");
            await client.SendAsync(background);
            background.Headers.GetValues(BackgroundActivity.HeaderName).Should().Equal(BackgroundActivity.HeaderValue);
        }

        using var after = new HttpRequestMessage(HttpMethod.Get, "https://app.test/api/probe");
        await client.SendAsync(after);
        after.Headers.Contains(BackgroundActivity.HeaderName).Should().BeFalse();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
