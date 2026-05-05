using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ManagedContactManagementServiceEntraImportTests
{
    private const string RegId = "entra_reg_1";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static ManagedContactManagementService CreateSut(
        out Mock<IEntraDirectoryDiscoveryService> directory,
        out Mock<IManagedContactRepository> contacts,
        IConfiguration? configuration = null)
    {
        var actor = new Mock<IActor>();
        actor.SetupGet(x => x.Id).Returns(new ActorId("actor1"));
        actor.SetupGet(x => x.Kind).Returns(ActorKind.Human);
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);

        var auth = new Mock<IAuthorizationService>();
        auth.Setup(x => x.EvaluateAsync(
                It.IsAny<IActor>(),
                It.IsAny<PermissionDescriptor>(),
                It.IsAny<ResourceDescriptor?>(),
                It.IsAny<IGovernedResourceIdentity?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var registrations = new Mock<IEntraInstanceRegistrationRepository>();
        registrations.Setup(x => x.GetByIdAsync(RegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new EntraInstanceRegistration
                {
                    Id = RegId,
                    ClientId = "app",
                    AzureTenantId = "tenant-1",
                    CreatedUtc = Now,
                    UpdatedUtc = Now,
                });

        directory = new Mock<IEntraDirectoryDiscoveryService>();
        contacts = new Mock<IManagedContactRepository>();
        var groups = new Mock<IManagedContactGroupRepository>();
        var users = new Mock<IUserRepository>();
        var time = new FixedTime(Now);
        var cfg = configuration ?? new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new ManagedContactManagementService(
            principal.Object,
            auth.Object,
            contacts.Object,
            groups.Object,
            registrations.Object,
            directory.Object,
            users.Object,
            cfg,
            time);
    }

    private static EntraDirectoryDiscoveryContext AnyCtx() =>
        It.IsAny<EntraDirectoryDiscoveryContext>();

    private static ManagedContactIdentityLinkRepositoryRow DummyLink(string managedContactId, string oid) =>
        new(
            "lnk_1",
            managedContactId,
            ManagedContactIdentityProviders.Entra,
            oid,
            "upn@test.com",
            Now,
            ManagedContactIdentityLinkStatuses.Active,
            Now,
            Now);

    private static UpsertEntraManagedContactResult UpsertResult(string contactId, bool created, string oid) =>
        new(contactId, created, DummyLink(contactId, oid));

    [Fact]
    public async Task Blank_object_id_sets_invalid_object_id_outcome()
    {
        var sut = CreateSut(out _, out _);
        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "   " });
        r.Items.Should().ContainSingle();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.InvalidObjectId);
        r.Items[0].ErrorMessage.Should().Be("Empty object id.");
    }

    [Fact]
    public async Task Import_entra_user_success_sets_success_outcome_code()
    {
        var sut = CreateSut(out var directory, out var contacts);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    true,
                    DiscoveryFailureCategory.None,
                    "ok",
                    null,
                    new EntraDirectoryUser("oid-new", "N", "n@upn.com", "n@mail.com")));

        contacts.Setup(x => x.FindIdentityLinkByEntraObjectIdAsync("oid-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedContactIdentityLinkRepositoryRow?)null);
        contacts.Setup(x => x.FindByEmailAsync("n@mail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedContactRepositoryRow?)null);
        contacts.Setup(x => x.UpsertFromEntraDirectoryUserAsync(
                "N",
                "n@mail.com",
                "n@upn.com",
                "oid-new",
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UpsertResult("mcontact_new", true, "oid-new"));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-new" });
        r.Items.Should().ContainSingle();
        r.Items[0].Success.Should().BeTrue();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.Success);
        r.Items[0].ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Repeat_import_sets_already_linked_outcome_code()
    {
        var sut = CreateSut(out var directory, out var contacts);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-same", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    true,
                    DiscoveryFailureCategory.None,
                    "ok",
                    null,
                    new EntraDirectoryUser("oid-same", "S", "s@upn", "s@mail.com")));

        var link = DummyLink("mcontact_exist", "oid-same");
        contacts.Setup(x => x.FindIdentityLinkByEntraObjectIdAsync("oid-same", It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        contacts.Setup(x => x.FindByEmailAsync("s@mail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ManagedContactRepositoryRow(
                    "mcontact_exist",
                    "S",
                    "s@mail.com",
                    "Entra",
                    true,
                    null,
                    Now,
                    Now));
        contacts.Setup(x => x.UpsertFromEntraDirectoryUserAsync(
                "S",
                "s@mail.com",
                "s@upn",
                "oid-same",
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UpsertResult("mcontact_exist", false, "oid-same"));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-same" });
        r.Items[0].Success.Should().BeTrue();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.AlreadyLinked);
    }

    [Fact]
    public async Task Email_conflict_sets_conflict_outcome_and_keeps_message()
    {
        var sut = CreateSut(out var directory, out var contacts);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-c", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    true,
                    DiscoveryFailureCategory.None,
                    "ok",
                    null,
                    new EntraDirectoryUser("oid-c", "C", "c@upn", "dup@mail.com")));

        contacts.Setup(x => x.FindIdentityLinkByEntraObjectIdAsync("oid-c", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedContactIdentityLinkRepositoryRow?)null);
        contacts.Setup(x => x.FindByEmailAsync("dup@mail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ManagedContactRepositoryRow(
                    "mcontact_other",
                    "Other",
                    "dup@mail.com",
                    "Custom",
                    true,
                    null,
                    Now,
                    Now));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-c" });
        r.Items[0].Success.Should().BeFalse();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.EmailConflictExistingContact);
        r.Items[0].ErrorMessage.Should().Contain("mcontact_other");
    }

    [Fact]
    public async Task Directory_failure_sets_directory_error_outcome()
    {
        var sut = CreateSut(out var directory, out _);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    false,
                    DiscoveryFailureCategory.Configuration,
                    "Graph misconfigured.",
                    null,
                    null));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-fail" });
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.DirectoryError);
        r.Items[0].ErrorMessage.Should().Be("Graph misconfigured.");
    }

    [Fact]
    public async Task User_not_found_sets_stable_outcome_code()
    {
        var sut = CreateSut(out var directory, out _);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    true,
                    DiscoveryFailureCategory.None,
                    "User not found.",
                    null,
                    null));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-missing" });
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.UserNotFoundInDirectory);
        r.Items[0].ErrorMessage.Should().Contain("directory");
    }

    [Fact]
    public async Task Upsert_exception_sets_import_failed_outcome()
    {
        var sut = CreateSut(out var directory, out var contacts);
        directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                    true,
                    DiscoveryFailureCategory.None,
                    "ok",
                    null,
                    new EntraDirectoryUser("oid-x", "X", "x@upn", "x@mail.com")));

        contacts.Setup(x => x.FindIdentityLinkByEntraObjectIdAsync("oid-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedContactIdentityLinkRepositoryRow?)null);
        contacts.Setup(x => x.FindByEmailAsync("x@mail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedContactRepositoryRow?)null);
        contacts.Setup(x => x.UpsertFromEntraDirectoryUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database boom"));

        var r = await sut.ImportEntraUsersAsync(RegId, new[] { "oid-x" });
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.ImportFailed);
        r.Items[0].ErrorMessage.Should().Be("Database boom");
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
