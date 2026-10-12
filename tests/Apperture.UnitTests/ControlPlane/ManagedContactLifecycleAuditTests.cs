using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// AUD-1.4 slice 6: the managed contact writers (custom contacts, enable, link, Entra import) as audited units over the real
/// repositories on EF InMemory with a recording unit (M7 at service level; the transactional guarantees and the races are proven
/// against SQL Server in <c>ManagedContactSqlServerTests</c>). A contact is a third party: its address, name and directory
/// identity never reach an audit row. This suite replaces the earlier import tests, which mocked the repository.
/// </summary>
public sealed class ManagedContactLifecycleAuditTests : IDisposable
{
    private const string RegId = "entra_reg_1";
    private const string AddressSentinel = "third.party@example.com";
    private const string NameSentinel = "Third Party Person";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly AppertureDbContext _db;
    private readonly RecordingAuditedUnitOfWork _unit = new();
    private readonly Mock<IEntraDirectoryDiscoveryService> _directory = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly SqlManagedContactRepository _contacts;
    private readonly SqlManagedContactGroupRepository _groups;
    private readonly ManagedContactManagementService _sut;

    public ManagedContactLifecycleAuditTests()
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        _contacts = new SqlManagedContactRepository(_db);
        _groups = new SqlManagedContactGroupRepository(_db);
        _users.Setup(u => u.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserId id, CancellationToken _) => new User(id, "Someone", [], [], false) { Username = "someone" });
        _sut = NewService(_contacts, _groups, _unit);
    }

    public void Dispose() => _db.Dispose();

    private ManagedContactManagementService NewService(IManagedContactRepository contacts, IManagedContactGroupRepository groups, IAuditedUnitOfWork? unit)
    {
        var actor = new Mock<IActor>();
        actor.SetupGet(x => x.Id).Returns(new ActorId("actor1"));
        actor.SetupGet(x => x.Kind).Returns(ActorKind.Human);
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
        var auth = new Mock<IAuthorizationService>();
        auth.Setup(x => x.EvaluateAsync(
                It.IsAny<IActor>(), It.IsAny<PermissionDescriptor>(), It.IsAny<ResourceDescriptor?>(),
                It.IsAny<IGovernedResourceIdentity?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var registrations = new Mock<IEntraInstanceRegistrationRepository>();
        registrations.Setup(x => x.GetByIdAsync(RegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraInstanceRegistration { Id = RegId, ClientId = "app", AzureTenantId = "tenant-1", CreatedUtc = Now, UpdatedUtc = Now });
        return new ManagedContactManagementService(
            principal.Object, auth.Object, contacts, groups, registrations.Object, _directory.Object, _users.Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build(), new FixedTime(Now), secretResolver: null, auditedUnitOfWork: unit);
    }

    private static string Everything(RecordingAuditedUnitOfWork unit) =>
        JsonSerializer.Serialize(unit.Staged.Select(e => new { e.Action, e.Target, e.OutcomeCode, e.Fields, e.Related }));

    private static EntraDirectoryDiscoveryContext AnyCtx() => It.IsAny<EntraDirectoryDiscoveryContext>();

    private void DirectoryReturns(string oid, string name, string? upn, string? mail) =>
        _directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                true, DiscoveryFailureCategory.None, "ok", null, new EntraDirectoryUser(oid, name, upn, mail)));

    // Custom contacts ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_creation_stages_one_created_row_that_says_whether_there_is_an_address_and_never_what_it_is()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand(NameSentinel, AddressSentinel, true));

        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactCreated);
        var evt = _unit.Single(AuditActionNames.ManagedContactCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.ManagedContact, created.Id));
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["source"] = "custom", ["via"] = "admin", ["enabled"] = true, ["hasEmail"] = true,
        });
        Everything(_unit).Should().NotContain(AddressSentinel).And.NotContain(NameSentinel);
    }

    [Fact]
    public async Task A_contact_without_an_address_is_recorded_as_having_none()
    {
        await _sut.CreateCustomAsync(new ManagedContactCreateCommand(NameSentinel, null, false));

        var evt = _unit.Single(AuditActionNames.ManagedContactCreated);
        evt.Fields["hasEmail"].Should().Be(false);
        evt.Fields["enabled"].Should().Be(false);
    }

    [Fact]
    public async Task A_creation_that_reuses_an_address_or_has_no_name_is_refused_and_stages_nothing()
    {
        await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();

        var duplicate = () => _sut.CreateCustomAsync(new ManagedContactCreateCommand("Second", AddressSentinel.ToUpperInvariant(), true)).AsTask();
        var nameless = () => _sut.CreateCustomAsync(new ManagedContactCreateCommand(" ", "other@example.com", true)).AsTask();

        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already uses this email*");
        await nameless.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Display name is required*");
        _unit.Actions.Should().BeEmpty();
        (await _db.ManagedContacts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_update_names_the_changed_fields_and_never_their_values()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();

        await _sut.UpdateCustomAsync(created.Id, new ManagedContactUpdateCommand(NameSentinel, "new.address@example.com", false));

        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactUpdated);
        var evt = _unit.Single(AuditActionNames.ManagedContactUpdated);
        evt.Fields["changedFields"].Should().BeEquivalentTo(new[] { "displayName", "email", "enabled" });
        evt.Fields["enabled"].Should().Be(false);
        evt.Fields["source"].Should().Be("custom");
        Everything(_unit).Should().NotContain(AddressSentinel).And.NotContain("new.address").And.NotContain(NameSentinel);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_declares_a_no_op_and_leaves_the_row_alone()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();
        var before = (await _db.ManagedContacts.AsNoTracking().SingleAsync()).UpdatedAtUtc;

        await _sut.UpdateCustomAsync(created.Id, new ManagedContactUpdateCommand(" First ", $" {AddressSentinel} ", true));

        _unit.Actions.Should().BeEmpty();
        _unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        (await _db.ManagedContacts.AsNoTracking().SingleAsync()).UpdatedAtUtc.Should().Be(before);
    }

    [Fact]
    public async Task An_update_is_refused_for_an_unknown_or_an_entra_contact_and_for_another_contacts_address()
    {
        var first = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", "first@example.com", true));
        var second = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("Second", "second@example.com", true));
        DirectoryReturns("oid-1", "Entra One", "one@upn", "one@mail.com");
        var imported = (await _sut.ImportEntraUsersAsync(RegId, ["oid-1"])).Items[0].ManagedContactId!;
        _unit.Operations.Clear();

        var unknown = () => _sut.UpdateCustomAsync("mcontact_missing", new ManagedContactUpdateCommand("x", null, true)).AsTask();
        var entra = () => _sut.UpdateCustomAsync(imported, new ManagedContactUpdateCommand("x", null, true)).AsTask();
        var taken = () => _sut.UpdateCustomAsync(second.Id, new ManagedContactUpdateCommand("Second", "first@example.com", true)).AsTask();

        await unknown.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        await entra.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only custom contacts*");
        await taken.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already uses this email*");
        _unit.Actions.Should().BeEmpty();
        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public async Task Enabling_and_disabling_name_the_enabled_flag_and_setting_the_current_state_records_nothing()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();

        await _sut.SetEnabledAsync(created.Id, false);
        await _sut.SetEnabledAsync(created.Id, false);
        await _sut.SetEnabledAsync(created.Id, true);

        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactUpdated, AuditActionNames.ManagedContactUpdated);
        _unit.Staged.Select(e => e.Fields["enabled"]).Should().Equal(false, true);
        _unit.Staged.Should().OnlyContain(e => ((IEnumerable<string>)e.Fields["changedFields"]!).SequenceEqual(new[] { "enabled" }));
        _unit.Operations.Count(o => o.NoOp).Should().Be(1);
        (await _sut.SetEnabledAsync("mcontact_missing", true)).Should().BeNull();
    }

    [Fact]
    public async Task Linking_and_unlinking_name_the_user_by_id_and_repeating_them_records_nothing()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();

        await _sut.LinkToUserAsync(created.Id, "usr_a");
        await _sut.LinkToUserAsync(created.Id, "usr_a");
        await _sut.LinkToUserAsync(created.Id, "usr_b");
        await _sut.UnlinkUserAsync(created.Id);
        await _sut.UnlinkUserAsync(created.Id);

        _unit.Actions.Should().Equal(
            AuditActionNames.ManagedContactUserLinked, AuditActionNames.ManagedContactUserLinked, AuditActionNames.ManagedContactUserUnlinked);
        var linked = _unit.Staged.Where(e => e.Action == AuditActionNames.ManagedContactUserLinked).ToList();
        linked[0].Fields["userId"].Should().Be("usr_a");
        linked[0].Fields.Should().NotContainKey("previousUserId");
        linked[1].Fields["previousUserId"].Should().Be("usr_a");
        _unit.Single(AuditActionNames.ManagedContactUserUnlinked).Fields["previousUserId"].Should().Be("usr_b");
        _unit.Operations.Count(o => o.NoOp).Should().Be(2);
    }

    [Fact]
    public async Task Linking_to_an_unknown_user_or_contact_is_refused_or_absent_and_stages_nothing()
    {
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        _unit.Operations.Clear();
        _users.Setup(u => u.GetByIdAsync(new UserId("usr_gone"), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var act = () => _sut.LinkToUserAsync(created.Id, "usr_gone").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Platform user was not found*");
        (await _sut.LinkToUserAsync("mcontact_missing", "usr_a")).Should().BeNull();
        (await _sut.UnlinkUserAsync("mcontact_missing")).Should().BeNull();
        _unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_result_and_an_unknown_outcome_reaches_the_caller()
    {
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        var created = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true));
        created.EmailAddress.Should().Be(AddressSentinel);

        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;
        var act = () => _sut.CreateCustomAsync(new ManagedContactCreateCommand("Second", "second@example.com", true)).AsTask();
        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task The_service_refuses_to_run_without_the_audited_unit()
    {
        var sut = NewService(_contacts, _groups, unit: null);

        var act = () => sut.CreateCustomAsync(new ManagedContactCreateCommand("First", AddressSentinel, true)).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
    }

    // Entra import ------------------------------------------------------------------------------------

    [Fact]
    public async Task Blank_object_id_sets_invalid_object_id_outcome()
    {
        var r = await _sut.ImportEntraUsersAsync(RegId, ["   "]);

        r.Items.Should().ContainSingle();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.InvalidObjectId);
        r.Items[0].ErrorMessage.Should().Be("Empty object id.");
        _unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Importing_a_new_directory_user_creates_a_contact_and_records_it_as_an_import()
    {
        DirectoryReturns("oid-new", NameSentinel, "n@upn.com", AddressSentinel);

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-new"]);

        r.Items[0].Success.Should().BeTrue();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.Success);
        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactCreated);
        var evt = _unit.Single(AuditActionNames.ManagedContactCreated);
        evt.Target.Id.Should().Be(r.Items[0].ManagedContactId);
        evt.Fields["via"].Should().Be("entra_import");
        evt.Fields["source"].Should().Be("entra");
        Everything(_unit).Should().NotContain(AddressSentinel).And.NotContain(NameSentinel).And.NotContain("oid-new").And.NotContain("n@upn.com");
    }

    [Fact]
    public async Task Repeating_an_import_that_changes_nothing_is_already_linked_records_nothing_and_still_refreshes_the_sync_time()
    {
        DirectoryReturns("oid-same", "S", "s@upn", "s@mail.com");
        await _sut.ImportEntraUsersAsync(RegId, ["oid-same"]);
        _unit.Operations.Clear();
        var linkBefore = (await _db.ManagedContactIdentityLinks.AsNoTracking().SingleAsync()).LastSyncedAtUtc;
        var later = new FixedTime(Now.AddHours(2));
        var sut = NewServiceAt(later);

        var r = await sut.ImportEntraUsersAsync(RegId, ["oid-same"]);

        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.AlreadyLinked);
        _unit.Actions.Should().BeEmpty();
        _unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        (await _db.ManagedContactIdentityLinks.AsNoTracking().SingleAsync()).LastSyncedAtUtc.Should().BeAfter(linkBefore!.Value);
    }

    private ManagedContactManagementService NewServiceAt(TimeProvider time)
    {
        var actor = new Mock<IActor>();
        actor.SetupGet(x => x.Id).Returns(new ActorId("actor1"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
        var auth = new Mock<IAuthorizationService>();
        auth.Setup(x => x.EvaluateAsync(
                It.IsAny<IActor>(), It.IsAny<PermissionDescriptor>(), It.IsAny<ResourceDescriptor?>(),
                It.IsAny<IGovernedResourceIdentity?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var registrations = new Mock<IEntraInstanceRegistrationRepository>();
        registrations.Setup(x => x.GetByIdAsync(RegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraInstanceRegistration { Id = RegId, ClientId = "app", AzureTenantId = "tenant-1", CreatedUtc = Now, UpdatedUtc = Now });
        return new ManagedContactManagementService(
            principal.Object, auth.Object, _contacts, _groups, registrations.Object, _directory.Object, _users.Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build(), time, secretResolver: null, auditedUnitOfWork: _unit);
    }

    [Fact]
    public async Task Re_importing_a_user_whose_directory_data_changed_records_which_parts_changed()
    {
        DirectoryReturns("oid-1", "Old Name", "old@upn", "old@mail.com");
        await _sut.ImportEntraUsersAsync(RegId, ["oid-1"]);
        _unit.Operations.Clear();
        DirectoryReturns("oid-1", NameSentinel, "new@upn", "new@mail.com");

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-1"]);

        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.AlreadyLinked);
        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactUpdated);
        var evt = _unit.Single(AuditActionNames.ManagedContactUpdated);
        evt.Fields["changedFields"].Should().BeEquivalentTo(new[] { "displayName", "email", "entraSync" });
        evt.Fields["source"].Should().Be("entra");
        Everything(_unit).Should().NotContain("new@mail.com").And.NotContain(NameSentinel).And.NotContain("new@upn");
    }

    [Fact]
    public async Task A_disabled_imported_contact_stays_disabled_across_a_re_import()
    {
        DirectoryReturns("oid-1", "Name", "u@upn", "u@mail.com");
        var contactId = (await _sut.ImportEntraUsersAsync(RegId, ["oid-1"])).Items[0].ManagedContactId!;
        await _sut.SetEnabledAsync(contactId, false);
        DirectoryReturns("oid-1", "Renamed", "u@upn", "u@mail.com");

        await _sut.ImportEntraUsersAsync(RegId, ["oid-1"]);

        (await _db.ManagedContacts.AsNoTracking().SingleAsync()).IsEnabled.Should().BeFalse("a manual disable survives a sync");
        _unit.Staged.Last().Fields["enabled"].Should().Be(false);
    }

    [Fact]
    public async Task Email_conflict_with_an_unlinked_contact_sets_conflict_outcome_keeps_the_message_and_records_nothing()
    {
        var other = await _sut.CreateCustomAsync(new ManagedContactCreateCommand("Other", "dup@mail.com", true));
        _unit.Operations.Clear();
        DirectoryReturns("oid-c", "C", "c@upn", "dup@mail.com");

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-c"]);

        r.Items[0].Success.Should().BeFalse();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.EmailConflictExistingContact);
        r.Items[0].ErrorMessage.Should().Contain(other.Id);
        _unit.Actions.Should().BeEmpty();
        (await _db.ManagedContacts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Directory_failure_and_user_not_found_are_reported_without_a_unit()
    {
        _directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryDiscoveryResult<EntraDirectoryUser?>(false, DiscoveryFailureCategory.Configuration, "Graph misconfigured.", null, null));
        _directory.Setup(x => x.GetUserByIdAsync(AnyCtx(), "oid-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryDiscoveryResult<EntraDirectoryUser?>(true, DiscoveryFailureCategory.None, "User not found.", null, null));

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-fail", "oid-missing"]);

        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.DirectoryError);
        r.Items[0].ErrorMessage.Should().Be("Graph misconfigured.");
        r.Items[1].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.UserNotFoundInDirectory);
        _unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failure_while_importing_one_user_is_that_users_result_and_the_others_still_import()
    {
        DirectoryReturns("oid-x", "X", "x@upn", "x@mail.com");
        DirectoryReturns("oid-y", "Y", "y@upn", "y@mail.com");
        var contacts = new Mock<IManagedContactRepository>();
        contacts.Setup(x => x.FindIdentityLinkByEntraObjectIdAsync("oid-x", It.IsAny<CancellationToken>())).ReturnsAsync((ManagedContactIdentityLinkRepositoryRow?)null);
        contacts.Setup(x => x.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ManagedContactRepositoryRow?)null);
        contacts.Setup(x => x.UpsertFromEntraDirectoryUserAsync("X", It.IsAny<string?>(), It.IsAny<string?>(), "oid-x", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database boom"));
        var sut = NewService(contacts.Object, _groups, _unit);

        var r = await sut.ImportEntraUsersAsync(RegId, ["oid-x"]);

        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.ImportFailed);
        r.Items[0].ErrorMessage.Should().Be("Database boom");
        _unit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_import_whose_outcome_is_unknown_is_that_items_result_with_the_operation_id_and_not_a_success()
    {
        DirectoryReturns("oid-u", "U", "u@upn", "u@mail.com");
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-u"]);

        r.Items[0].Success.Should().BeFalse();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.ImportFailed);
        r.Items[0].ErrorMessage.Should().Contain("could not be established").And.Contain("operation ");
    }

    [Fact]
    public async Task A_lost_acknowledgement_on_an_import_reports_the_contact_from_the_committed_row()
    {
        DirectoryReturns("oid-l", "L", "l@upn", "l@mail.com");
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var r = await _sut.ImportEntraUsersAsync(RegId, ["oid-l"]);

        r.Items[0].Success.Should().BeTrue();
        r.Items[0].OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.Success);
        r.Items[0].ManagedContactId.Should().Be(_unit.Single(AuditActionNames.ManagedContactCreated).Target.Id);
    }

    // Entra groups ------------------------------------------------------------------------------------

    private void GroupReturns(string oid, string name) =>
        _directory.Setup(x => x.GetGroupByIdAsync(AnyCtx(), oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(
                true, DiscoveryFailureCategory.None, "ok", null, new EntraDirectoryGroup(oid, name, null, null)));

    [Fact]
    public async Task Importing_a_group_records_that_its_directory_row_was_created_or_refreshed_and_never_its_name()
    {
        GroupReturns("grp-1", "Operations Mailing List");

        var first = await _sut.ImportEntraGroupsAsync(RegId, ["grp-1"]);
        GroupReturns("grp-1", "Operations Mailing List 2");
        await _sut.ImportEntraGroupsAsync(RegId, ["grp-1"]);
        await _sut.ImportEntraGroupsAsync(RegId, ["grp-1"]);

        _unit.Actions.Should().Equal(AuditActionNames.ManagedContactGroupImported, AuditActionNames.ManagedContactGroupImported);
        var rows = _unit.Staged.ToList();
        rows[0].Target.Should().Be(new AuditTarget(AuditTargetTypes.ManagedContact, first[0].Id));
        rows[0].Fields["created"].Should().Be(true);
        rows[1].Fields["created"].Should().Be(false);
        _unit.Operations.Count(o => o.NoOp).Should().Be(1, "the third import found the stored name already matching");
        Everything(_unit).Should().NotContain("Operations Mailing List");
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
