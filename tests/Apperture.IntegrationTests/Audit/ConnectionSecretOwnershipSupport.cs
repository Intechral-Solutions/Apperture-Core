using System.Collections.Concurrent;
using System.Data.Common;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Apperture.IntegrationTests.Audit;

/// <summary>Fixtures for the SEC-2.2 connection secret ownership suites: poisoned references of every class, and the recorders that prove what was and was not touched.</summary>
public static class ConnectionSecretOwnershipSupport
{
    /// <summary>Material of a secret that belongs to someone else. It must never be unprotected, returned, sent or logged.</summary>
    internal const string ForeignMaterial = "foreign-material-sentinel";

    /// <summary>Material of the connection's own secret.</summary>
    internal const string OwnedMaterial = "owned-material-sentinel";

    internal const string Field = "Password";

    /// <summary>The ways a connection field's stored reference can fail to name a secret that field owns.</summary>
    public enum Kind
    {
        /// <summary>The control: the connection's own secret.</summary>
        Owned,

        /// <summary>Another connection's secret for the same field.</summary>
        OtherConnection,

        /// <summary>This connection's own secret, for another field.</summary>
        OtherField,

        /// <summary>A settings secret (another provider).</summary>
        OtherProvider,

        /// <summary>The descriptor name differs from the owner's only in case. The catalog's collation may treat them as equal.</summary>
        NameCaseVariant,

        /// <summary>The descriptor provider differs from the owner's only in case.</summary>
        ProviderCaseVariant,

        /// <summary>A well-formed reference to no descriptor.</summary>
        Dangling,

        /// <summary>Not a GUID.</summary>
        Unreadable,

        /// <summary>A Key Vault style reference, which no SQL resolver handles.</summary>
        KeyVaultStyle,

        /// <summary>A blank stored reference.</summary>
        Blank,

        /// <summary>The connection's own secret, disabled.</summary>
        DisabledOwned,

        /// <summary>The connection's own secret with a version that was never activated.</summary>
        PendingOnly,
    }

    /// <summary>The kinds that name another holder's live secret: material exists, and must stay unread.</summary>
    internal static readonly Kind[] ForeignWithMaterial =
        [Kind.OtherConnection, Kind.OtherField, Kind.OtherProvider, Kind.NameCaseVariant, Kind.ProviderCaseVariant];

    /// <summary>Every kind that must be refused.</summary>
    internal static readonly Kind[] Refused =
        [.. ForeignWithMaterial, Kind.Dangling, Kind.Unreadable, Kind.KeyVaultStyle, Kind.Blank, Kind.DisabledOwned, Kind.PendingOnly];

    internal static string NewConnectionId() => "con-" + Guid.NewGuid().ToString("N")[..12];

    internal static SecretOwner OwnerOf(string connectionId, string field = Field) =>
        ConnectionSecretOwnership.For(new ConnectionId(connectionId), field);

    /// <summary>
    /// Seeds whatever <paramref name="kind"/> needs in the real store and returns the reference text a connection row would hold.
    /// Seeding goes through the store directly, so it writes no audit row.
    /// </summary>
    internal static async Task<string> SeedReferenceAsync(AuditSqlScratch scratch, string connectionId, Kind kind, string field = Field)
    {
        var own = $"connection:{connectionId}:{field}";
        switch (kind)
        {
            case Kind.Owned:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, ConnectionSecretOwnership.Provider, own, OwnedMaterial);
            case Kind.OtherConnection:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, ConnectionSecretOwnership.Provider, $"connection:{NewConnectionId()}:{field}", ForeignMaterial);
            case Kind.OtherField:
                // This connection's own secret, but for a different field than the one that will name it.
                return await ConnectionSqlHarness.SeedSecretAsync(
                    scratch, ConnectionSecretOwnership.Provider, $"connection:{connectionId}:{(field == "ApiKey" ? Field : "ApiKey")}", ForeignMaterial);
            case Kind.OtherProvider:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, "platform-setting", "platform-setting:notifications.smtp.password", ForeignMaterial);
            case Kind.NameCaseVariant:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, ConnectionSecretOwnership.Provider, own.ToUpperInvariant(), ForeignMaterial);
            case Kind.ProviderCaseVariant:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, "Platform-Connection", own, ForeignMaterial);
            case Kind.Dangling:
                return Guid.NewGuid().ToString("D");
            case Kind.Unreadable:
                return "not-a-guid";
            case Kind.KeyVaultStyle:
                return "kv:some-vault-secret";
            case Kind.Blank:
                return "";
            case Kind.DisabledOwned:
            {
                var reference = await ConnectionSqlHarness.SeedSecretAsync(scratch, ConnectionSecretOwnership.Provider, own, OwnedMaterial);
                await using var db = scratch.NewDb();
                await ConnectionSqlHarness.SecretStore(db).DisableAsync(new SecretId(Guid.Parse(reference)));
                return reference;
            }

            case Kind.PendingOnly:
            {
                await using var db = scratch.NewDb();
                var store = ConnectionSqlHarness.SecretStore(db);
                var descriptor = await store.CreateAsync(new SecretCreateRequest(own, ConnectionSecretOwnership.Provider, SecretMaterialType.OpaqueString));
                await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: OwnedMaterial));
                return descriptor.SecretId.Value.ToString("D");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>Counts and records every unprotect, with the key id it was handed. Protect is a pass-through.</summary>
    internal sealed class RecordingProtector : ISecretProtector
    {
        public ConcurrentQueue<string> UnprotectedWithKeyId { get; } = new();

        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
        {
            UnprotectedWithKeyId.Enqueue(keyId);
            return ValueTask.FromResult(protectedPayload.ToArray());
        }
    }

    /// <summary>Records the text of every query a context executes, in order.</summary>
    internal sealed class CommandLog : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => [.. _commands];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }
    }

    internal sealed class Resolution(AppertureDbContext db, RecordingProtector protector, CommandLog log) : IAsyncDisposable
    {
        public RecordingProtector Protector { get; } = protector;

        public CommandLog Log { get; } = log;

        public SqlSecretStore Store { get; } = new(db, protector, new DefaultSecretAuditActorProvider());

        public SqlSecretResolver Resolver { get; } = new(new SqlSecretStore(db, protector, new DefaultSecretAuditActorProvider()), protector);

        public ISecretResolver Composite { get; } =
            new CompositeSecretResolver(new SqlSecretResolverRegistry(new SqlSecretResolver(new SqlSecretStore(db, protector, new DefaultSecretAuditActorProvider()), protector)));

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    /// <summary>A context whose every query is logged, with a protector that records what it is asked to unprotect.</summary>
    internal static Resolution NewResolution(AuditSqlScratch scratch)
    {
        var log = new CommandLog();
        return new Resolution(scratch.NewDb(log), new RecordingProtector(), log);
    }
}
