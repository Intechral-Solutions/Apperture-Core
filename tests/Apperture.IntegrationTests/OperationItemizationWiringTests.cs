using Apperture.Core.Application.OperationItems;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Operation-itemization service wiring. It resolves the registered stores and never opens a connection, so it needs no SQL Server
/// and is not a SQL test: it lives apart from <see cref="OperationItemizationSqlServerTests"/>, whose cases all run against SQL
/// Server, so that no case in a SQL-named class is a fast pass that touches nothing.
/// </summary>
public sealed class OperationItemizationWiringTests
{
    [Fact]
    public async Task AddOperationItemizationPersistence_registers_Sql_store()
    {
        var cs = SqlTestExecution.NonConnectingConnectionString;

        var services = new ServiceCollection();
        services.AddApperturePersistence(cs);
        services.AddOperationItemizationPersistence();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IOperationItemStore>();
        store.Should().BeOfType<SqlOperationItemStore>();
        scope.ServiceProvider.GetRequiredService<IOperationItemRetryAttemptStore>()
            .Should().BeOfType<SqlOperationItemRetryAttemptStore>();
        scope.ServiceProvider.GetRequiredService<IOperationItemRetryReconciliationService>()
            .Should().BeOfType<OperationItemRetryReconciliationService>();
    }
}
