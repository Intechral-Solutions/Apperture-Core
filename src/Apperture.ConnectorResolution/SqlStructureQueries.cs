using System.Data;

namespace Apperture.ConnectorResolution;

/// <summary>
/// The CLOSED set of statements the SQL structure browser can ever send. Each is a fixed, platform-authored literal that reads
/// <c>INFORMATION_SCHEMA</c> metadata; every operator-supplied selection (schema, table) reaches the server ONLY as a
/// bound <see cref="IDbDataParameter"/> VALUE compared against a metadata COLUMN. Nothing is ever concatenated,
/// interpolated or quoted into command text, and there is no dynamic SQL: no <c>sp_executesql</c>, no <c>EXEC</c>, no
/// <c>QUOTENAME</c>, no identifier insertion. There is no statement that reads rows, and none that writes or changes anything.
/// <para>
/// If a future metadata operation seems to need an identifier inside SQL text, that is a design and safety finding to be
/// raised, never a reason to widen this class.
/// </para>
/// </summary>
internal static class SqlStructureQueries
{
    public const string LimitParameter = "@limit";
    public const string SchemaParameter = "@schema";
    public const string TableParameter = "@table";

    /// <summary>SQL Server identifiers (<c>sysname</c>) are at most 128 characters; anything longer cannot name a real object.</summary>
    public const int MaxIdentifierLength = Apperture.Core.Application.ConnectionTools.SqlStructureLimits.MaxIdentifierLength;

    // The fixed system schemas are a LITERAL list in the statement (platform-authored), not an operator input.
    public const string Schemas =
        "SELECT TOP (@limit) [SCHEMA_NAME] " +
        "FROM INFORMATION_SCHEMA.SCHEMATA " +
        "WHERE [SCHEMA_NAME] NOT IN (N'sys', N'INFORMATION_SCHEMA', N'guest', N'db_owner', N'db_accessadmin', N'db_securityadmin', " +
        "N'db_ddladmin', N'db_backupoperator', N'db_datareader', N'db_datawriter', N'db_denydatareader', N'db_denydatawriter') " +
        "ORDER BY [SCHEMA_NAME]";

    public const string Tables =
        "SELECT TOP (@limit) TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE " +
        "FROM INFORMATION_SCHEMA.TABLES " +
        "WHERE TABLE_SCHEMA = @schema " +
        "ORDER BY TABLE_NAME";

    public const string Columns =
        "SELECT TOP (@limit) COLUMN_NAME, DATA_TYPE, IS_NULLABLE, ORDINAL_POSITION, CHARACTER_MAXIMUM_LENGTH " +
        "FROM INFORMATION_SCHEMA.COLUMNS " +
        "WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table " +
        "ORDER BY ORDINAL_POSITION";

    /// <summary>Every statement this feature can issue. A test asserts this is the whole set and that nothing else is ever sent.</summary>
    public static IReadOnlyList<string> All { get; } = [Schemas, Tables, Columns];
}
