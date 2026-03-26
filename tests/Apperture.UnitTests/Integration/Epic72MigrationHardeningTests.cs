using Apperture.Persistence.SqlServer.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Linq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class Epic72MigrationHardeningTests
{
    [Fact]
    public void Epic72_up_contains_data_copy_before_legacy_drop()
    {
        var migration = new TestableEpic72Migration();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        migration.RunUp(builder);

        var sqlOpIndex = builder.Operations
            .Select((op, idx) => (op, idx))
            .First(x => x.op is SqlOperation).idx;
        var legacyDropIndex = builder.Operations
            .Select((op, idx) => (op, idx))
            .First(x => x.op is DropTableOperation dt && dt.Name == "AttentionContactGroupMembers").idx;

        sqlOpIndex.Should().BeLessThan(legacyDropIndex);

        var sql = ((SqlOperation)builder.Operations[sqlOpIndex]).Sql;
        sql.Should().Contain("INSERT INTO OperationalTeams");
        sql.Should().Contain("INSERT INTO OperationalTeamMembers");
        sql.Should().Contain("FROM AttentionContactGroups");
        sql.Should().Contain("FROM AttentionContactGroupMembers");
    }

    private sealed class TestableEpic72Migration : Epic72_OperationalTeamsPrincipalRouting
    {
        public void RunUp(MigrationBuilder builder) => base.Up(builder);
    }
}
