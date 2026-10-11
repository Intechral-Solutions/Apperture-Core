using System.Reflection;
using Apperture.Core.Application.Audit;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4: the SQL secret store joins a transaction already open on its own context (design gate §22 ruling on the secret
/// store). That makes secret changes atomic with an audited unit only because both resolve the same scoped
/// <see cref="AppertureDbContext"/>. These tests pin that, with the persistence, recorder and secrets registrations the hosts
/// use, so a change to either lifetime cannot silently move secret changes outside the unit.
/// </summary>
public sealed class SecretStoreSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_secret_store_and_the_audited_unit_use_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        var unit = scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>();

        store.Should().BeOfType<SqlSecretStore>();
        unit.Should().BeOfType<SqlAuditedUnitOfWork>();
        Context(store).Should().BeSameAs(Context(unit)).And.BeSameAs(scope.ServiceProvider.GetRequiredService<AppertureDbContext>());
    }

    [Fact]
    public void Separate_scopes_get_separate_contexts()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(first.ServiceProvider.GetRequiredService<ISecretStore>())
            .Should().NotBeSameAs(Context(second.ServiceProvider.GetRequiredService<ISecretStore>()));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // No connection is opened: the tests only compare instances.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddAppertureSqlSecrets(protector: new UnusedProtector());
        return services;
    }

    private static AppertureDbContext Context(object service) =>
        (AppertureDbContext)service.GetType().GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

    private sealed class UnusedProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
