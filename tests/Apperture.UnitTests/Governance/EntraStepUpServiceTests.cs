using Apperture.ControlPlane.Security;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EntraStepUpServiceTests
{
    private const string ActiveTenantId = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenantId = "22222222-2222-2222-2222-222222222222";
    private const string Oid = "33333333-3333-3333-3333-333333333333";
    private const string OtherOid = "44444444-4444-4444-4444-444444444444";
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Same_oid_tid_and_requested_context_succeeds()
    {
        var harness = new Harness();
        harness.Users.Seed(User("usr-1", EntraLink(Oid, proven: true)));

        var outcome = await harness.Service.CompleteAsync(
            new UserId("usr-1"),
            new EntraIdentityAssertion(Oid, ActiveTenantId),
            "c1",
            ["c1"],
            developmentSimulated: false);

        outcome.Succeeded.Should().BeTrue();
        var (satisfied, actor) = harness.Audit.Events.Should().ContainSingle().Subject;
        satisfied.Action.Should().Be(StrongAuthenticationAuditActionNames.StepUpSatisfied);
        satisfied.Definition.WriteClass.Should().Be(Apperture.Core.Application.Audit.AuditWriteClass.RequiredSecurityEvent);
        actor.Should().Be(Apperture.Core.Application.Audit.AuditActor.AuthenticatedUser(new UserId("usr-1")), "the initiating user from the protected state");
        harness.Audit.Writer.Written.Should().ContainSingle(w => !w.BestEffort, "a W2 row is written and committed, not attempted");
        satisfied.Fields.Values.Select(v => v?.ToString() ?? "").Should().OnlyContain(text =>
            !text.Contains("eyJ", StringComparison.Ordinal)
            && !text.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("client_secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Different_oid_fails()
    {
        var harness = new Harness();
        harness.Users.Seed(User("usr-1", EntraLink(Oid, proven: true)));

        var outcome = await harness.Service.CompleteAsync(
            new UserId("usr-1"),
            new EntraIdentityAssertion(OtherOid, ActiveTenantId),
            "c1",
            ["c1"],
            false);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraStepUpFailureCodes.IdentityMismatch);
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.StepUpIdentityMismatch);
    }

    [Fact]
    public async Task Wrong_tid_fails()
    {
        var harness = new Harness();
        harness.Users.Seed(User("usr-1", EntraLink(Oid, proven: true)));

        var outcome = await harness.Service.CompleteAsync(
            new UserId("usr-1"),
            new EntraIdentityAssertion(Oid, OtherTenantId),
            "c1",
            ["c1"],
            false);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraStepUpFailureCodes.TenantMismatch);
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.StepUpTenantMismatch);
    }

    [Fact]
    public async Task Missing_authentication_context_fails()
    {
        var harness = new Harness();
        harness.Users.Seed(User("usr-1", EntraLink(Oid, proven: true)));

        var outcome = await harness.Service.CompleteAsync(
            new UserId("usr-1"),
            new EntraIdentityAssertion(Oid, ActiveTenantId),
            "c1",
            [],
            false);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraStepUpFailureCodes.AuthenticationContextNotSatisfied);
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.StepUpContextNotSatisfied);
    }

    [Fact]
    public async Task Unproven_link_cannot_complete_step_up()
    {
        var harness = new Harness();
        harness.Users.Seed(User("usr-1", EntraLink(Oid, proven: false)));

        var outcome = await harness.Service.CompleteAsync(
            new UserId("usr-1"),
            new EntraIdentityAssertion(Oid, ActiveTenantId),
            "c1",
            ["c1"],
            false);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureCode.Should().Be(EntraStepUpFailureCodes.Unproven);
    }

    private static User User(string id, IdentityLink link) =>
        new(new UserId(id), "Operator", [link], [], false) { Username = id };

    private static IdentityLink EntraLink(string oid, bool proven) =>
        new(IdentitySource.Entra, oid, proven ? Now : null, ActiveTenantId, "reg-1", Now);

    private sealed class Harness
    {
        public Harness()
        {
            Users = new RecordingUsers();
            Audit = new AuditRecording();
            Service = new EntraStepUpService(
                Users,
                new StubRegistrations(),
                NullLogger<EntraStepUpService>.Instance,
                Audit);
        }

        public RecordingUsers Users { get; }
        internal AuditRecording Audit { get; }
        public EntraStepUpService Service { get; }
    }

    private sealed class StubRegistrations : IEntraInstanceRegistrationRepository
    {
        public Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<EntraInstanceRegistration?>(new EntraInstanceRegistration
            {
                Id = "reg-1",
                Authority = $"https://login.microsoftonline.com/{ActiveTenantId}",
                ClientId = "client",
                AzureTenantId = ActiveTenantId,
                Enabled = true,
                IsActive = true,
                AuthenticationContextId = "c1"
            });

        public Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => GetActiveAsync(cancellationToken);
        public Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntraInstanceRegistration>>([]);
        public Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default) => Task.FromResult(registration);
        public Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetActiveAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateOpenIdValidationOutcomeAsync(string id, OpenIdConfigurationStatus status, DateTimeOffset validatedUtc, string? summary, string? detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateDirectoryDiscoveryOutcomeAsync(string id, DirectoryDiscoveryStatus status, DateTimeOffset utc, string? summary, string? detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

        public void Seed(params User[] users)
        {
            foreach (var user in users)
                _users[user.Id.Value] = user;
        }

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u =>
                u.IdentityLinks.Any(l => l.Source == source && string.Equals(l.ExternalId, externalId, StringComparison.OrdinalIgnoreCase))));
        public ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(identity.Matches)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompleteEntraLinkProofAsync(string tenantId, string objectId, DateTimeOffset provenAt, string? registrationIdWhenMissing, string? providerSubject, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

}
