using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

public sealed class SqlNotificationUserPreferenceStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_no_row_exists()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var result = await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_creates_a_new_row_when_none_exists()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var created = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, "actor-1");

        created.UserId.Should().Be("user-1");
        created.NotificationDefinitionId.Should().Be("def-1");
        created.Channel.Should().Be(NotificationChannels.Toast);
        created.IsEnabled.Should().BeTrue();
        created.UpdatedByActorId.Should().Be("actor-1");
        created.CreatedAtUtc.Should().Be(created.UpdatedAtUtc);

        var fetched = await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task SetAsync_updates_in_place_on_a_second_call_for_the_same_key()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var first = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, "actor-1");
        // Windows' default DateTimeOffset.UtcNow timer granularity is ~15.6ms, so a 15ms delay is
        // marginal; 30ms gives comfortable headroom against flakiness.
        await Task.Delay(30);
        var second = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, false, "actor-2");

        second.Id.Should().Be(first.Id, "SetAsync upserts by the (userId, definitionId, channel) key");
        second.IsEnabled.Should().BeFalse();
        second.UpdatedByActorId.Should().Be("actor-2");
        second.UpdatedAtUtc.Should().BeAfter(first.UpdatedAtUtc);
        second.CreatedAtUtc.Should().Be(first.CreatedAtUtc, "creation time never changes on update");

        var allRows = await db.NotificationUserPreferences.AsNoTracking().ToListAsync();
        allRows.Should().ContainSingle("the second SetAsync call must update the existing row, not insert a duplicate");
    }

    [Fact]
    public async Task SetAsync_does_not_cross_contaminate_different_channels_for_the_same_user_and_definition()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-1", NotificationChannels.Email, false, null);

        (await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast))!.IsEnabled.Should().BeTrue();
        (await sut.GetAsync("user-1", "def-1", NotificationChannels.Email))!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListForUserAsync_filters_by_user_and_optionally_by_definition_ids()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-2", NotificationChannels.Toast, false, null);
        await sut.SetAsync("user-2", "def-1", NotificationChannels.Toast, true, null);

        var allForUser1 = await sut.ListForUserAsync("user-1", null);
        allForUser1.Should().HaveCount(2);
        allForUser1.Should().OnlyContain(x => x.UserId == "user-1");

        var filtered = await sut.ListForUserAsync("user-1", new[] { "def-1" });
        filtered.Should().ContainSingle(x => x.NotificationDefinitionId == "def-1");
    }

    [Fact]
    public async Task ListExplicitStatesForDefinitionAsync_filters_by_definition_and_channel_only()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-2", "def-1", NotificationChannels.Toast, false, null);
        // Different definition -- must not leak into the result.
        await sut.SetAsync("user-3", "def-2", NotificationChannels.Toast, true, null);
        // Same definition, other channel -- must not leak into the result.
        await sut.SetAsync("user-4", "def-1", NotificationChannels.Email, true, null);

        var result = await sut.ListExplicitStatesForDefinitionAsync("def-1", NotificationChannels.Toast);

        result.Should().HaveCount(2);
        result.Should().ContainKey("user-1").WhoseValue.Should().BeTrue();
        result.Should().ContainKey("user-2").WhoseValue.Should().BeFalse();
        result.Should().NotContainKey("user-3");
        result.Should().NotContainKey("user-4");
    }

    [Fact]
    public async Task ListExplicitStatesForDefinitionAsync_returns_empty_dictionary_when_no_rows_match()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var result = await sut.ListExplicitStatesForDefinitionAsync("does-not-exist", NotificationChannels.Toast);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListExplicitStatesForDefinitionAsync_throws_for_an_invalid_channel()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var act = () => sut.ListExplicitStatesForDefinitionAsync("def-1", "carrier-pigeon").AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteForDefinitionAsync_removes_only_rows_for_that_definition_and_returns_the_count()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-1", NotificationChannels.Email, true, null);
        await sut.SetAsync("user-2", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-2", NotificationChannels.Toast, true, null);

        var removed = await sut.DeleteForDefinitionAsync("def-1");

        removed.Should().Be(3);
        (await db.NotificationUserPreferences.AsNoTracking().CountAsync(x => x.NotificationDefinitionId == "def-1")).Should().Be(0);
        (await db.NotificationUserPreferences.AsNoTracking().CountAsync(x => x.NotificationDefinitionId == "def-2")).Should().Be(1);
    }

    [Fact]
    public async Task DeleteForDefinitionAsync_returns_zero_when_no_rows_match()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var removed = await sut.DeleteForDefinitionAsync("does-not-exist");

        removed.Should().Be(0);
    }

    // ================================================================================================
    // SetAsync upsert-race handling (mandatory-gate Finding 1).
    //
    // SetAsync does a plain find-then-insert-or-update, so two concurrent calls for the same
    // (userId, notificationDefinitionId, channel) key can both miss the initial SELECT, both attempt an
    // INSERT, and the loser must recover by updating the row the winner just committed instead of letting
    // DbUpdateException propagate. The production fix wraps the insert in try/catch(DbUpdateException) and
    // falls through to an update-in-place recovery path when Store.IsUniqueViolation(ex) matches.
    //
    // EF Core's InMemory provider -- used throughout this file -- does NOT enforce the unique index the
    // way SQL Server does: verified directly (two separate DbContexts against the same InMemory database
    // name, and even two conflicting entities Added and SaveChanges'd together in a single context, both
    // persist silently with zero rows rejected and no exception thrown). So SetAsync's catch clause can
    // never actually be reached by driving the public API alone under this provider; a genuine
    // provider-thrown DbUpdateException would require a real SQL Server round trip, which this unit-test
    // project does not have available to it (see the IntegrationTests project's SqlServerScratchDatabase
    // for that path; it is out of scope for this narrow fix's allowed file set).
    //
    // Instead: the two pieces of logic that together make the recovery correct are proven directly,
    // reaching them via the internal test seams SqlNotificationUserPreferenceStore exposes for exactly
    // this purpose (see its file-level InternalsVisibleTo and doc comments):
    //   1. IsUniqueViolation -- the predicate the catch clause's `when` guard evaluates -- is exercised
    //      directly against fabricated DbUpdateExceptions, proving it matches the index-name signature a
    //      real SQL Server violation carries in its message chain, and does not match unrelated failures.
    //   2. RecoverFromLostInsertRaceForTestsAsync -- the exact re-query-and-update-in-place code SetAsync's
    //      catch clause falls through to -- is exercised by seeding a row that represents "a concurrent
    //      writer already won the race and committed", then calling it for the same key, and asserting the
    //      existing row is updated in place (single row survives, values reflect the losing caller's
    //      intent, CreatedAtUtc is preserved) rather than a duplicate being created or data being lost.
    // Together these prove: the catch clause would recognize the real exception, and the code path it
    // falls through to recovers correctly and without corrupting data.
    // ================================================================================================

    [Fact]
    public void IsUniqueViolation_matches_an_exception_chain_naming_the_preference_unique_index()
    {
        var inner = new InvalidOperationException(
            "Cannot insert duplicate key row in object 'dbo.NotificationUserPreferences' with unique index " +
            "'IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel'.");
        var dbUpdateException = new DbUpdateException("An error occurred while saving the entity changes.", inner);

        SqlNotificationUserPreferenceStore.IsUniqueViolation(dbUpdateException).Should().BeTrue();
    }

    [Fact]
    public void IsUniqueViolation_does_not_match_an_unrelated_failure()
    {
        var inner = new InvalidOperationException("The connection was forcibly closed by the remote host.");
        var dbUpdateException = new DbUpdateException("An error occurred while saving the entity changes.", inner);

        SqlNotificationUserPreferenceStore.IsUniqueViolation(dbUpdateException).Should().BeFalse();
    }

    [Fact]
    public async Task RecoverFromLostInsertRace_updates_the_winning_row_in_place_instead_of_duplicating_it()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        // Seed the row directly, simulating "a concurrent SetAsync call already won the insert race and
        // committed" -- the state SetAsync's catch clause finds itself in after a real DbUpdateException.
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var winnerRow = new NotificationUserPreferenceEntity
        {
            Id = NotificationUserPreferenceId.New().Value,
            UserId = "user-1",
            NotificationDefinitionId = "def-1",
            Channel = NotificationChannels.Email,
            IsEnabled = true,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
            UpdatedByActorId = "winner-actor",
        };
        db.NotificationUserPreferences.Add(winnerRow);
        await db.SaveChangesAsync();

        // The losing caller's values -- what the caught DbUpdateException's SetAsync invocation was trying
        // to persist when it lost the race.
        var result = await sut.RecoverFromLostInsertRaceForTestsAsync(
            "user-1", "def-1", NotificationChannels.Email, isEnabled: false, updatedByActorId: "loser-actor");

        result.Id.Should().Be(winnerRow.Id, "recovery must update the winner's row, not create a new one");
        result.IsEnabled.Should().BeFalse("the losing caller's value must still be applied -- last write wins");
        result.UpdatedByActorId.Should().Be("loser-actor");
        result.CreatedAtUtc.Should().Be(createdAt, "recovery is an update, not a re-creation -- creation time must be preserved");
        result.UpdatedAtUtc.Should().BeAfter(createdAt);

        var allRows = await db.NotificationUserPreferences.AsNoTracking().ToListAsync();
        allRows.Should().ContainSingle("recovery must never leave a duplicate row behind for this key");
        allRows[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task RecoverFromLostInsertRace_throws_if_the_row_it_expects_to_recover_is_somehow_gone()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var act = () => sut.RecoverFromLostInsertRaceForTestsAsync(
            "user-1", "def-1", NotificationChannels.Email, isEnabled: false, updatedByActorId: "loser-actor");

        // No row exists at all -- this should never happen in the real SetAsync catch clause (the
        // DbUpdateException guarantees a conflicting row exists), so this documents that the recovery path
        // fails loudly rather than silently fabricating a row, if that invariant is ever violated.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
