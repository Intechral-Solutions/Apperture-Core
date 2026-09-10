using Apperture.ControlPlane.Security;
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
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.StepUpSatisfied);
        foreach (var details in harness.Audit.Details)
        {
            var text = details ?? string.Empty;
            text.Contains("eyJ", StringComparison.Ordinal).Should().BeFalse();
            text.Contains("cookie", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            text.Contains("client_secret", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
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
        new(new UserId(id), "Operator", [link], [], false);

    private static IdentityLink EntraLink(string oid, bool proven) =>
        new(IdentitySource.Entra, oid, proven ? Now : null, ActiveTenantId, "reg-1", Now);

    private sealed class Harness
    {
        public Harness()
        {
            Users = new RecordingUsers();
            Audit = new RecordingAudit();
            Service = new EntraStepUpService(
                Users,
                new StubRegistrations(),
                Audit,
                TimeProvider.System,
                NullLogger<EntraStepUpService>.Instance);
        }

        public RecordingUsers Users { get; }
        public RecordingAudit Audit { get; }
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

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public List<string?> Details { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            Details.Add(record.Details);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }
}
