/*
================================================================================================================
Setting secret ownership inventory (read-only)

Run it once against the INSTANCE database of every PERSISTENT installation that existed before the setting secret
ownership check shipped, before upgrading, and again after. It writes nothing, creates nothing (no table, temporary
table or procedure) and needs only read access to five tables: dbo.PlatformSettingValues, dbo.SecretDescriptors,
dbo.SecretVersions, dbo.Connections and dbo.EntraInstanceRegistrations (the db_datareader role is enough). It needs
database compatibility level 130 or higher (it uses OPENJSON and ISJSON). It has no GO separators, so it runs as one
batch from sqlcmd, SSMS or a client library.

    sqlcmd -S <server> -d <instance database> -E -i Get-SettingSecretOwnershipInventory.sql -W -s "|"

A database that was deliberately discarded and reseeded needs no run. Take the database names from the deployment
records of each installation; this script names none.

Before you run it: list the sensitive setting keys
---------------------------------------------------
The database does not record which settings are credentials; the platform and each module declare that in code. The
script therefore classifies every setting row by what the secret store says about the secret the row's value names,
which needs no declaration, and uses the list at the top of the query only to decide which rows MUST name a secret. Add
the keys of the sensitive settings your modules declare (the setting's Key, one line each) before running. The platform's
own Notifications.Email.SmtpPasswordRef is already listed. A key that is missing from the list is never hidden: a row whose
value names a secret is still listed, as ordinary_guid_text, with the relation the secret has to the row.

The rule the platform now applies (exact, case-sensitive comparison of both parts)
----------------------------------------------------------------------------------
  Instance setting K              owns provider 'platform-setting',    name 'platform-setting:K'
  Setting K of integration I      owns provider 'integration-setting', name 'integration-setting:I:K'
                                  where, in I and in K, '%' is written '%25' and ':' is written '%3A'
                                  (ids and keys that hold neither character are written as they are)
and the secret must also be usable (not disabled or revoked, with an active version). A reference to any other secret is
REFUSED at read, never versioned or disabled by the setting, and never adopted.

What it lists
-------------
Each row is one setting row whose stored value needs attention. A row that names the secret it owns is not listed, so an
empty result means every setting is clean.

  Class              Meaning                                                   What to do
  -----------------  --------------------------------------------------------  -------------------------------------
  legacy_integration An integration row names a secret in the old shape        The row is refused at read. Re-enter
                     (provider 'platform-setting', name 'platform-setting:K'), the value (PUT the setting with a
                     which cannot say which integration made it. It is never   secret replace). That creates a secret
                     adopted or renamed. SoleHolder says whether any other     the row owns and repoints it. The
                     row, connection or Entra registration also names it.      legacy secret is retired only when
                                                                               SoleHolder was 1 (H1's evidence);
                                                                               otherwise it is retained until the
                                                                               last holder has moved.
  shared             The row owns its secret but another row, connection or    Find the other holder in the columns
                     Entra registration also names it.                         OtherSettingRows, OtherConnections,
                                                                               OtherEntraRegistrations. Each holder
                                                                               re-enters its value; nothing is
                                                                               adopted and the secret is retained.
  key_mismatch       The row names a secret made for another setting, another  Refused at read. The foreign secret is
                     integration or another scope (provider 'platform-setting' never modified. Re-enter the value.
                     or 'integration-setting' with a name that is not this
                     row's owner or the legacy shape).
  foreign_provider   The row names a secret of another provider (a connection  INCIDENT: a module may have read another
                     or an Entra secret).                                      holder's credential. Re-enter the value
                                                                               and rotate the credential the secret
                                                                               holds.
  ordinary_guid_text A key that is not in the sensitive list holds text that   Reported only. Harmless until a module
                     names a usable secret. Relation says how the secret       declares the key sensitive, and
                     relates to the row.                                       refused at read after that. If the key
                                                                               IS sensitive, add it to the list.
  dangling           A listed sensitive key names no secret, or one that is    Re-enter the value (a replace creates
                     disabled or has no active version. It already reads as    a new owned secret), or clear the
                     not configured.                                           setting.
  unreadable         A listed sensitive key holds a value that is not a        Clear the setting, or re-enter the
                     readable secret reference (blank, not text, not a GUID).  value.

For the classes that name another holder's secret the row also shows the provider and the NAME of the secret that was
named, because that tells you whose credential may have been read. The name identifies an owner; it is not the secret.

What it never shows
-------------------
The stored reference, the secret's id, any version, any payload and any secret material. The script does not select
dbo.SecretVersions.ProtectedPayload at all.

Notes on fidelity: the platform reads a reference with .NET's Guid.TryParse, which trims surrounding whitespace and accepts
the hyphenated form, the same in braces or in parentheses, and 32 digits without hyphens. This script recognizes exactly
those by explicit pattern and reports anything else as unreadable (sensitive keys) or ignores it (other keys). Whitespace
trimming covers space, tab, carriage return and line feed; the platform trims every Unicode space (a non-breaking space,
say), so a reference padded with one of those that the platform would still resolve is reported as unreadable here. That
errs towards over-reporting and never hides a row. The
hexadecimal GUID form is not decoded; the product never wrote it. Holders are found by searching the other rows' text for
the secret's hyphenated and 32-digit forms, which finds every spelling of a reference except the hexadecimal one. Name and
provider are compared exactly, trailing spaces included. A value that is an array or an object is not a reference to one
secret: a listed sensitive key with such a value is unreadable, and any other key is ignored.
An instance setting whose row names a secret in the old integration shape for the same key cannot be told from its own
secret (the two have the same provider and name, by design); it is not listed.
================================================================================================================
*/
SET NOCOUNT ON;

