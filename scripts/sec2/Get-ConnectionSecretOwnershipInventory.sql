/*
================================================================================================================
Connection secret ownership inventory (read-only)

Run it once against the INSTANCE database of every PERSISTENT installation that existed before the connection
secret ownership check shipped, before upgrading, and again after. It writes nothing, creates nothing (no temporary
table, no procedure) and needs only read access to three tables: dbo.Connections, dbo.SecretDescriptors and
dbo.SecretVersions (the db_datareader role is enough). It needs database compatibility level 130 or higher (it uses
OPENJSON and ISJSON). It has no GO separators, so it runs as one batch from sqlcmd, SSMS or a client library.

    sqlcmd -S <server> -d <instance database> -E -i Get-ConnectionSecretOwnershipInventory.sql -W -s "|"

A database that was deliberately discarded and reseeded needs no run. Take the database names from the deployment
records of each installation; this script names none.

What it lists
-------------
Each row is one secret field of one connection whose stored reference the platform will now REFUSE to resolve, and the
class of the problem. A field whose reference names a secret that field owns is not listed, so an empty result means
every connection is clean. The platform's rule, which this script applies in the same way (exact, case-sensitive
comparison of both parts): a connection field owns the secret stored under provider 'platform-connection' with the
name 'connection:{connectionId}:{fieldKey}', and that secret must also be usable (not disabled, with an active
version).

  Class             Meaning                                                       What to do
  ----------------  ------------------------------------------------------------  --------------------------------
  dangling          No such secret, or it is disabled or has no active version.   Clear the field, or re-enter its
                    The connection already fails today.                           value (PATCH secretValues).
  unreadable        The reference is blank, null, not text, not a secret id, or   Clear the field (PATCH
                    the whole reference map is not a JSON object (FieldKey is     clearSecretFields). If the whole
                    NULL for that case).                                          map is unreadable, repair the row.
  check_by_hand     The reference is written in the hexadecimal GUID form         Decode it and look the secret up
                    ({0x...,0x...,0x...,{0x...}}), which this script does not     by hand; treat it as foreign_owner
                    decode. The product never wrote it.                           until shown otherwise.
  foreign_provider  The reference names a secret of another provider (a setting    INCIDENT. Clear or re-enter the
                    or an Entra secret).                                          field. If the connection was ever
                                                                                  tested, browsed or bound, rotate
                                                                                  the credential it names.
  foreign_owner     The reference names a platform-connection secret that         INCIDENT, as above. Rotate the
                    belongs to another connection or another field.               other connection's credential.

For the two foreign classes the row also shows the provider and the NAME of the secret that was named (for example
'connection:ab12...:Password' or 'platform-setting:...'), because that tells you whose credential may have been sent.
The name identifies an owner; it is not the secret.

What it never shows
-------------------
The stored reference, the secret's id, any version, any payload and any secret material. The script does not select
dbo.SecretVersions.ProtectedPayload at all.

Repairing a row does not need the reference. In the Management UI or API, give the field a new value (secretValues) to
store a secret the connection owns and repoint the field, or clear the field (clearSecretFields). Neither reads,
rotates, disables or otherwise changes the secret the old reference named.

Notes on fidelity: the platform reads a reference with .NET's Guid.TryParse, which trims surrounding whitespace and
accepts the hyphenated form, the same in braces or in parentheses, and 32 digits without hyphens. This script
recognizes exactly those by explicit pattern (SQL Server's own conversion does not: it rejects the 32-digit and
parenthesised forms and padding, and it ignores text after a GUID) and reports anything else as unreadable, so
a reference the platform would have resolved is never mislabelled as clear-and-forget. Only the hexadecimal form is left
to a person (check_by_hand). Whitespace trimming covers space, tab, carriage return and line feed.
Name and provider are compared exactly, trailing spaces included.
================================================================================================================
*/
SET NOCOUNT ON;

