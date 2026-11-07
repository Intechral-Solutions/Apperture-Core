using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The Management Host's side of the attestation: what it writes at start and on each tick, that it never builds the Entra or
/// OpenID Connect options to do so, who owns the row when two processes of one host overlap, what a graceful stop does, and that a
/// failure to write defers the evidence and never the host. Time is a manual clock: no test sleeps.
/// </summary>
public sealed class HostConfigurationAttestationPublisherTests
{
    // ---------------------------------------------------------------- a clock the test moves

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_timers)
                _timers.Add(timer);
            return timer;
        }

        /// <summary>Moves the clock forward one interval at a time, firing every timer that falls due on the way.</summary>
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            while (true)
            {
                ManualTimer? next;
                lock (_timers)
                    next = _timers.Where(t => t.Active && t.Next <= end).MinBy(t => t.Next);
                if (next is null)
                    break;
                _now = next.Next;
                next.Fire();
            }

            _now = end;
        }

        public DateTimeOffset Now => _now;

        public int ActiveTimers
        {
            get
            {
                lock (_timers)
                    return _timers.Count(t => t.Active);
            }
        }

        internal sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period;

            public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _period = period;
                Next = owner._now + due;
                Active = due != Timeout.InfiniteTimeSpan;
            }

            public DateTimeOffset Next { get; private set; }

            public bool Active { get; private set; }

            public void Fire()
            {
                if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
                    Active = false;
                else
                    Next += _period;
                _callback(_state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _period = period;
                Next = _owner._now + dueTime;
                Active = dueTime != Timeout.InfiniteTimeSpan;
                return true;
            }

            public void Dispose() => Active = false;

            public ValueTask DisposeAsync()
            {
                Active = false;
                return ValueTask.CompletedTask;
            }
        }
    }

    // ---------------------------------------------------------------- a store that remembers what it was asked

    private sealed class RecordingStore : IHostConfigurationAttestationStore
    {
        public List<HostConfigurationAttestationWrite> Claims { get; } = [];

        public List<HostConfigurationAttestationWrite> Refreshes { get; } = [];

        public List<(string Host, string Process)> Stops { get; } = [];

        public List<HostConfigurationAttestationWrite> Reclaims { get; } = [];

        public Queue<HostAttestationRefreshOutcome> RefreshOutcomes { get; } = new();

        /// <summary>What the store answers to a reclaim; once empty it answers <see cref="HostAttestationReclaimOutcome.Held"/>, the safe answer.</summary>
        public Queue<HostAttestationReclaimOutcome> ReclaimOutcomes { get; } = new();

        public int FailClaims { get; set; }

        public int FailRefreshes { get; set; }

        public int FailReclaims { get; set; }

        public ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default)
        {
            if (FailReclaims > 0)
            {
                FailReclaims--;
                throw new InvalidOperationException("database unavailable");
            }

            Reclaims.Add(write);
            return ValueTask.FromResult(ReclaimOutcomes.Count > 0 ? ReclaimOutcomes.Dequeue() : HostAttestationReclaimOutcome.Held);
        }

        public ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default)
        {
            if (FailClaims > 0)
            {
                FailClaims--;
                throw new InvalidOperationException("database unavailable");
            }

            Claims.Add(write);
            return ValueTask.CompletedTask;
        }

        public ValueTask<HostAttestationRefreshOutcome> RefreshAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default)
        {
            if (FailRefreshes > 0)
            {
                FailRefreshes--;
                throw new InvalidOperationException("database unavailable");
            }

            Refreshes.Add(write);
            return ValueTask.FromResult(RefreshOutcomes.Count > 0 ? RefreshOutcomes.Dequeue() : HostAttestationRefreshOutcome.Refreshed);
        }

        public ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default)
        {
            Stops.Add((hostId, processInstanceId));
            return ValueTask.FromResult(true);
        }

        public ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public RecordingStore Store { get; } = new();

        public ManualTimeProvider Time { get; } = new();

        public EntraOptionsBuildHealth Health { get; } = new();

        public Mock<IDeploymentAuthenticationConfiguration> Deployment { get; } = new(MockBehavior.Strict);

        public HostAttestationIdentity Identity => new("host-a", "process-1", Time.Now);

        public HostConfigurationAttestationPublisher Publisher { get; }

        public Rig(bool localConfigured = true)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHostConfigurationAttestationStore>(Store);
            services.AddSingleton(Deployment.Object);
            var provider = services.BuildServiceProvider();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalAuth:Enabled"] = localConfigured.ToString(), ["Entra:Enabled"] = "true", ["Entra:ClientId"] = "c" })
                .Build();
            var source = new HostAttestationPayloadSource(config, provider.GetRequiredService<IServiceScopeFactory>(), Health);
            Publisher = new HostConfigurationAttestationPublisher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                source,
                Identity,
                Time,
                NullLogger<HostConfigurationAttestationPublisher>.Instance);
        }
    }

    private static AttestedAuthenticationPayload Payload(HostConfigurationAttestationWrite write)
    {
        AttestedAuthenticationPayload.TryParse(write.Payload, out var payload).Should().BeTrue();
        return payload!;
    }

    // ---------------------------------------------------------------- start

    [Fact]
    public async Task Start_claims_the_row_with_a_startup_attestation_that_is_local_trusted_and_entra_not_established()
    {
        var rig = new Rig();

        await rig.Publisher.StartAsync(CancellationToken.None);

        var claim = rig.Store.Claims.Should().ContainSingle().Subject;
        claim.HostId.Should().Be("host-a");
        claim.ProcessInstanceId.Should().Be("process-1");
        claim.SchemaVersion.Should().Be(AttestedAuthenticationPayload.SchemaVersion);
        var payload = Payload(claim);
        payload.LocalNow.Should().BeTrue();
        payload.LocalRestart.Should().BeTrue();
        payload.EntraNow.Should().BeNull("the process starting does not initialise Microsoft sign-in");
        payload.EntraBase.Enabled.Should().BeTrue("a configured flag is recorded as a configured value, never as the handler's reading");
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_does_not_build_the_entra_options_or_ask_the_host_for_its_running_reading()
    {
        var rig = new Rig();   // the deployment fake is strict: any Read() would throw

        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval * 3);

        rig.Deployment.Invocations.Should().BeEmpty("nothing was built, or asked for, before real traffic reached the host");
        rig.Store.Claims.Should().HaveCount(1);
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_succeeds_when_the_database_cannot_be_written_and_the_next_tick_asks_for_the_row_without_displacing_a_live_holder()
    {
        var rig = new Rig();
        rig.Store.FailClaims = 1;
        rig.Store.ReclaimOutcomes.Enqueue(HostAttestationReclaimOutcome.Reclaimed);

        var start = () => rig.Publisher.StartAsync(CancellationToken.None);
        await start.Should().NotThrowAsync("the host must start whatever the state of the evidence");
        rig.Store.Claims.Should().BeEmpty();

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);

        await WaitForAsync(() => rig.Store.Reclaims.Count == 1);
        rig.Store.Claims.Should().BeEmpty("only the first write of a process is unconditional: a retry must not take the row from a newer process");
        rig.Publisher.Superseded.Should().BeFalse();

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 1);
        await rig.Publisher.StopAsync(CancellationToken.None);
        rig.Store.Stops.Should().ContainSingle("the retry made this process the owner");
    }

    [Fact]
    public async Task A_start_whose_write_failed_and_whose_retry_finds_a_live_holder_is_superseded_and_does_not_publish()
    {
        var rig = new Rig();
        rig.Store.FailClaims = 1;   // the reclaim answers Held (the safe default): a newer process claimed meanwhile
        await rig.Publisher.StartAsync(CancellationToken.None);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);

        await WaitForAsync(() => rig.Publisher.Superseded);
        rig.Store.Claims.Should().BeEmpty();
        rig.Store.Refreshes.Should().BeEmpty();
        await rig.Publisher.StopAsync(CancellationToken.None);
        rig.Store.Stops.Should().BeEmpty("a process that never owned the row does not stop the holder's");
    }

    // ---------------------------------------------------------------- the tick

    [Fact]
    public async Task A_refresh_is_written_each_interval_and_not_before()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval - TimeSpan.FromTicks(1));
        await Task.Yield();
        rig.Store.Refreshes.Should().BeEmpty();

        rig.Time.Advance(TimeSpan.FromTicks(1));
        await WaitForAsync(() => rig.Store.Refreshes.Count == 1);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 2);
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_interval_leaves_room_for_two_missed_refreshes_inside_the_window()
    {
        (HostConfigurationAttestationTiming.RefreshInterval * 2).Should().BeLessThan(HostConfigurationAttestationTiming.FreshnessWindow);
        await Task.CompletedTask;
    }

    [Fact]
    public void The_reader_and_the_store_share_one_freshness_window_of_whole_seconds()
    {
        HostConfigurationAttestationTiming.FreshnessWindow.Should().Be(HostConfigurationAttestationLiveness.FreshnessWindow);
        HostConfigurationAttestationLiveness.FreshnessWindow.Should().Be(TimeSpan.FromSeconds(180));
        (HostConfigurationAttestationLiveness.FreshnessWindow.Ticks % TimeSpan.TicksPerSecond).Should().Be(0, "the store compares it in whole seconds");
    }

    [Fact]
    public async Task A_failed_refresh_is_retried_on_the_next_tick_and_never_stops_the_host()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.FailRefreshes = 1;

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.FailRefreshes == 0);
        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 1);
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_row_that_has_gone_is_taken_again_through_the_conditional_path()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.RefreshOutcomes.Enqueue(HostAttestationRefreshOutcome.Missing);
        rig.Store.ReclaimOutcomes.Enqueue(HostAttestationReclaimOutcome.Reclaimed);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);

        await WaitForAsync(() => rig.Store.Reclaims.Count == 1);
        rig.Store.Claims.Should().HaveCount(1, "the gone row is not an unconditional claim: someone may have claimed it since");
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_superseded_process_writes_nothing_to_the_row_while_the_holder_is_live_and_never_marks_the_holders_row_stopped()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.RefreshOutcomes.Enqueue(HostAttestationRefreshOutcome.Superseded);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Publisher.Superseded);

        // One interval at a time: a periodic timer coalesces ticks that arrive while the loop is busy.
        for (var tick = 2; tick <= 6; tick++)
        {
            rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
            await WaitForAsync(() => rig.Store.Reclaims.Count == tick);
        }

        rig.Store.Refreshes.Should().HaveCount(1, "a superseded process refreshes nothing: it only asks, and the store says no");
        rig.Publisher.Superseded.Should().BeTrue();
        (await rig.Publisher.PublishOnceAsync()).Should().BeFalse();

        await rig.Publisher.StopAsync(CancellationToken.None);
        rig.Store.Stops.Should().BeEmpty();
        rig.Store.Claims.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_superseded_process_asks_again_in_the_same_tick_and_owns_the_row_again_when_the_store_gives_it_back()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.RefreshOutcomes.Enqueue(HostAttestationRefreshOutcome.Superseded);
        rig.Store.ReclaimOutcomes.Enqueue(HostAttestationReclaimOutcome.Reclaimed);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);

        await WaitForAsync(() => rig.Store.Reclaims.Count == 1);
        await WaitForAsync(() => !rig.Publisher.Superseded);
        rig.Store.Reclaims[0].ProcessInstanceId.Should().Be("process-1");

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 2);
        await rig.Publisher.StopAsync(CancellationToken.None);
        rig.Store.Stops.Should().ContainSingle().Which.Should().Be(("host-a", "process-1"), "the stop closes the row this process owns again");
    }

    [Fact]
    public async Task A_superseded_process_that_was_refused_recovers_on_a_later_tick_and_not_before_the_store_agrees()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.RefreshOutcomes.Enqueue(HostAttestationRefreshOutcome.Superseded);

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Reclaims.Count == 1);
        rig.Publisher.Superseded.Should().BeTrue();

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Reclaims.Count == 2);
        rig.Publisher.Superseded.Should().BeTrue("a refusal changes nothing");

        rig.Store.ReclaimOutcomes.Enqueue(HostAttestationReclaimOutcome.Reclaimed);
        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => !rig.Publisher.Superseded);
        rig.Store.Claims.Should().HaveCount(1, "taking the row back is never an unconditional claim");
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failed_reclaim_is_retried_on_the_next_tick_and_does_not_change_who_the_process_thinks_owns_the_row()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Store.RefreshOutcomes.Enqueue(HostAttestationRefreshOutcome.Superseded);
        rig.Store.FailReclaims = 1;

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Publisher.Superseded && rig.Store.FailReclaims == 0);

        rig.Store.ReclaimOutcomes.Enqueue(HostAttestationReclaimOutcome.Reclaimed);
        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => !rig.Publisher.Superseded);
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------- stop

    [Fact]
    public async Task A_graceful_stop_marks_this_process_stopped_and_publishes_nothing_afterwards()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);

        await rig.Publisher.StopAsync(CancellationToken.None);

        rig.Store.Stops.Should().ContainSingle().Which.Should().Be(("host-a", "process-1"));
        (await rig.Publisher.PublishOnceAsync()).Should().BeFalse();
        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval * 3);
        rig.Store.Refreshes.Should().BeEmpty();
        rig.Time.ActiveTimers.Should().Be(0, "the loop's timer is released");
    }

    [Fact]
    public async Task A_stop_before_anything_was_written_marks_nothing()
    {
        var rig = new Rig();
        rig.Store.FailClaims = 1;
        await rig.Publisher.StartAsync(CancellationToken.None);

        await rig.Publisher.StopAsync(CancellationToken.None);

        rig.Store.Stops.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- what the payload source reads

    [Fact]
    public async Task Once_real_traffic_has_built_the_options_the_tick_attests_the_hosts_own_established_reading()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Health.NoteOpenIdConnectOptionsBuilt();
        var entra = new EntraAuthOptions { Enabled = true, Authority = "https://login.microsoftonline.com/t", ClientId = "c", ClientSecret = "s" };
        rig.Deployment.Setup(d => d.Read()).Returns(new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], true, entra, true,
            new RunningAuthenticationReading(new LocalAuthOptions { Enabled = false }, entra, RunningHandlerBinding.Bound("https://login.microsoftonline.com/t/v2.0", "c", true), EntraOptionsBuildQuality.Complete)));

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 1);

        var payload = Payload(rig.Store.Refreshes[0]);
        payload.EntraNow.Should().NotBeNull();
        payload.LocalNow.Should().BeFalse("the running options say Local is off");
        payload.LocalRestart.Should().BeTrue();
        payload.EntraNow!.Handler.State.Should().Be(RunningHandlerState.Bound);
        rig.Store.Refreshes[0].Payload.Should().NotContain("\"s\"", "the secret is reported as present, never as a value");
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_host_that_cannot_read_its_running_options_attests_them_as_not_established()
    {
        var rig = new Rig();
        await rig.Publisher.StartAsync(CancellationToken.None);
        rig.Health.NoteOpenIdConnectOptionsBuilt();
        rig.Deployment.Setup(d => d.Read()).Returns(new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Untrusted, ["invalid"], true, new EntraAuthOptions(), true, Running: null));

        rig.Time.Advance(HostConfigurationAttestationTiming.RefreshInterval);
        await WaitForAsync(() => rig.Store.Refreshes.Count == 1);

        Payload(rig.Store.Refreshes[0]).EntraNow.Should().BeNull();
        await rig.Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Local_options_that_fail_validation_are_attested_as_off_for_the_running_host()
    {
        var local = new Mock<IOptions<LocalAuthOptions>>();
        local.SetupGet(o => o.Value).Throws(new OptionsValidationException("", typeof(LocalAuthOptions), ["bad"]));
        var services = new ServiceCollection();
        services.AddSingleton(local.Object);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LocalAuth:Enabled"] = "true" }).Build();

        var payload = new HostAttestationPayloadSource(config, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new EntraOptionsBuildHealth()).Build();

        payload.LocalNow.Should().BeFalse("a smaller set of viable administrators can only make a decision refuse more");
        payload.LocalRestart.Should().BeTrue();
    }

    // ---------------------------------------------------------------- identity

    [Fact]
    public void The_host_id_is_stable_across_processes_of_one_host_and_differs_between_hosts()
    {
        var time = new ManualTimeProvider();
        IHostEnvironment Env(string root, string app = "Apperture.Management.Host") =>
            Mock.Of<IHostEnvironment>(e => e.ContentRootPath == root && e.ApplicationName == app);

        var first = HostAttestationIdentity.Create(Env(@"C:\apps\apperture"), time);
        var restarted = HostAttestationIdentity.Create(Env(@"C:\apps\apperture\"), time);
        var elsewhere = HostAttestationIdentity.Create(Env(@"C:\apps\other"), time);

        restarted.HostId.Should().Be(first.HostId, "a restart of the same host keeps its id so the new process replaces the old row");
        restarted.ProcessInstanceId.Should().NotBe(first.ProcessInstanceId, "every process is its own owner");
        elsewhere.HostId.Should().NotBe(first.HostId);
        first.HostId.Should().MatchRegex("^[0-9A-F]{32}$").And.HaveLength(32);
        first.ProcessStartedUtc.Should().Be(time.Now);
    }

    // ---------------------------------------------------------------- the build record

    [Fact]
    public void The_build_record_notes_a_handler_build_and_a_reader_that_records_nothing_never_claims_one()
    {
        var health = new EntraOptionsBuildHealth();
        health.OpenIdConnectOptionsBuilt.Should().BeFalse();
        health.NoteOpenIdConnectOptionsBuilt();
        health.OpenIdConnectOptionsBuilt.Should().BeTrue();

        IEntraOptionsBuildHealth bare = new BareHealth();
        bare.OpenIdConnectOptionsBuilt.Should().BeFalse();
        bare.NoteOpenIdConnectOptionsBuilt();
        bare.OpenIdConnectOptionsBuilt.Should().BeFalse();
    }

    private sealed class BareHealth : IEntraOptionsBuildHealth
    {
        public void Record(object options, EntraOptionsBuildQuality quality, string? reasonCode = null)
        {
        }

        public EntraOptionsBuildQuality? QualityOf(object options) => null;

        public string? ReasonOf(object options) => null;

        public void Carry(object from, object to)
        {
        }
    }

    // ---------------------------------------------------------------- source pins

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(string relativeToSrc) => File.ReadAllText(Path.Combine(RepoRoot(), "src", relativeToSrc));

    private const string Viability = "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/";

    [Theory]
    [InlineData("HostAttestationPayloadSource.cs")]
    [InlineData("HostConfigurationAttestationPublisher.cs")]
    [InlineData("HostAttestationPayload.cs")]
    [InlineData("HostConfigurationAttestationClassifier.cs")]
    [InlineData("AttestedDeploymentAuthenticationConfiguration.cs")]
    public void The_attestation_code_never_names_the_options_it_must_not_build(string file)
    {
        var text = StripComments(Source(Viability + file));

        text.Should().NotContain("IOptionsMonitor<", "the monitor builds options on first use");
        text.Should().NotContain("IOptionsSnapshot<");
        text.Should().NotContain("IOptions<EntraAuthOptions>");
        text.Should().NotMatchRegex(@"\bOpenIdConnectOptions\b", "the framework's options type is never named, so it cannot be asked for");
        text.Should().NotContain("IRunningEntraHandlerBindingSource", "reading the handler's options builds them if real traffic has not");
        text.Should().NotContain("Task.Delay").And.NotContain("Thread.Sleep");
    }

    [Fact]
    public void The_payload_source_asks_for_the_hosts_reading_only_after_the_build_record_shows_real_traffic()
    {
        var text = StripComments(Source(Viability + "HostAttestationPayloadSource.cs"));

        var guard = text.IndexOf("OpenIdConnectOptionsBuilt", StringComparison.Ordinal);
        var read = text.IndexOf(".Read()", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(-1);
        read.Should().BeGreaterThan(guard, "the host's own reading is made only past the build-record check");
    }

    [Fact]
    public void The_management_host_registers_the_attestation_last_and_the_command_line_reads_only_the_attestation()
    {
        var program = Source("Apperture.ControlPlane/Apperture.Management.Host/Program.cs");
        var attest = program.IndexOf("AddHostConfigurationAttestation()", StringComparison.Ordinal);
        attest.Should().BeGreaterThan(program.IndexOf("AddAppertureGovernance()", StringComparison.Ordinal));
        attest.Should().BeGreaterThan(program.IndexOf("AddAppertureAuthentication(", StringComparison.Ordinal));
        attest.Should().BeLessThan(program.IndexOf("builder.Build()", StringComparison.Ordinal));
        program[(attest + 1)..program.IndexOf("builder.Build()", StringComparison.Ordinal)]
            .Should().NotContain("builder.Services.Add", "no registration may follow the attestation: it starts after every other startup step");

        var governance = Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs");
        var cli = governance[governance.IndexOf("AddAppertureEmergencyOperatorServices", StringComparison.Ordinal)..];
        cli = cli[..cli.IndexOf("return services;", StringComparison.Ordinal)];
        cli.Should().Contain("AddAdministratorViability<AttestedDeploymentAuthenticationConfiguration>()");
        cli.Should().NotContain("ConfiguredDeploymentAuthenticationConfiguration");
        governance.Should().NotContain("AddHostConfigurationAttestation", "the governance composition is not the host's: only the Management Host attests");
    }

    [Fact]
    public void The_viability_evaluation_reads_the_deployment_asynchronously()
    {
        var provider = StripComments(Source(Viability + "EffectiveSignInConfigurationProvider.cs"));

        provider.Should().Contain("_deployment.ReadAsync(");
        provider.Should().NotContain("_deployment.Read()");
    }

    [Fact]
    public void The_attestation_store_stamps_every_time_and_the_binding_on_the_server()
    {
        var store = StripComments(File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.Persistence", "Apperture.Persistence.SqlServer", "SqlHostConfigurationAttestationStore.cs")));

        store.Should().Contain("SYSUTCDATETIME()");
        store.Should().NotContain("DateTime.UtcNow").And.NotContain("TimeProvider").And.NotContain("DateTimeOffset.UtcNow");
        store.Should().Contain("HASHBYTES('SHA2_256'");
        // A refresh and a stop are conditional on owning the row.
        store.Should().Contain("[ProcessInstanceId] = @process AND [StoppedUtc] IS NULL");
        // A refresh re-stamps the binding with the time; taking a row back is one statement that decides against the database's clock
        // and the shared window, and never names a time or a window the host chose.
        store.Should().Contain("[RefreshedUtc] = SYSUTCDATETIME(), [SchemaVersion] = @schemaVersion, [DatabaseFingerprint] = @fingerprint");
        store.Should().Contain("[ProcessInstanceId] <> @process AND ([StoppedUtc] IS NOT NULL OR [RefreshedUtc] < @staleBefore)");
        store.Should().Contain("HostConfigurationAttestationLiveness.FreshnessWindow");
    }

    private static string StripComments(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", "");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not become true.");
            await Task.Delay(5);
        }
    }
}