WITH [SensitiveKeys] ([Key]) AS
(
    SELECT k.[Key]
    FROM (VALUES
        (N'Notifications.Email.SmtpPasswordRef')
        -- Add the sensitive keys your modules declare here, one per line, each line starting with a comma:
        -- , (N'<module setting key>')
    ) AS k ([Key])
),
[Rows] AS
(
    SELECT
        v.[Id]        AS [RowId],
        v.[ScopeKind],
        v.[ScopeId],
        v.[Key]       AS [SettingKey],
        -- SQL Server's JSON functions accept only an object or an array at the root, so the stored value is wrapped.
        N'{"v":' + v.[ValueJson] + N'}' AS [Wrapped]
    FROM [dbo].[PlatformSettingValues] AS v
),
[Typed] AS
(
    SELECT
        r.[RowId], r.[ScopeKind], r.[ScopeId], r.[SettingKey],
        CASE WHEN ISJSON(r.[Wrapped]) = 1 THEN 1 ELSE 0 END AS [IsValidJson],
        j.[type]  AS [JsonType],   -- 1 is a JSON string; anything else cannot be a reference
        j.[value] AS [JsonValue]
    FROM [Rows] AS r
    OUTER APPLY
    (
        SELECT TOP (1) o.[type], o.[value]
        FROM OPENJSON(CASE WHEN ISJSON(r.[Wrapped]) = 1 THEN r.[Wrapped] ELSE N'{}' END) AS o
    ) AS j
),
[Trimmed] AS
(
    SELECT
        t.[RowId], t.[ScopeKind], t.[ScopeId], t.[SettingKey], t.[IsValidJson], t.[JsonType],
        LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(CASE WHEN t.[JsonType] = 1 THEN t.[JsonValue] END, NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '))) AS [T]
    FROM [Typed] AS t
),
[Canonical] AS
(
    SELECT
        t.[RowId], t.[ScopeKind], t.[ScopeId], t.[SettingKey], t.[IsValidJson], t.[JsonType], t.[T],
        CASE
            WHEN t.[T] LIKE p.[Dashed]                                   THEN t.[T]                     -- D
            WHEN t.[T] LIKE N'{' + p.[Dashed] + N'}'                     THEN SUBSTRING(t.[T], 2, 36)   -- B
            WHEN t.[T] LIKE N'(' + p.[Dashed] + N')'                     THEN SUBSTRING(t.[T], 2, 36)   -- P
            WHEN LEN(t.[T]) = 32 AND t.[T] LIKE REPLICATE(p.[Hex], 32)   -- N
                THEN STUFF(STUFF(STUFF(STUFF(t.[T], 21, 0, N'-'), 17, 0, N'-'), 13, 0, N'-'), 9, 0, N'-')
        END AS [Canon]
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
        c.[RowId], c.[ScopeKind], c.[ScopeId], c.[SettingKey], c.[IsValidJson], c.[JsonType],
        g.[SecretGuid],
        CASE WHEN EXISTS (SELECT 1 FROM [SensitiveKeys] AS s WHERE s.[Key] COLLATE Latin1_General_100_BIN2 = c.[SettingKey] COLLATE Latin1_General_100_BIN2) THEN 1 ELSE 0 END AS [Sensitive],
        -- The owner this row's setting defines. Escape '%' first, then ':'.
        CASE
            WHEN c.[ScopeKind] = N'Instance' THEN N'platform-setting'
            WHEN c.[ScopeKind] = N'Integration' THEN N'integration-setting'
        END AS [ExpectedProvider],
        CASE
            WHEN c.[ScopeKind] = N'Instance' THEN N'platform-setting:' + c.[SettingKey]
            WHEN c.[ScopeKind] = N'Integration'
                THEN N'integration-setting:' + REPLACE(REPLACE(c.[ScopeId], N'%', N'%25'), N':', N'%3A')
                     + N':' + REPLACE(REPLACE(c.[SettingKey], N'%', N'%25'), N':', N'%3A')
        END AS [ExpectedName],
        N'platform-setting:' + c.[SettingKey] AS [LegacyName],
        REPLACE(CONVERT(nvarchar(36), g.[SecretGuid]), N'-', N'') AS [Plain],
        CONVERT(nvarchar(36), g.[SecretGuid]) AS [Dashed],
        d.[Id]            AS [DescriptorId],
        d.[Provider]      AS [DescriptorProvider],
        d.[Name]          AS [DescriptorName],
        d.[State]         AS [DescriptorState],
        d.[ActiveVersion] AS [DescriptorActiveVersion]
    FROM [Canonical] AS c
    CROSS APPLY (SELECT TRY_CONVERT(uniqueidentifier, c.[Canon]) AS [SecretGuid]) AS g
    LEFT JOIN [dbo].[SecretDescriptors] AS d ON d.[Id] = g.[SecretGuid]
),
[Related] AS
(
    SELECT
        r.*,
        -- Exact comparison. BIN2 makes it case sensitive; the DATALENGTH test catches a difference in trailing spaces, which the
        -- comparison operators otherwise ignore.
        CASE WHEN r.[DescriptorId] IS NULL THEN 0
             WHEN r.[ExpectedProvider] IS NULL THEN 0
             WHEN r.[DescriptorProvider] COLLATE Latin1_General_100_BIN2 = r.[ExpectedProvider] COLLATE Latin1_General_100_BIN2
              AND DATALENGTH(r.[DescriptorProvider]) = DATALENGTH(r.[ExpectedProvider])
              AND r.[DescriptorName] COLLATE Latin1_General_100_BIN2 = r.[ExpectedName] COLLATE Latin1_General_100_BIN2
              AND DATALENGTH(r.[DescriptorName]) = DATALENGTH(r.[ExpectedName]) THEN 1 ELSE 0 END AS [IsOwn],
        CASE WHEN r.[DescriptorId] IS NULL THEN 0
             WHEN r.[ScopeKind] <> N'Integration' THEN 0
             WHEN r.[DescriptorProvider] COLLATE Latin1_General_100_BIN2 = N'platform-setting' COLLATE Latin1_General_100_BIN2
              AND DATALENGTH(r.[DescriptorProvider]) = DATALENGTH(N'platform-setting')
              AND r.[DescriptorName] COLLATE Latin1_General_100_BIN2 = r.[LegacyName] COLLATE Latin1_General_100_BIN2
              AND DATALENGTH(r.[DescriptorName]) = DATALENGTH(r.[LegacyName]) THEN 1 ELSE 0 END AS [IsLegacy],
        CASE WHEN r.[DescriptorId] IS NULL THEN 0
             WHEN r.[DescriptorProvider] COLLATE Latin1_General_100_BIN2 IN (N'platform-setting' COLLATE Latin1_General_100_BIN2, N'integration-setting' COLLATE Latin1_General_100_BIN2) THEN 1 ELSE 0 END AS [IsSettingsProvider],
        CASE WHEN r.[DescriptorId] IS NULL THEN 0
             WHEN r.[DescriptorState] IN (2 /* Disabled */, 3 /* Revoked */) THEN 0
             WHEN NULLIF(r.[DescriptorActiveVersion], N'') IS NULL THEN 0
             WHEN NOT EXISTS
                  (
                      SELECT 1 FROM [dbo].[SecretVersions] AS v
                      WHERE v.[SecretId] = r.[DescriptorId] AND v.[Version] = r.[DescriptorActiveVersion] AND v.[State] = 1 /* Active */
                  ) THEN 0
             ELSE 1 END AS [IsUsable],
        -- Every other holder of the same secret, in every known location. The hexadecimal GUID form is not searched for.
        (SELECT COUNT(*) FROM [dbo].[PlatformSettingValues] AS o
          WHERE o.[Id] <> r.[RowId]
            AND (o.[ValueJson] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Dashed] + N'%'
              OR o.[ValueJson] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Plain] + N'%')) AS [OtherSettingRows],
        (SELECT COUNT(*) FROM [dbo].[Connections] AS o
          WHERE o.[SecretRefsJson] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Dashed] + N'%'
             OR o.[SecretRefsJson] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Plain] + N'%') AS [OtherConnections],
        (SELECT COUNT(*) FROM [dbo].[EntraInstanceRegistrations] AS o
          WHERE o.[ClientSecretRef] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Dashed] + N'%'
             OR o.[ClientSecretRef] COLLATE Latin1_General_100_CI_AS LIKE N'%' + r.[Plain] + N'%') AS [OtherEntraRegistrations]
    FROM [Resolved] AS r
),
[Classified] AS
(
    SELECT
        x.*,
        CASE
            -- Not a string at all, or not valid JSON, or text that is not a reference: only a key that must hold one is a problem.
            WHEN x.[JsonType] IS NULL OR x.[JsonType] <> 1 OR x.[SecretGuid] IS NULL THEN
                CASE WHEN x.[Sensitive] = 1 THEN N'unreadable' END
            -- A readable reference to nothing, or to a secret that cannot be used.
            WHEN x.[DescriptorId] IS NULL OR x.[IsUsable] = 0 THEN
                CASE WHEN x.[Sensitive] = 1 THEN N'dangling' END
            -- A usable secret under a key that is not listed as sensitive.
            WHEN x.[Sensitive] = 0 THEN N'ordinary_guid_text'
            WHEN x.[IsOwn] = 1 THEN
                CASE WHEN x.[OtherSettingRows] + x.[OtherConnections] + x.[OtherEntraRegistrations] > 0 THEN N'shared' ELSE N'owned' END
            WHEN x.[IsLegacy] = 1 THEN N'legacy_integration'
            WHEN x.[IsSettingsProvider] = 1 THEN N'key_mismatch'
            ELSE N'foreign_provider'
        END AS [Class],
        CASE WHEN x.[IsOwn] = 1 THEN N'owned'
             WHEN x.[IsLegacy] = 1 THEN N'legacy_integration'
             WHEN x.[IsSettingsProvider] = 1 THEN N'key_mismatch'
             WHEN x.[DescriptorId] IS NOT NULL THEN N'foreign_provider'
             ELSE N'none' END AS [Relation]
    FROM [Related] AS x
)
SELECT
    k.[ScopeKind],
    k.[ScopeId],
    k.[SettingKey],
    k.[Class],
    k.[Sensitive]    AS [KeyListedSensitive],
    k.[Relation],
    CASE WHEN k.[IsLegacy] = 1 THEN CASE WHEN k.[OtherSettingRows] + k.[OtherConnections] + k.[OtherEntraRegistrations] = 0 THEN 1 ELSE 0 END END AS [SoleHolder],
    CASE WHEN k.[DescriptorId] IS NOT NULL THEN k.[OtherSettingRows] END        AS [OtherSettingRows],
    CASE WHEN k.[DescriptorId] IS NOT NULL THEN k.[OtherConnections] END        AS [OtherConnections],
    CASE WHEN k.[DescriptorId] IS NOT NULL THEN k.[OtherEntraRegistrations] END AS [OtherEntraRegistrations],
    CASE WHEN k.[Relation] IN (N'legacy_integration', N'key_mismatch', N'foreign_provider') THEN k.[DescriptorProvider] END AS [ForeignProvider],
    CASE WHEN k.[Relation] IN (N'legacy_integration', N'key_mismatch', N'foreign_provider') THEN k.[DescriptorName] END     AS [ForeignOwnerName]
FROM [Classified] AS k
WHERE k.[Class] IS NOT NULL AND k.[Class] <> N'owned'
ORDER BY k.[ScopeKind], k.[ScopeId], k.[SettingKey], k.[Class];
