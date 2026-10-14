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

Notes on fidelity: the secret id is read with the server's uniqueidentifier conversion, which accepts the usual
hyphenated, braced and 32-digit forms. A reference only the platform's own parser would accept (a hexadecimal
"X" form) is listed as unreadable here although it would be looked up there; none is written by the product.
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
[Resolved] AS
(
    SELECT
        x.[ConnectionId], x.[ConnectionName], x.[ConnectionState], x.[FieldKey], x.[RefJsonType],
        g.[SecretGuid],
        d.[Id]            AS [DescriptorId],
        d.[Provider]      AS [DescriptorProvider],
        d.[Name]          AS [DescriptorName],
        d.[State]         AS [DescriptorState],
        d.[ActiveVersion] AS [DescriptorActiveVersion]
    FROM [Fields] AS x
    CROSS APPLY (SELECT TRY_CONVERT(uniqueidentifier, NULLIF(LTRIM(RTRIM(x.[RefValue])), N'')) AS [SecretGuid]) AS g
    LEFT JOIN [dbo].[SecretDescriptors] AS d ON d.[Id] = g.[SecretGuid]
),
[Classified] AS
(
    SELECT
        r.[ConnectionId], r.[ConnectionName], r.[ConnectionState], r.[FieldKey],
        r.[DescriptorProvider], r.[DescriptorName],
        CASE
            WHEN r.[RefJsonType] <> 1 OR r.[SecretGuid] IS NULL THEN N'unreadable'
            WHEN r.[DescriptorId] IS NULL THEN N'dangling'
            WHEN r.[DescriptorProvider] COLLATE Latin1_General_100_BIN2 <> N'platform-connection' COLLATE Latin1_General_100_BIN2
                THEN N'foreign_provider'
            WHEN r.[DescriptorName] COLLATE Latin1_General_100_BIN2
                 <> (N'connection:' + r.[ConnectionId] + N':' + r.[FieldKey]) COLLATE Latin1_General_100_BIN2
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
