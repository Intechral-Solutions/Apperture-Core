using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EntraProvisioningDirectoryServiceTests
{
    private const string Oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string OtherOid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";
    private const string Tid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task Unauthorized_caller_cannot_search()
    {
        var harness = Harness.Create(allowUserManage: false);
        var act = () => harness.Service.SearchAsync("reg-1", "jane", null, 25).AsTask();
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Discovery.SearchCalls.Should().Be(0);
    }

    [Fact]
    public async Task Results_identify_provisioned_and_pending_identities_by_oid_not_email()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("u-1"),
            "Existing",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, Tid, "reg-1")],
            [],
            IsBreakGlassEligible: false));
        harness.Requests.Seed(new EntraAccessRequest(
            "req-1", "reg-1", Tid, OtherOid, "Pat", "pat@contoso.com", "pat@contoso.com", "Member",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, EntraAccessRequestStatus.Pending));
        harness.Discovery.Users =
        [
            new EntraDirectoryUser(Oid, "Jane", "jane@contoso.com", "shared@contoso.com", "Member", true),
            new EntraDirectoryUser(OtherOid, "Pat", "pat@contoso.com", "shared@contoso.com", "Member", true)
        ];

        var page = await harness.Service.SearchAsync("reg-1", "shared", null, 25);

        page.Success.Should().BeTrue();
        page.Items.Should().ContainSingle(u => u.ObjectId == Oid && u.AppertureStatus == EntraDirectoryAppertureStatus.Provisioned);
        page.Items.Should().ContainSingle(u => u.ObjectId == OtherOid && u.AppertureStatus == EntraDirectoryAppertureStatus.AccessRequestPending);
    }

    [Fact]
    public async Task Provisioned_status_ignores_the_same_oid_in_another_tenant()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("u-other-tenant"),
            "Other tenant",
            [new IdentityLink(IdentitySource.Entra, Oid, DateTimeOffset.UtcNow, "99999999-8888-7777-6666-555555555555", "reg-other")],
            [],
            IsBreakGlassEligible: false));
        harness.Discovery.Users =
        [
            new EntraDirectoryUser(Oid, "Jane", "jane@contoso.com", "jane@contoso.com", "Member", true)
        ];

        var page = await harness.Service.SearchAsync("reg-1", "jane", null, 25);

        page.Success.Should().BeTrue();
        page.Items.Should().ContainSingle(u => u.ObjectId == Oid && u.AppertureStatus == EntraDirectoryAppertureStatus.Available);
    }

    [Fact]
    public async Task Client_supplied_graph_url_is_not_fetched()
    {
        var harness = Harness.Create();
        var page = await harness.Service.SearchAsync(
            "reg-1",
            "jane",
            "https://graph.microsoft.com/v1.0/users?$skiptoken=abc",
            25);

        page.Success.Should().BeFalse();
        page.Summary.Should().Contain("no longer valid");
        harness.Discovery.SearchCalls.Should().Be(0);
    }

    [Fact]
    public void Continuation_token_is_opaque_and_registration_scoped()
    {
        var token = EntraDirectoryContinuationCodec.Encode("reg-1", "jane", "skip-1", 25);
        EntraDirectoryContinuationCodec.TryDecode(token, "reg-1", out var search, out var skip, out var top).Should().BeTrue();
        search.Should().Be("jane");
        skip.Should().Be("skip-1");
        top.Should().Be(25);
        EntraDirectoryContinuationCodec.TryDecode(token, "other-reg", out _, out _, out _).Should().BeFalse();
        EntraDirectoryContinuationCodec.TryDecode("https://evil.example/graph", "reg-1", out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Graph_failure_does_not_fall_back_to_email_matching()
    {
        var harness = Harness.Create();
        harness.Discovery.Fail = true;
        harness.Users.Seed(new User(
            new UserId("u-1"),
            "Jane",
            [new IdentityLink(IdentitySource.Local, "jane@contoso.com", DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: false));

        var page = await harness.Service.SearchAsync("reg-1", "jane@contoso.com", null, 25);

        page.Success.Should().BeFalse();
        page.Items.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingRequests Requests { get; } = new();
        public StubDiscovery Discovery { get; } = new();
        public EntraProvisioningDirectoryService Service { get; }

        private Harness(bool allowUserManage)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entra:ClientSecret"] = "secret"
            }).Build();
            Service = new EntraProvisioningDirectoryService(
                new StubPrincipal(),
                new SelectiveAuth(allowUserManage),
                new StubRegistrations(),
                Discovery,
                Users,
                Requests,
                config);
        }

        public static Harness Create(bool allowUserManage = true) => new(allowUserManage);
    }

    private sealed class StubDiscovery : IEntraDirectoryDiscoveryService
    {
        public IReadOnlyList<EntraDirectoryUser> Users { get; set; } = [];
        public bool Fail { get; set; }
        public int SearchCalls { get; private set; }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            SearchUsersAsync(context, search, top, null, cancellationToken);

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, string? skipToken, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Fail
                ? Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(false, DiscoveryFailureCategory.Transient, "graph down", null, []))
                : Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(true, DiscoveryFailureCategory.None, "ok", null, Users));
        }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(true, DiscoveryFailureCategory.None, "ok", null, []));

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(true, DiscoveryFailureCategory.None, "ok", null, Users.FirstOrDefault(u => u.ObjectId == objectId)));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(true, DiscoveryFailureCategory.None, "ok", null, null));
    }

    private sealed class StubRegistrations : IEntraInstanceRegistrationRepository
    {
        private readonly EntraInstanceRegistration _registration = new()
        {
            Id = "reg-1",
            DisplayName = "Contoso",
            Authority = "https://login.microsoftonline.com/" + Tid,
            ClientId = "client",
            Enabled = true,
            AzureTenantId = Tid,
            UsesDeploymentSecret = true
        };

        public Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<EntraInstanceRegistration?>(_registration);
        public Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<EntraInstanceRegistration?>(id == "reg-1" ? _registration : null);
        public Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntraInstanceRegistration>>([_registration]);
        public Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetActiveAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateOpenIdValidationOutcomeAsync(string id, OpenIdConfigurationStatus status, DateTimeOffset validatedUtc, string? summary, string? detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateDirectoryDiscoveryOutcomeAsync(string id, DirectoryDiscoveryStatus status, DateTimeOffset utc, string? summary, string? detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingRequests : IEntraAccessRequestRepository
    {
        private readonly List<EntraAccessRequest> _items = [];
        public void Seed(EntraAccessRequest request) => _items.Add(request);
        public ValueTask<EntraAccessRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.FirstOrDefault(r => r.Id == id));
        public ValueTask<EntraAccessRequest?> FindPendingAsync(string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.FirstOrDefault(r => r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));
        public ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(string tenantId, IReadOnlyList<string> objectIds, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(_items.Where(r =>
                r.Status == EntraAccessRequestStatus.Pending
                && string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && objectIds.Contains(r.ProviderObjectId, StringComparer.OrdinalIgnoreCase)).ToList());
        public ValueTask<EntraAccessRequest?> FindLatestAsync(string tenantId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.LastOrDefault(r =>
                string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ProviderObjectId, objectId, StringComparison.OrdinalIgnoreCase)));
        public ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EntraAccessRequest>>(_items.Where(r => r.Status == EntraAccessRequestStatus.Pending).ToList());
        public ValueTask<int> CountPendingAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.Count(r => r.Status == EntraAccessRequestStatus.Pending));
        public ValueTask<int> CountRecentForIdentityAsync(string tenantId, string objectId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask AddAsync(EntraAccessRequest request, CancellationToken cancellationToken = default) { _items.Add(request); return ValueTask.CompletedTask; }
        public ValueTask UpdateAsync(EntraAccessRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public void Seed(User user) => _users[user.Id.Value] = user;
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            GetByIdentityLinkAsync(ProviderIdentityKey.FromSourceAndExternalId(source, externalId), cancellationToken);
        public ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(identity.Matches)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) { _users[user.Id.Value] = user; return ValueTask.CompletedTask; }
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) { _users[user.Id.Value] = user; return ValueTask.CompletedTask; }
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class SelectiveAuth : IAuthorizationService
    {
        private readonly bool _userManage;
        public SelectiveAuth(bool userManage) => _userManage = userManage;
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permission.Action != ActionConstants.UserManage || _userManage);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("admin"), "admin"));
    }
}
