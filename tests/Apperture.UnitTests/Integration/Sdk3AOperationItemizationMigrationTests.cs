using System.Text.RegularExpressions;
using Apperture.Persistence.SqlServer.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Guards against SQL Server invalid column types in hand-authored platform migrations.
/// SQL Server allows nvarchar(n) only for n &lt;= 4000; larger logical caps must use nvarchar(max).
/// </summary>
public sealed class Sdk3AOperationItemizationMigrationTests
{
    private static readonly Regex NvarcharSized = new(
        @"^nvarchar\((\d+)\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void Sdk3A_up_does_not_use_nvarchar_sizes_above_4000()
    {
        var migration = new TestableSdk3AMigration();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.RunUp(builder);

        var invalid = builder.Operations
            .OfType<CreateTableOperation>()
            .SelectMany(t => t.Columns.Select(c => (Table: t.Name, Column: c)))
            .Select(x =>
            {
                var type = x.Column.ColumnType ?? string.Empty;
                var match = NvarcharSized.Match(type);
                var size = match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
                return (x.Table, x.Column.Name, ColumnType: type, Size: size);
            })
            .Where(x => x.Size is > 4000)
            .ToList();

        invalid.Should().BeEmpty(
            "SQL Server rejects nvarchar(n) when n > 4000 (error 2717); use nvarchar(max) instead. Invalid: {0}",
            string.Join(", ", invalid.Select(x => $"{x.Table}.{x.Name}={x.ColumnType}")));
    }

    [Fact]
    public void Sdk3A_MetadataJson_columns_use_nvarchar_max()
    {
        var migration = new TestableSdk3AMigration();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.RunUp(builder);

        var metadataColumns = builder.Operations
            .OfType<CreateTableOperation>()
            .SelectMany(t => t.Columns.Select(c => (Table: t.Name, Column: c)))
            .Where(x => x.Column.Name == "MetadataJson")
            .ToList();

        metadataColumns.Should().HaveCount(2);
        metadataColumns.Should().OnlyContain(x =>
            string.Equals(x.Column.ColumnType, "nvarchar(max)", StringComparison.OrdinalIgnoreCase));
        metadataColumns.Should().OnlyContain(x => x.Column.MaxLength == 4096);
    }

    private sealed class TestableSdk3AMigration : Sdk3A_OperationItemization
    {
        public void RunUp(MigrationBuilder builder) => base.Up(builder);
    }
}
