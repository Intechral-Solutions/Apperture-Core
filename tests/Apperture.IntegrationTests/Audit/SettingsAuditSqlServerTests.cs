using System.Data.Common;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 platform and integration settings against SQL Server (design gate §13.4, §13.5, §22 and the rulings on secret audit
/// identifiers and secret-store transaction participation). Each change commits with its audit rows and, for a sensitive
/// setting, with the secret it stores or retires and the store's own evidence, or none of them does. Fails, never skips,
/// without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SettingsAuditSqlServerTests : IAsyncLifetime
{
    /// <summary>A value shaped like a real credential. It must never leave the secret store's own payload column.</summary>
    private static readonly string Sentinel = "sentinel-setting-secret-" + Guid.NewGuid().ToString("N");

    private const string IntegrationId = "int-aud14";
    private const string IntegrationTextKey = "Test.Aud14.Integration.Text";
    private const string IntegrationSecretKey = "Test.Aud14.Integration.Secret";

    private static readonly string PlatformTextKey = PlatformSettingKeys.NotificationsEmailSmtpHost;
    private static readonly string PlatformSecretKey = PlatformSettingKeys.NotificationsEmailSmtpPasswordRef;

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public SettingsAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14Settings_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Platform: ordinary settings ----

    [Fact]
    public async Task A_platform_ordinary_setting_is_set_changed_left_unchanged_and_reset_each_with_its_row()
    {
        var (admin, principal) = await AdminAsync();

        await SetPlatformAsync(principal, PlatformTextKey, "smtp.one.test", null);
        var first = await PlatformRowVersionAsync(PlatformTextKey);
        await SetPlatformAsync(principal, PlatformTextKey, "smtp.two.test", first);
        var second = await PlatformRowVersionAsync(PlatformTextKey);

        await SetPlatformAsync(principal, PlatformTextKey, "smtp.two.test", second);
        (await PlatformRowVersionAsync(PlatformTextKey)).Should().Be(second, "an identical value writes nothing");

        await ResetPlatformAsync(principal, PlatformTextKey);
        await ResetPlatformAsync(principal, PlatformTextKey);

        var rows = await AuditRowsAsync();
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingReset);
        foreach (var row in rows)
        {
            row["Actor"].Should().Be(admin);
            row["Category"].Should().Be(AuditCategories.Configuration);
            row["TargetType"].Should().Be(AuditTargetTypes.PlatformSetting);
            row["TargetId"].Should().Be(PlatformTextKey);
            row["OperationEventId"].Should().Be(row["EventId"], "each is the primary row of its own operation");
        }

        Detail(rows[0], "operation").Should().Be("set");
        Detail(rows[0], "newValue").Should().Be("smtp.one.test");
        Detail(rows[0], "previousValue").Should().BeNull();
        Detail(rows[1], "previousValue").Should().Be("smtp.one.test");
        Detail(rows[1], "newValue").Should().Be("smtp.two.test");
        Detail(rows[2], "previousValue").Should().Be("smtp.two.test");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PlatformSettingValues;")).Should().Be(0);
    }

    [Fact]
    public async Task The_sender_address_is_recorded_and_the_smtp_username_is_audited_by_name_only()
    {
        var (_, principal) = await AdminAsync();
        const string fromAddress = "notifications@example.test";
        const string smtpUser = "smtp-user@example.test";

        await SetPlatformAsync(principal, PlatformSettingKeys.NotificationsEmailFromAddress, fromAddress, null);
        await SetPlatformAsync(principal, PlatformSettingKeys.NotificationsEmailSmtpUser, smtpUser, null);
        await ResetPlatformAsync(principal, PlatformSettingKeys.NotificationsEmailFromAddress);
        await ResetPlatformAsync(principal, PlatformSettingKeys.NotificationsEmailSmtpUser);

        var rows = await AuditRowsAsync();
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSet,
            AuditActionNames.PlatformSettingReset, AuditActionNames.PlatformSettingReset);
        Detail(rows[0], "newValue").Should().Be(fromAddress, "the sender address is an explicitly permitted operational email field");
        Detail(rows[2], "previousValue").Should().Be(fromAddress);

        Detail(rows[1], "key").Should().Be(PlatformSettingKeys.NotificationsEmailSmtpUser);
        Detail(rows[1], "operation").Should().Be("set");
        Detail(rows[1], "newValue").Should().BeNull();
        Detail(rows[1], "previousValue").Should().BeNull();
        Detail(rows[3], "previousValue").Should().BeNull();
        foreach (var row in rows.Skip(1).Where((_, i) => i is 0 or 2))
            ((string)row["Details"]!).Should().NotContain(smtpUser, "the username is credential-related even when formatted as an email address");
        foreach (var column in rows.SelectMany(r => r.Where(c => c.Key != "Details").Select(c => c.Value)).OfType<string>())
            column.Should().NotContain(smtpUser);
    }

    [Fact]
    public async Task A_malformed_ordinary_override_can_still_be_reset_and_the_reset_is_audited_without_its_previous_value()
    {
        var (admin, principal) = await AdminAsync();
        await InsertPlatformOverrideAsync(PlatformSettingKeys.NotificationsDeliveryBatchSize, "\"not-an-integer\"");

        await ResetPlatformAsync(principal, PlatformSettingKeys.NotificationsDeliveryBatchSize);

        var row = (await AuditRowsAsync()).Single();
        row["Action"].Should().Be(AuditActionNames.PlatformSettingReset);
        row["Actor"].Should().Be(admin);
        Detail(row, "previousValue").Should().BeNull("a value that cannot be displayed is omitted");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PlatformSettingValues;")).Should().Be(0, "the override is gone");
    }

    // ---- Platform: sensitive settings ----

    [Fact]
    public async Task A_platform_sensitive_setting_is_replaced_cleared_and_reset_with_its_secret_stored_and_retired_in_the_same_unit()
    {
        var (admin, principal) = await AdminAsync();

        // Replace for the first time: a new secret.
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        (await ResolveAsync(secretRef)).Should().Be(Sentinel);
        (await StoreEventsAsync(secretRef)).Should().Equal("Create", "AddVersion", "ActivateVersion");

        // Replace again: a new active version of the secret the setting already names.
        await ReplacePlatformSecretAsync(principal, Sentinel + "-2", await PlatformRowVersionAsync(PlatformSecretKey));
        (await PlatformSecretRefAsync()).Should().Be(secretRef, "the reference is preserved under the versioning model");
        (await ResolveAsync(secretRef)).Should().Be(Sentinel + "-2");
        (await VersionCountAsync(secretRef)).Should().Be(2);

        // Clear: the override and its secret are retired together.
        await ClearPlatformSecretAsync(principal);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Disabled);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PlatformSettingValues;")).Should().Be(0);

        // A new secret, then a reset.
        await ReplacePlatformSecretAsync(principal, Sentinel + "-3", null);
        var third = await PlatformSecretRefAsync();
        third.Should().NotBe(secretRef);
        await ResetPlatformAsync(principal, PlatformSecretKey);
        (await SecretStateAsync(third)).Should().Be(SecretState.Disabled);

        var rows = await AuditRowsAsync();
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSecretStored,
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSecretStored,
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSecretRetired,
            AuditActionNames.PlatformSettingSet, AuditActionNames.PlatformSettingSecretStored,
            AuditActionNames.PlatformSettingReset, AuditActionNames.PlatformSettingSecretRetired);

        Detail(rows[0], "operation").Should().Be("replace");
        Detail(rows[0], "sensitive").Should().Be("true");
        Detail(rows[0], "previousConfigured").Should().Be("false");
        Detail(rows[1], "change").Should().Be("created");
        Detail(rows[2], "previousConfigured").Should().Be("true");
        Detail(rows[3], "change").Should().Be("replaced");
        Detail(rows[4], "operation").Should().Be("clear");
        Detail(rows[5], "reason").Should().Be("cleared");
        Detail(rows[9], "reason").Should().Be("reset");

        for (var i = 0; i < rows.Count; i += 2)
        {
            rows[i]["OperationEventId"].Should().Be(rows[i]["EventId"]);
            rows[i + 1]["OperationEventId"].Should().Be(rows[i]["EventId"], "the secret row is a secondary row of the same unit");
            rows[i + 1]["Category"].Should().Be(AuditCategories.Secrets);
            rows[i + 1]["Severity"].Should().Be(AuditSeverities.Elevated);
            rows[i + 1]["TargetType"].Should().Be(AuditTargetTypes.PlatformSetting, "the owning setting is the target, never the secret");
            rows[i + 1]["TargetId"].Should().Be(PlatformSecretKey);
        }

        foreach (var row in rows)
            row["Actor"].Should().Be(admin);

        AssertNoSecretMaterial(rows, Sentinel, secretRef, third);
        await AssertStoreEvidenceHoldsNoSecretMaterialAsync(Sentinel, secretRef, third);
    }

    [Fact]
    public async Task A_replacement_whose_audit_fails_leaves_the_previous_secret_live_and_nothing_else_changed()
    {
        // The live-secret-first defect: the replacement used to add and activate a version on the secret the setting names
        // before the change committed, so a failed change still changed the credential the setting resolves.
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var before = await SnapshotAsync(secretRef);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var currentRowVersion = await PlatformRowVersionAsync(PlatformSecretKey);
        Exception thrown;
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Platform(db, principal).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                currentRowVersion);
            thrown = (await act.Should().ThrowAsync<Exception>()).Which;

            db.ChangeTracker.Entries().Should().BeEmpty("nothing the failed change staged is still tracked");
            (await db.SaveChangesAsync()).Should().Be(0, "no later save can apply it");
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await SnapshotAsync(secretRef)).Should().Be(before);
        (await ResolveAsync(secretRef)).Should().Be(Sentinel, "the live credential never changed");
        AssertNoSecretMaterial(thrown, Sentinel, "replacement-" + Sentinel, secretRef);
    }

    [Fact]
    public async Task A_failed_first_replacement_leaves_no_orphaned_secret_no_row_and_nothing_tracked()
    {
        // The settings upsert used to fail after the secret was stored; the compensation then saved the failed row again,
        // failed, and left the new secret behind.
        var (_, principal) = await AdminAsync();

        var failUpsert = new FailingCommand(sql => sql.Contains("INSERT INTO [PlatformSettingValues]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failUpsert))
        {
            var act = () => Platform(db, principal).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = Sentinel },
                null);
            (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<AuditInvariantException>();

            db.ChangeTracker.Entries().Should().BeEmpty();
            (await db.SaveChangesAsync()).Should().Be(0);
        }

        failUpsert.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors;")).Should().Be(0, "the secret was stored in the unit that rolled back");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretVersions;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PlatformSettingValues;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(0);
    }

    [Fact]
    public async Task A_stale_row_version_is_a_conflict_that_stores_nothing_and_leaves_the_live_secret_unchanged()
    {
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var before = await SnapshotAsync(secretRef);
        var staleVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

        await using (var db = _db.NewDb())
        {
            var act = () => Platform(db, principal).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "stale-" + Sentinel },
                staleVersion);
            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await SnapshotAsync(secretRef)).Should().Be(before);
        (await ResolveAsync(secretRef)).Should().Be(Sentinel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_lost_first_insert_race_is_a_concurrency_conflict_that_stores_nothing_and_keeps_the_winners_row(bool sensitive)
    {
        var (_, principal) = await AdminAsync();
        var key = sensitive ? PlatformSecretKey : PlatformTextKey;
        var winnerId = $"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}";

        // Between this request's read (no override) and its insert, another request commits its own insert.
        var race = new RaceBeforeInsert(
            _db,
            $"""
            INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
            VALUES (N'{winnerId}', N'{key}', N'{PlatformSettingScopeConstants.InstanceKind}', N'{PlatformSettingScopeConstants.InstanceId}',
                    N'"winner"', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        await using (var db = _db.NewDb(race))
        {
            var payload = sensitive
                ? new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = Sentinel }
                : new PlatformSettingUpdatePayload { ScalarValue = "loser.test" };
            var act = () => Platform(db, principal).SetAsync(key, payload, null);

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>("a lost race is reported, never borrowed as this request's success");
            db.ChangeTracker.Entries().Should().BeEmpty();
        }

        race.Raced.Should().BeTrue();
        (await _db.ScalarAsync<string>($"SELECT ValueJson FROM PlatformSettingValues WHERE Id = N'{winnerId}';")).Should().Be("\"winner\"");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(0, "the loser recorded no change it did not make");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(0);
    }

    [Fact]
    public async Task A_lost_commit_acknowledgement_reconstructs_success_without_a_second_secret_version()
    {
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var rowVersion = await PlatformRowVersionAsync(PlatformSecretKey);

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
        {
            var result = await Platform(db, principal).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                rowVersion);
            result.IsSecretConfigured.Should().BeTrue();
        }

        attempts.Started.Should().Be(1, "a verified commit is not run again");
        (await VersionCountAsync(secretRef)).Should().Be(2, "one arranged version and exactly one from the acknowledged change");
        (await ResolveAsync(secretRef)).Should().Be("replacement-" + Sentinel);
        var rows = await AuditRowsAsync();
        rows.Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSet).Should().Be(2);
        rows.Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSecretStored).Should().Be(2);
    }

    [Fact]
    public async Task A_failed_response_read_after_a_verified_commit_is_reported_as_an_unavailable_result_without_rerunning_the_change()
    {
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var rowVersion = await PlatformRowVersionAsync(PlatformSecretKey);

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
        {
            var act = () => Platform(db, principal, decorateResolver: inner => new ReadFailingResolver(inner)).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                rowVersion);

            var thrown = (await act.Should().ThrowAsync<AuditCommittedResultUnavailableException>()).Which;
            thrown.OperationEventId.Should().NotBeNullOrEmpty();
            AssertNoSecretMaterial(thrown, Sentinel, "replacement-" + Sentinel, secretRef);
        }

        attempts.Started.Should().Be(1, "a verified commit is not run again and nothing is retried");
        (await VersionCountAsync(secretRef)).Should().Be(2, "exactly one version from the committed change, none from a retry");
        (await ResolveAsync(secretRef)).Should().Be("replacement-" + Sentinel);
        (await AuditRowsAsync()).Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSet).Should().Be(2);
    }

    [Fact]
    public async Task A_confirmed_rollback_reruns_the_change_and_only_the_reruns_version_exists()
    {
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var rowVersion = await PlatformRowVersionAsync(PlatformSecretKey);

        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(rolledBack, attempts))
        {
            await Platform(db, principal).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                rowVersion);
        }

        attempts.Started.Should().Be(2);
        (await VersionCountAsync(secretRef)).Should().Be(2, "the rolled-back attempt's version does not exist");
        (await ResolveAsync(secretRef)).Should().Be("replacement-" + Sentinel);
        var rows = await AuditRowsAsync();
        rows.Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSet).Should().Be(2, "one for the arranged change, one for the rerun");
        rows.Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSecretStored).Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_outcome_leaves_the_secret_the_setting_and_its_rows_wholly_committed_or_wholly_absent(bool committed)
    {
        var (_, principal) = await AdminAsync();
        await ReplacePlatformSecretAsync(principal, Sentinel, null);
        var secretRef = await PlatformSecretRefAsync();
        var before = await SnapshotAsync(secretRef);
        var rowVersion = await PlatformRowVersionAsync(PlatformSecretKey);

        DbTransactionInterceptor fault = committed ? new LostCommitAcknowledgement() : new RolledBackCommit();
        Exception thrown;
        await using (var db = _db.NewDb(fault))
        {
            var unit = new SqlAuditedUnitOfWork(db, principal, HarnessCatalog, TimeProvider.System, FastOptions(), null, null, new UnreachableVerification());
            var act = () => Platform(db, principal, unit).SetAsync(
                PlatformSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                rowVersion);
            thrown = (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which;
        }

        var after = await SnapshotAsync(secretRef);
        var setRows = (await AuditRowsAsync()).Count(r => (string)r["Action"]! == AuditActionNames.PlatformSettingSet);
        if (committed)
        {
            after.VersionCount.Should().Be(before.VersionCount + 1);
            after.ActiveVersion.Should().NotBe(before.ActiveVersion);
            setRows.Should().Be(2);
        }
        else
        {
            after.Should().Be(before);
            setRows.Should().Be(1);
        }

        AssertNoSecretMaterial(thrown, Sentinel, "replacement-" + Sentinel, secretRef);
    }

    [Fact]
    public async Task Changes_that_change_nothing_write_no_row_and_no_secret()
    {
        var (_, principal) = await AdminAsync();

        // A clear and a reset of an override that does not exist, and a blank secret operation.
        await using (var db = _db.NewDb())
            await Platform(db, principal).SetAsync(PlatformSecretKey, new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear }, null);
        await ResetPlatformAsync(principal, PlatformSecretKey);
        await ResetPlatformAsync(principal, PlatformTextKey);
        await using (var db = _db.NewDb())
            await Platform(db, principal).SetAsync(PlatformSecretKey, new PlatformSettingUpdatePayload { SecretValue = "" }, null);

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PlatformSettingValues;")).Should().Be(0);
    }

    // ---- Integration settings ----

    [Fact]
    public async Task An_integration_ordinary_setting_is_set_and_reset_with_rows_that_target_the_integration_setting()
    {
        var (admin, principal) = await AdminAsync();

        await SetIntegrationAsync(principal, IntegrationTextKey, "one", null);
        var rowVersion = await IntegrationRowVersionAsync(IntegrationTextKey);
        await SetIntegrationAsync(principal, IntegrationTextKey, "one", rowVersion);
        await ResetIntegrationAsync(principal, IntegrationTextKey);
        await ResetIntegrationAsync(principal, IntegrationTextKey);

        var rows = await AuditRowsAsync();
        rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.IntegrationSettingSet, AuditActionNames.IntegrationSettingReset);
        foreach (var row in rows)
        {
            row["Actor"].Should().Be(admin);
            row["TargetType"].Should().Be(AuditTargetTypes.IntegrationSetting);
            row["TargetId"].Should().Be(AuditEvents.IntegrationSettingTargetId(IntegrationId, IntegrationTextKey));
            Detail(row, "integrationId").Should().Be(IntegrationId);
            Detail(row, "key").Should().Be(IntegrationTextKey);
        }

        Detail(rows[0], "newValue").Should().Be("one");
        Detail(rows[1], "previousValue").Should().Be("one");
    }

    [Fact]
    public async Task An_integration_sensitive_setting_is_replaced_cleared_and_reset_with_its_secret_rows_in_the_same_unit()
    {
        var (_, principal) = await AdminAsync();

        await ReplaceIntegrationSecretAsync(principal, Sentinel, null);
        var secretRef = await IntegrationSecretRefAsync();
        (await ResolveAsync(secretRef)).Should().Be(Sentinel);

        await ReplaceIntegrationSecretAsync(principal, Sentinel + "-2", await IntegrationRowVersionAsync(IntegrationSecretKey));
        (await IntegrationSecretRefAsync()).Should().Be(secretRef);
        (await VersionCountAsync(secretRef)).Should().Be(2);

        await ClearIntegrationSecretAsync(principal);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Disabled);

        await ReplaceIntegrationSecretAsync(principal, Sentinel + "-3", null);
        var third = await IntegrationSecretRefAsync();
        await ResetIntegrationAsync(principal, IntegrationSecretKey);
        (await SecretStateAsync(third)).Should().Be(SecretState.Disabled);

        var rows = await AuditRowsAsync();
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            AuditActionNames.IntegrationSettingSet, AuditActionNames.IntegrationSettingSecretStored,
            AuditActionNames.IntegrationSettingSet, AuditActionNames.IntegrationSettingSecretStored,
            AuditActionNames.IntegrationSettingSet, AuditActionNames.IntegrationSettingSecretRetired,
            AuditActionNames.IntegrationSettingSet, AuditActionNames.IntegrationSettingSecretStored,
            AuditActionNames.IntegrationSettingReset, AuditActionNames.IntegrationSettingSecretRetired);
        for (var i = 0; i < rows.Count; i += 2)
        {
            rows[i + 1]["OperationEventId"].Should().Be(rows[i]["EventId"]);
            rows[i + 1]["Category"].Should().Be(AuditCategories.Secrets);
            rows[i + 1]["TargetType"].Should().Be(AuditTargetTypes.IntegrationSetting);
            rows[i + 1]["TargetId"].Should().Be(AuditEvents.IntegrationSettingTargetId(IntegrationId, IntegrationSecretKey));
        }

        AssertNoSecretMaterial(rows, Sentinel, secretRef, third);
        await AssertStoreEvidenceHoldsNoSecretMaterialAsync(Sentinel, secretRef, third);
    }

    [Fact]
    public async Task An_integration_replacement_whose_audit_fails_leaves_the_previous_secret_live()
    {
        var (_, principal) = await AdminAsync();
        await ReplaceIntegrationSecretAsync(principal, Sentinel, null);
        var secretRef = await IntegrationSecretRefAsync();
        var before = await SnapshotAsync(secretRef);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = async () => await Integration(db, principal).SetAsync(
                IntegrationId,
                IntegrationSecretKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "replacement-" + Sentinel },
                await IntegrationRowVersionAsync(IntegrationSecretKey));
            AssertNoSecretMaterial((await act.Should().ThrowAsync<Exception>()).Which, Sentinel, "replacement-" + Sentinel, secretRef);
            db.ChangeTracker.Entries().Should().BeEmpty();
        }

        (await SnapshotAsync(secretRef)).Should().Be(before);
        (await ResolveAsync(secretRef)).Should().Be(Sentinel);
    }

    // ---- The in-transaction integration reset ----

    [Fact]
    public async Task The_in_transaction_reset_commits_with_the_callers_unit_as_secondary_rows_and_opens_no_transaction_of_its_own()
    {
        var (admin, principal) = await AdminAsync();
        await ReplaceIntegrationSecretAsync(principal, Sentinel, null);
        var secretRef = await IntegrationSecretRefAsync();
        await SetIntegrationAsync(principal, IntegrationTextKey, "plain", null);
        var arranged = (await AuditRowsAsync()).Count;

        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(attempts))
        {
            var helper = (IIntegrationSettingResetInTransaction)Integration(db, principal);
            await NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "caller-owned"), ct);
                    (await helper.ResetInTransactionAsync(transaction, IntegrationId, IntegrationSecretKey, ct)).Should().BeTrue();
                    (await helper.ResetInTransactionAsync(transaction, IntegrationId, IntegrationTextKey, ct)).Should().BeTrue();
                    (await helper.ResetInTransactionAsync(transaction, IntegrationId, IntegrationTextKey, ct)).Should().BeFalse("the override is already gone");
                    return true;
                },
                (_, _) => Task.FromResult(true));
        }

        attempts.Started.Should().Be(1, "the helper joined the unit's transaction and began none");
        var rows = (await AuditRowsAsync()).Skip(arranged).ToList();
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            HarnessPrimary.Action,
            AuditActionNames.IntegrationSettingReset, AuditActionNames.IntegrationSettingSecretRetired, AuditActionNames.IntegrationSettingReset);
        rows.Select(r => r["OperationEventId"]).Distinct().Should().ContainSingle("every row belongs to the caller's one operation");
        rows[0]["EventId"].Should().Be(rows[0]["OperationEventId"]);
        foreach (var row in rows)
            row["Actor"].Should().Be(admin);

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeId = N'{IntegrationId}';")).Should().Be(0);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Disabled);
        AssertNoSecretMaterial(rows, Sentinel, secretRef);
    }

    [Fact]
    public async Task The_in_transaction_reset_rolls_back_with_the_callers_unit_and_commits_nothing_itself()
    {
        var (_, principal) = await AdminAsync();
        await ReplaceIntegrationSecretAsync(principal, Sentinel, null);
        var secretRef = await IntegrationSecretRefAsync();
        var before = await SnapshotAsync(secretRef);
        var auditRows = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var helper = (IIntegrationSettingResetInTransaction)Integration(db, principal);
            var act = () => NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "caller-owned"), ct);
                    await helper.ResetInTransactionAsync(transaction, IntegrationId, IntegrationSecretKey, ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeId = N'{IntegrationId}';")).Should().Be(1, "the override was not removed");
        (await SnapshotAsync(secretRef)).Should().Be(before);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Active);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditRows);
    }

    [Fact]
    public async Task The_in_transaction_reset_refuses_a_transaction_that_is_not_current_before_it_changes_anything()
    {
        var (_, principal) = await AdminAsync();
        await ReplaceIntegrationSecretAsync(principal, Sentinel, null);
        var secretRef = await IntegrationSecretRefAsync();
        Snapshot before;

        await using (var db = _db.NewDb())
        {
            var helper = (IIntegrationSettingResetInTransaction)Integration(db, principal);
            IAuditedTransaction? leaked = null;
            await NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    leaked = transaction;
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "finished"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));

            // The unit has ended: there is no transaction to join, and the helper must not save the delete by itself.
            before = await SnapshotAsync(secretRef);
            var act = () => helper.ResetInTransactionAsync(leaked!, IntegrationId, IntegrationSecretKey);
            await act.Should().ThrowAsync<AuditInvariantException>();
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeId = N'{IntegrationId}';")).Should().Be(1);
        (await SnapshotAsync(secretRef)).Should().Be(before);
    }

    // ---- service wiring ----

    private PlatformSettingsService Platform(
        AppertureDbContext db,
        IPrincipalContext principal,
        IAuditedUnitOfWork? unit = null,
        Func<IPlatformSettingsResolver, IPlatformSettingsResolver>? decorateResolver = null)
    {
        var registry = new PlatformSettingDefinitionRegistry();
        var repository = new SqlPlatformSettingValueRepository(db);
        IPlatformSettingsResolver resolver = new PlatformSettingsResolver(registry, repository, new ConfigurationBuilder().Build());
        return new PlatformSettingsService(
            registry,
            repository,
            decorateResolver?.Invoke(resolver) ?? resolver,
            Coordinator(db, principal),
            new PlatformSettingSecretMaterialProvisioner(SecretStore(db)),
            unit ?? NewUnit(db, principal));
    }

    private IntegrationSettingsService Integration(AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork? unit = null)
    {
        var registry = new TestIntegrationRegistry();
        var repository = new SqlPlatformSettingValueRepository(db);
        return new IntegrationSettingsService(
            registry,
            repository,
            new IntegrationSettingsResolver(registry, repository),
            Coordinator(db, principal),
            new PlatformSettingSecretMaterialProvisioner(SecretStore(db)),
            new SqlIntegrationRepository(db),
            unit ?? NewUnit(db, principal));
    }

    private static PlatformSettingOptionProviderCoordinator Coordinator(AppertureDbContext db, IPrincipalContext principal)
    {
        var integrations = new SqlIntegrationRepository(db);
        return new PlatformSettingOptionProviderCoordinator(
            new PlatformSettingOptionProviderRegistry([]),
            new PlatformSettingOptionProviderAuthorization(principal, new AllowAll(), integrations),
            new IntegrationSettingOptionProviderAuthorization(principal, new AllowAll(), integrations));
    }

    private static SqlSecretStore SecretStore(AppertureDbContext db) =>
        new(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());

    private async Task SetPlatformAsync(IPrincipalContext principal, string key, string value, string? rowVersion)
    {
        await using var db = _db.NewDb();
        await Platform(db, principal).SetAsync(key, new PlatformSettingUpdatePayload { ScalarValue = value }, rowVersion);
    }

    private async Task ResetPlatformAsync(IPrincipalContext principal, string key)
    {
        await using var db = _db.NewDb();
        await Platform(db, principal).ResetAsync(key);
    }

    private async Task ReplacePlatformSecretAsync(IPrincipalContext principal, string secret, string? rowVersion)
    {
        await using var db = _db.NewDb();
        await Platform(db, principal).SetAsync(
            PlatformSecretKey,
            new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = secret },
            rowVersion);
    }

    private async Task ClearPlatformSecretAsync(IPrincipalContext principal)
    {
        await using var db = _db.NewDb();
        await Platform(db, principal).SetAsync(
            PlatformSecretKey, new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear }, null);
    }

    private async Task SetIntegrationAsync(IPrincipalContext principal, string key, string value, string? rowVersion)
    {
        await using var db = _db.NewDb();
        await Integration(db, principal).SetAsync(IntegrationId, key, new PlatformSettingUpdatePayload { ScalarValue = value }, rowVersion);
    }

    private async Task ResetIntegrationAsync(IPrincipalContext principal, string key)
    {
        await using var db = _db.NewDb();
        await Integration(db, principal).ResetAsync(IntegrationId, key);
    }

    private async Task ReplaceIntegrationSecretAsync(IPrincipalContext principal, string secret, string? rowVersion)
    {
        await using var db = _db.NewDb();
        await Integration(db, principal).SetAsync(
            IntegrationId,
            IntegrationSecretKey,
            new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = secret },
            rowVersion);
    }

    private async Task ClearIntegrationSecretAsync(IPrincipalContext principal)
    {
        await using var db = _db.NewDb();
        await Integration(db, principal).SetAsync(
            IntegrationId, IntegrationSecretKey, new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear }, null);
    }

    private Task InsertPlatformOverrideAsync(string key, string valueJson) =>
        _db.ExecuteAsync(
            $"""
            INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
            VALUES (N'{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}', N'{key}',
                    N'{PlatformSettingScopeConstants.InstanceKind}', N'{PlatformSettingScopeConstants.InstanceId}',
                    N'{valueJson}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    // ---- reading ----

    private async Task<(string Admin, Principal Principal)> AdminAsync()
    {
        var id = NewUserId();
        await _db.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private Task<List<Dictionary<string, object?>>> AuditRowsAsync() =>
        _db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private async Task<string> PlatformRowVersionAsync(string key) =>
        await RowVersionAsync($"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}");

    private async Task<string> IntegrationRowVersionAsync(string key) =>
        await RowVersionAsync($"{PlatformSettingScopeConstants.IntegrationKind}:{IntegrationId}:{key}");

    private async Task<string> RowVersionAsync(string id) =>
        Convert.ToBase64String((byte[])(await _db.RowsAsync($"SELECT RowVersion FROM PlatformSettingValues WHERE Id = N'{id}';")).Single()["RowVersion"]!);

    private async Task<string> PlatformSecretRefAsync() =>
        await SecretRefAsync($"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{PlatformSecretKey}");

    private async Task<string> IntegrationSecretRefAsync() =>
        await SecretRefAsync($"{PlatformSettingScopeConstants.IntegrationKind}:{IntegrationId}:{IntegrationSecretKey}");

    /// <summary>The reference a setting row holds: the row stores it as a JSON string.</summary>
    private async Task<string> SecretRefAsync(string rowId) =>
        JsonSerializer.Deserialize<string>((await _db.ScalarAsync<string>($"SELECT ValueJson FROM PlatformSettingValues WHERE Id = N'{rowId}';"))!)!;

    private async Task<SecretState> SecretStateAsync(string secretRef) =>
        (SecretState)await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secretRef}';");

    private async Task<int> VersionCountAsync(string secretRef) =>
        await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{secretRef}';");

    private async Task<List<string>> StoreEventsAsync(string secretRef) =>
        (await _db.RowsAsync($"SELECT EventType FROM SecretAudits WHERE SecretId = '{secretRef}' ORDER BY Id;"))
            .Select(r => (string)r["EventType"]!).ToList();

    /// <summary>Everything a failed or unacknowledged change must leave as it was.</summary>
    private sealed record Snapshot(
        string? ActiveVersion, int VersionCount, int State, string RowValue, string RowVersion, int AuditLogs, int StoreEvidence);

    private async Task<Snapshot> SnapshotAsync(string secretRef)
    {
        var rows = await _db.RowsAsync("SELECT ValueJson, RowVersion FROM PlatformSettingValues;");
        var row = rows.Single();
        return new Snapshot(
            await _db.ScalarAsync<string?>($"SELECT ActiveVersion FROM SecretDescriptors WHERE Id = '{secretRef}';"),
            await VersionCountAsync(secretRef),
            await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secretRef}';"),
            (string)row["ValueJson"]!,
            Convert.ToBase64String((byte[])row["RowVersion"]!),
            await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;"),
            await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;"));
    }

    private async Task<string?> ResolveAsync(string secretRef)
    {
        await using var db = _db.NewDb();
        try
        {
            var resolved = await new SqlSecretResolver(SecretStore(db), new PassThroughProtector()).ResolveAsync(new SecretRef(secretRef));
            return resolved.Material.OpaqueString;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        if (!details.RootElement.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    // ---- confidentiality ----

    /// <summary>No audit column holds a secret value or a secret's store id, which is the reference a setting row holds, in either GUID form.</summary>
    private static void AssertNoSecretMaterial(IEnumerable<Dictionary<string, object?>> rows, params string[] material)
    {
        var forms = WithGuidForms(material);
        foreach (var row in rows)
        {
            foreach (var value in row.Values.OfType<string>())
            {
                foreach (var item in forms)
                    value.Should().NotContainEquivalentOf(item, $"audit column values never hold a secret or its reference ({row["Action"]})");
            }
        }
    }

    private static void AssertNoSecretMaterial(Exception exception, params string[] material)
    {
        var forms = WithGuidForms(material);
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            foreach (var item in forms)
                e.Message.Should().NotContainEquivalentOf(item, "exception text never holds a secret or its reference");
        }
    }

    /// <summary>The store's evidence is keyed by the secret's id; no other column of it holds a secret or a reference.</summary>
    private async Task AssertStoreEvidenceHoldsNoSecretMaterialAsync(params string[] material)
    {
        var forms = WithGuidForms(material);
        foreach (var row in await _db.RowsAsync("SELECT EventType, Version, Actor, Outcome FROM SecretAudits;"))
        {
            foreach (var value in row.Values.OfType<string>())
            {
                foreach (var item in forms)
                    value.Should().NotContainEquivalentOf(item, "the store's evidence holds ids and event types only");
            }
        }
    }

    private static string[] WithGuidForms(string[] material) =>
        material.SelectMany(m => Guid.TryParse(m, out var guid) ? new[] { m, guid.ToString("D"), guid.ToString("N") } : new[] { m }).Distinct().ToArray();

    // ---- doubles ----

    /// <summary>Between a request's read and its insert, another request commits its own insert of the same row.</summary>
    private sealed class RaceBeforeInsert(AuditSqlScratch scratch, string competingInsert) : DbCommandInterceptor
    {
        public bool Raced { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Raced && command.CommandText.Contains("INSERT INTO [PlatformSettingValues]", StringComparison.Ordinal))
            {
                Raced = true;
                await scratch.ExecuteAsync(competingInsert);
            }

            return result;
        }
    }

    /// <summary>Answers everything the change itself reads, and fails the response read that follows the commit.</summary>
    private sealed class ReadFailingResolver(IPlatformSettingsResolver inner) : IPlatformSettingsResolver
    {
        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) =>
            inner.ListResolvedAsync(cancellationToken);

        public Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected: the response read failed after the commit.");

        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
            inner.ResolveRawValueAsync(key, cancellationToken);
    }

    private sealed class TestIntegrationRegistry : IIntegrationSettingDefinitionRegistry
    {
        private static readonly PlatformSettingValueScopeKind[] IntegrationScope = [PlatformSettingValueScopeKind.Integration];

        private static readonly IReadOnlyList<PlatformSettingDefinition> All =
        [
            new()
            {
                Key = IntegrationTextKey,
                DisplayName = "Integration text (test)",
                Description = "Plain integration-scoped string.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = PlatformSettingInputKind.Text,
                AllowedValueScopes = IntegrationScope,
                ConfigurationPath = "Test:Aud14:Integration:Text",
                DefaultValue = string.Empty,
                AuditValue = PlatformSettingAuditValuePolicy.Record
            },
            new()
            {
                Key = IntegrationSecretKey,
                DisplayName = "Integration secret (test)",
                Description = "Sensitive integration-scoped setting.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = PlatformSettingInputKind.Secret,
                IsSensitive = true,
                AllowedValueScopes = IntegrationScope,
                ConfigurationPath = "Test:Aud14:Integration:Secret",
                DefaultValue = string.Empty
            }
        ];

        public IReadOnlyList<PlatformSettingDefinition> GetAll() => All;

        public PlatformSettingDefinition? TryGet(string key) => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
    }

    private sealed class AllowAll : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }
}
