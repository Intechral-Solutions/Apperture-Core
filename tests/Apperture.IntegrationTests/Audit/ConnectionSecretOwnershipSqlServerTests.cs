using System.Text.Json;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.ConnectionSecretOwnershipSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.2 (A2.2, A2.3, A2.5): owned secret resolution against the real store, on locking READ COMMITTED and
/// READ_COMMITTED_SNAPSHOT catalogs. The owner is compared with the secret's own descriptor before the version row or the
/// protected payload is read, so a foreign secret is never read or unprotected; every refusal is one exception with one fixed
/// message that holds no identifier; and a secret the connection owns resolves exactly as it did before the owner check existed.
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionSecretOwnershipSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ConnectionSecretOwnershipSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> ForeignCases() =>
        from kind in ForeignWithMaterial
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    public static IEnumerable<object[]> RefusedCases() =>
        from kind in Refused
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    // ---- the owner is checked on the descriptor first ----

    [Theory]
    [MemberData(nameof(ForeignCases))]
    public async Task A_foreign_secret_is_refused_before_its_version_row_or_payload_is_read_and_is_never_unprotected(Kind kind, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, kind);
        var before = await ConnectionSqlHarness.SnapshotAsync(scratch);
        await using var rig = NewResolution(scratch);

        var act = () => rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), OwnerOf(connectionId)).AsTask();

        var thrown = (await act.Should().ThrowAsync<SecretUnavailableException>()).Which;
        thrown.Message.Should().Be(SecretUnavailableException.FixedMessage);
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty("a foreign secret is never decrypted");
        rig.Log.Commands.Should().ContainSingle("only the descriptor is read").Which.Should().Contain("SecretDescriptors");
        rig.Log.Commands.Should().NotContain(c => c.Contains("SecretVersions"), "the version row and its payload are not read for a secret the owner does not own");
        (await ConnectionSqlHarness.SnapshotAsync(scratch)).Should().Be(before, "a refused resolution changes nothing in the store");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public async Task Every_refused_class_throws_the_same_exception_with_a_message_that_holds_no_identifier(Kind kind, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, kind);
        await using var rig = NewResolution(scratch);

        var act = () => rig.Composite.ResolveOwnedAsync(new SecretRef(reference), OwnerOf(connectionId)).AsTask();

        var thrown = (await act.Should().ThrowAsync<SecretUnavailableException>()).Which;
        thrown.Message.Should().Be(SecretUnavailableException.FixedMessage);
        var everything = thrown + thrown.Message;
        if (reference.Length > 0)
            everything.Should().NotContain(reference);
        everything.Should().NotContain(connectionId).And.NotContain("connection:").And.NotContain("platform-").And.NotContain(ForeignMaterial);
        thrown.InnerException.Should().BeNull();
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_case_variants_are_only_refused_because_the_comparison_is_ordinal(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.NameCaseVariant);

        // The control that makes the refusal meaningful: this catalog's own comparison finds the upper-case name equal to the
        // lower-case one. If it did not, the variant would be refused for the wrong reason.
        var equalToCatalog = await scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM SecretDescriptors WHERE Id = '{reference}' AND Name = N'connection:{connectionId}:{Field}';");
        equalToCatalog.Should().Be(1, "the default collation is case insensitive, so only an ordinal check tells these names apart");

        await using var rig = NewResolution(scratch);
        var act = () => rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), OwnerOf(connectionId)).AsTask();
        await act.Should().ThrowAsync<SecretUnavailableException>();
        rig.Protector.UnprotectedWithKeyId.Should().BeEmpty();
    }

    // ---- the connection's own secret resolves as before ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_owned_secret_resolves_with_one_descriptor_read_then_one_version_read_and_the_stored_key_id(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned);
        await scratch.ExecuteAsync($"UPDATE SecretVersions SET KeyId = N'key-from-the-row' WHERE SecretId = '{reference}';");
        await using var rig = NewResolution(scratch);

        var resolved = await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), OwnerOf(connectionId));

        resolved.Material.OpaqueString.Should().Be(OwnedMaterial);
        rig.Protector.UnprotectedWithKeyId.Should().ContainSingle().Which.Should().Be("key-from-the-row", "the protector receives the key id the version row stored");
        rig.Log.Commands.Should().HaveCount(2);
        rig.Log.Commands[0].Should().Contain("SecretDescriptors");
        rig.Log.Commands[1].Should().Contain("SecretVersions");

        // Gate K-2: the owner predicate rides on the descriptor read that already came first, so the check costs no round trip.
        await using var unchecked_ = NewResolution(scratch);
        await unchecked_.Resolver.ResolveAsync(new SecretRef(reference));
        unchecked_.Log.Commands.Should().HaveCount(rig.Log.Commands.Count, "the unchecked path also reads the descriptor and then the version");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_active_version_after_a_rotation_is_the_one_delivered_and_a_pinned_version_is_honoured(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned);
        string firstVersion, secondVersion;
        await using (var db = scratch.NewDb())
        {
            var store = ConnectionSqlHarness.SecretStore(db);
            var id = new SecretId(Guid.Parse(reference));
            firstVersion = (await store.GetAsync(id))!.ActiveVersion!;
            secondVersion = await store.AddVersionAsync(id, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "rotated-material"));
            await store.ActivateVersionAsync(id, secondVersion);
        }

        await using var rig = NewResolution(scratch);
        var owner = OwnerOf(connectionId);

        (await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), owner)).Material.OpaqueString.Should().Be("rotated-material");
        var pinned = await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference, firstVersion), owner);
        pinned.Material.OpaqueString.Should().Be(OwnedMaterial);
        pinned.Version.Should().Be(firstVersion);
        var current = await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference, secondVersion), owner);
        current.Version.Should().Be(secondVersion);
    }

    /// <summary>
    /// The owned call changes only who may be answered. For every state of a secret the connection owns, it must resolve, or
    /// fail, exactly as the unchecked call does.
    /// </summary>
    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task For_a_secret_the_connection_owns_the_owned_call_agrees_with_the_unchecked_one_in_every_lifecycle_state(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var owner = (string connectionId) => OwnerOf(connectionId);

        async Task<(string Id, SecretRef Ref)> SeededAsync(Func<SecretId, SqlSecretStore, Task> arrange, string? pinned = null)
        {
            var connectionId = NewConnectionId();
            var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned);
            await using var db = scratch.NewDb();
            await arrange(new SecretId(Guid.Parse(reference)), ConnectionSqlHarness.SecretStore(db));
            return (connectionId, new SecretRef(reference, pinned));
        }

        var cases = new List<(string Name, (string Id, SecretRef Ref) Seed)>
        {
            ("active", await SeededAsync((_, _) => Task.CompletedTask)),
            ("pinned to an unknown version", await SeededAsync((_, _) => Task.CompletedTask, pinned: "no-such-version")),
            ("disabled", await SeededAsync(async (id, store) => await store.DisableAsync(id))),
            ("active version revoked", await SeededAsync(async (id, store) =>
                await store.RevokeVersionAsync(id, (await store.GetAsync(id))!.ActiveVersion!))),
            ("a second version added but not activated", await SeededAsync(async (id, store) =>
                await store.AddVersionAsync(id, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "never-active")))),
        };

        foreach (var (name, (connectionId, secretRef)) in cases)
        {
            await using var rig = NewResolution(scratch);
            ResolvedSecret? unchecked_ = null;
            ResolvedSecret? owned = null;
            Exception? uncheckedFailure = null, ownedFailure = null;
            try { unchecked_ = await rig.Resolver.ResolveAsync(secretRef); } catch (Exception ex) { uncheckedFailure = ex; }
            try { owned = await rig.Resolver.ResolveOwnedAsync(secretRef, owner(connectionId)); } catch (Exception ex) { ownedFailure = ex; }

            (ownedFailure is null).Should().Be(uncheckedFailure is null, $"[{name}] the owned call must succeed exactly when the unchecked one does");
            if (uncheckedFailure is null)
            {
                owned!.Material.OpaqueString.Should().Be(unchecked_!.Material.OpaqueString, name);
                owned.Version.Should().Be(unchecked_.Version, name);
            }
            else
            {
                ownedFailure.Should().BeAssignableTo<InvalidOperationException>(name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_reactivated_owned_secret_resolves_again_and_ownership_never_changes_under_the_supported_operations(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned);
        var id = new SecretId(Guid.Parse(reference));
        var owner = OwnerOf(connectionId);
        async Task<(object? Name, object? Provider)> StoredOwnerAsync()
        {
            var row = (await scratch.RowsAsync($"SELECT Name, Provider FROM SecretDescriptors WHERE Id = '{reference}';")).Single();
            return (row["Name"], row["Provider"]);
        }

        var original = await StoredOwnerAsync();
        original.Should().Be((owner.Name, owner.Provider));

        await using (var rig = NewResolution(scratch))
        {
            (await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), owner)).Material.OpaqueString.Should().Be(OwnedMaterial);

            // Every operation the store supports on a descriptor: none can change who owns it, so a check made once stays true.
            await using var db = scratch.NewDb();
            var store = ConnectionSqlHarness.SecretStore(db);
            await store.DisableAsync(id);
            (await StoredOwnerAsync()).Should().Be(original);
            await FluentActions.Awaiting(() => rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), owner).AsTask()).Should().ThrowAsync<SecretUnavailableException>();

            var version = await store.AddVersionAsync(id, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "after-reactivation"));
            await store.ActivateVersionAsync(id, version);
            (await StoredOwnerAsync()).Should().Be(original);
            await store.RevokeVersionAsync(id, version);
            (await StoredOwnerAsync()).Should().Be(original);
        }

        await using var again = NewResolution(scratch);
        var viaOldVersion = await again.Resolver.ResolveOwnedAsync(
            new SecretRef(reference, (await scratch.ScalarAsync<string>($"SELECT TOP 1 Version FROM SecretVersions WHERE SecretId = '{reference}' ORDER BY CreatedAt;"))),
            owner);
        viaOldVersion.Material.OpaqueString.Should().Be(OwnedMaterial);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Resolving_while_the_secret_is_rotated_always_delivers_one_complete_version_of_the_owned_secret(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, Kind.Owned);
        var id = new SecretId(Guid.Parse(reference));
        var owner = OwnerOf(connectionId);
        var expected = new HashSet<string> { OwnedMaterial };
        using var stop = new CancellationTokenSource();

        var rotator = Task.Run(async () =>
        {
            for (var i = 0; i < 8; i++)
            {
                var material = $"rotation-{i}";
                expected.Add(material);
                await using var db = scratch.NewDb();
                var store = ConnectionSqlHarness.SecretStore(db);
                var version = await store.AddVersionAsync(id, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: material));
                await store.ActivateVersionAsync(id, version);
            }
        });

        var delivered = new List<string>();
        while (!rotator.IsCompleted)
        {
            await using var rig = NewResolution(scratch);
            try
            {
                delivered.Add((await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), owner)).Material.OpaqueString!);
            }
            catch (SecretUnavailableException)
            {
                // A reader that lands between "descriptor has no active version" and activation may find none. It must never find a foreign one.
            }
        }

        await rotator;
        delivered.Should().OnlyContain(m => expected.Contains(m), "whatever is delivered is a complete version of this connection's own secret");
        delivered.Should().NotContain(ForeignMaterial);
        (await new Func<Task<string?>>(async () =>
        {
            await using var rig = NewResolution(scratch);
            return (await rig.Resolver.ResolveOwnedAsync(new SecretRef(reference), owner)).Material.OpaqueString;
        })()).Should().Be("rotation-7", "after the rotations the last active version is delivered");
    }

    // ---- the pre-existing messages ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_unchecked_resolver_no_longer_echoes_the_reference_in_its_errors(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var dangling = Guid.NewGuid().ToString("D");
        await using var rig = NewResolution(scratch);

        var missing = await FluentActions.Awaiting(() => rig.Resolver.ResolveAsync(new SecretRef(dangling)).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        var unreadable = await FluentActions.Awaiting(() => rig.Resolver.ResolveAsync(new SecretRef("secret-sentinel-text")).AsTask()).Should().ThrowAsync<ArgumentException>();

        (missing.Which.Message + missing.Which).Should().NotContain(dangling);
        (unreadable.Which.Message + unreadable.Which).Should().NotContain("secret-sentinel-text");
    }

    [Fact]
    public void The_refusal_details_are_serializable_without_a_reference_field()
    {
        // The audit schema of the refusal has exactly the field key and the path: nothing that could carry a reference.
        var schemaFields = Apperture.Core.Application.Audit.AuditActionCatalog.Default.Get("connection.secret.resolution_refused").Details!.Fields.Select(f => f.Name);

        schemaFields.Should().Equal("fieldKey", "path");
        JsonSerializer.Serialize(schemaFields).Should().NotContain("ref", "no field is named for a reference");
    }
}