WITH [Maps] AS
(
    -- One row per connection, with whether its reference map is a readable JSON object.
    SELECT
        c.[Id]            AS [ConnectionId],
        c.[Name]          AS [ConnectionName],
        c.[State]         AS [ConnectionState],
        c.[SecretRefsJson] AS [Json],
        CASE WHEN ISJSON(c.[SecretRefsJson]) = 1 AND LEFT(LTRIM(c.[SecretRefsJson]), 1) = N'{' THEN 1 ELSE 0 END AS [IsObject]
    FROM [dbo].[Connections] AS c
),
[Fields] AS
(
    -- One row per secret field stored on a readable map. [type] 1 is a JSON string; anything else cannot be a reference.
    SELECT
        m.[ConnectionId], m.[ConnectionName], m.[ConnectionState],
        f.[key]   AS [FieldKey],
        f.[value] AS [RefValue],
        f.[type]  AS [RefJsonType]
    FROM [Maps] AS m
    CROSS APPLY OPENJSON(CASE WHEN m.[IsObject] = 1 THEN m.[Json] ELSE N'{}' END) AS f
    WHERE m.[IsObject] = 1
),
[Trimmed] AS
(
    -- The platform parses a reference with .NET's Guid.TryParse, which trims whitespace and accepts only the forms below.
    -- SQL Server's own conversion does not match it (it rejects the 32-digit and parenthesised forms and whitespace padding,
    -- and it silently ignores text after a GUID), so the text is checked against explicit patterns first.
    SELECT
        x.[ConnectionId], x.[ConnectionName], x.[ConnectionState], x.[FieldKey], x.[RefJsonType],
        LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(x.[RefValue], NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '))) AS [T]
    FROM [Fields] AS x
),
[Canonical] AS
(
    SELECT
        t.[ConnectionId], t.[ConnectionName], t.[ConnectionState], t.[FieldKey], t.[RefJsonType],
        CASE
            WHEN t.[T] LIKE p.[Dashed]                                   THEN t.[T]                     -- D: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            WHEN t.[T] LIKE N'{' + p.[Dashed] + N'}'                     THEN SUBSTRING(t.[T], 2, 36)   -- B: {D}
            WHEN t.[T] LIKE N'(' + p.[Dashed] + N')'                     THEN SUBSTRING(t.[T], 2, 36)   -- P: (D)
            WHEN LEN(t.[T]) = 32 AND t.[T] LIKE REPLICATE(p.[Hex], 32)   -- N: 32 digits
                THEN STUFF(STUFF(STUFF(STUFF(t.[T], 21, 0, N'-'), 17, 0, N'-'), 13, 0, N'-'), 9, 0, N'-')
        END AS [Canon],
        CASE WHEN t.[T] LIKE N'{0x%' THEN 1 ELSE 0 END AS [IsHexForm]    -- X: {0x...,0x...,0x...,{0x...}}; not parsed here
    FROM [Trimmed] AS t
    CROSS APPLY
    (
        SELECT
            N'[0-9A-Fa-f]' AS [Hex],
            REPLICATE(N'[0-9A-Fa-f]', 8) + N'-' + REPLICATE(N'[0-9A-Fa-f]', 4) + N'-' + REPLICATE(N'[0-9A-Fa-f]', 4) + N'-'
                + REPLICATE(N'[0-9A-Fa-f]', 4) + N'-' + REPLICATE(N'[0-9A-Fa-f]', 12) AS [Dashed]
    ) AS p
),
[Resolved] AS
(
    SELECT
        c.[ConnectionId], c.[ConnectionName], c.[ConnectionState], c.[FieldKey], c.[RefJsonType], c.[IsHexForm],
        g.[SecretGuid],
        N'connection:' + c.[ConnectionId] + N':' + c.[FieldKey] AS [ExpectedName],
        d.[Id]            AS [DescriptorId],
        d.[Provider]      AS [DescriptorProvider],
        d.[Name]          AS [DescriptorName],
        d.[State]         AS [DescriptorState],
        d.[ActiveVersion] AS [DescriptorActiveVersion]
    FROM [Canonical] AS c
    CROSS APPLY (SELECT TRY_CONVERT(uniqueidentifier, c.[Canon]) AS [SecretGuid]) AS g
    LEFT JOIN [dbo].[SecretDescriptors] AS d ON d.[Id] = g.[SecretGuid]
),
[Classified] AS
(
    SELECT
        r.[ConnectionId], r.[ConnectionName], r.[ConnectionState], r.[FieldKey],
        r.[DescriptorProvider], r.[DescriptorName],
        CASE
            WHEN r.[RefJsonType] <> 1 THEN N'unreadable'
            WHEN r.[IsHexForm] = 1 THEN N'check_by_hand'
            WHEN r.[SecretGuid] IS NULL THEN N'unreadable'
            WHEN r.[DescriptorId] IS NULL THEN N'dangling'
            -- Exact comparison. BIN2 makes it case sensitive; the DATALENGTH test catches a difference in trailing spaces,
            -- which the comparison operators otherwise ignore.
            WHEN r.[DescriptorProvider] COLLATE Latin1_General_100_BIN2 <> N'platform-connection' COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(r.[DescriptorProvider]) <> DATALENGTH(N'platform-connection')
                THEN N'foreign_provider'
            WHEN r.[DescriptorName] COLLATE Latin1_General_100_BIN2 <> r.[ExpectedName] COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(r.[DescriptorName]) <> DATALENGTH(r.[ExpectedName])
                THEN N'foreign_owner'
            WHEN r.[DescriptorState] = 2 /* Disabled */
              OR NULLIF(r.[DescriptorActiveVersion], N'') IS NULL
              OR NOT EXISTS
                 (
                     SELECT 1
                     FROM [dbo].[SecretVersions] AS v
                     WHERE v.[SecretId] = r.[DescriptorId]
                       AND v.[Version]  = r.[DescriptorActiveVersion]
                       AND v.[State]    = 1 /* Active */
                 )
                THEN N'dangling'
            ELSE N'owned'
        END AS [Class]
    FROM [Resolved] AS r
)
SELECT
    k.[ConnectionId],
    k.[ConnectionName],
    CASE k.[ConnectionState] WHEN 0 THEN N'Active' WHEN 1 THEN N'Retired' ELSE CONVERT(nvarchar(20), k.[ConnectionState]) END AS [ConnectionState],
    k.[FieldKey],
    k.[Class],
    CASE WHEN k.[Class] IN (N'foreign_provider', N'foreign_owner') THEN k.[DescriptorProvider] END AS [ForeignProvider],
    CASE WHEN k.[Class] IN (N'foreign_provider', N'foreign_owner') THEN k.[DescriptorName] END     AS [ForeignOwnerName]
FROM [Classified] AS k
WHERE k.[Class] <> N'owned'

UNION ALL

-- A reference map that is not a JSON object at all: the connection cannot be read, so it has no field to name.
SELECT
    m.[ConnectionId],
    m.[ConnectionName],
    CASE m.[ConnectionState] WHEN 0 THEN N'Active' WHEN 1 THEN N'Retired' ELSE CONVERT(nvarchar(20), m.[ConnectionState]) END,
    CAST(NULL AS nvarchar(4000)),
    N'unreadable',
    CAST(NULL AS nvarchar(128)),
    CAST(NULL AS nvarchar(256))
FROM [Maps] AS m
WHERE m.[IsObject] = 0 AND m.[Json] IS NOT NULL

ORDER BY [ConnectionId], [FieldKey], [Class];
