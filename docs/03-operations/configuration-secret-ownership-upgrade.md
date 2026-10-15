# Upgrading a persistent installation: configuration secret ownership (SEC-2)

Applies to an installation whose database already holds connections or settings from before SEC-2.2 (connections) and SEC-2.3
(instance and integration settings). It is a **pre-upgrade step**. A database that was deliberately discarded and reseeded needs
none of it: a new database uses the corrected conventions from its first row.

> **Status.** The supported production topology and its installer, service hosting and upgrade procedure are delivered by the OPS-1
> epic and do not exist yet ([deploy/README.md](../../deploy/README.md)). This page is the SEC-2 part of that procedure, written so
> OPS-1 can include it. It names no database and no installation: take them from the deployment records of the installation being
> upgraded.

## Why

A configuration row names a secret by id, and an id says nothing about who owns the secret. Before SEC-2 a row that named another
holder's secret would have the platform resolve it, and (for settings) version it or disable it. Now every read and write proves,
on the secret's own stored record, that the secret belongs to that row's holder, and refuses everything else with the same fixed
outcome. A row that names a secret it does not own therefore **stops working after the upgrade** until an operator repairs it.
The inventories below list those rows before the upgrade, so no one is surprised.

| Holder | Owns the secret stored under |
|---|---|
| A connection field | provider `platform-connection`, name `connection:{connectionId}:{fieldKey}` |
| An instance setting `K` | provider `platform-setting`, name `platform-setting:K` (unchanged, so the SMTP password keeps working) |
| Setting `K` of integration `I` | provider `integration-setting`, name `integration-setting:I:K` (`%` is written `%25` and `:` is written `%3A` in `I` and in `K`) |

A secret supplied by **deployment configuration** (`Notifications:Email:SmtpPasswordRef` in an application settings file or the
environment) is not a setting row. It is trusted input outside this rule and keeps working.

## Procedure

For each persistent installation, before upgrading:

1. Take a backup of the instance database ([backups-and-retention.md](backups-and-retention.md)).
2. Run the **connection inventory**, read-only, against the instance database:

   ```
   sqlcmd -S <server> -d <instance database> -E -i scripts/sec2/Get-ConnectionSecretOwnershipInventory.sql -W -s "|"
   ```

3. List the sensitive setting keys your modules declare. Open `scripts/sec2/Get-SettingSecretOwnershipInventory.sql` and, in the
   list at the top of the query, add one line per key, each starting with a comma. The product's own
   `Notifications.Email.SmtpPasswordRef` is already there.
4. Run the **setting inventory**, read-only, the same way:

   ```
   sqlcmd -S <server> -d <instance database> -E -i scripts/sec2/Get-SettingSecretOwnershipInventory.sql -W -s "|"
   ```

5. An empty result is the evidence that nothing needs repair. Record it with the database's identity and the date.
6. For each listed row, apply the disposition below, then run the inventory again. Record each row's disposition.
7. Upgrade.

Both scripts need only `SELECT` on the tables they read (the `db_datareader` role is enough), write and create nothing, and never
show a secret, a reference, a secret id or a payload. They show the **name** of a foreign secret, which identifies whose credential
may have been used. They need database compatibility level 130 or higher.

## Dispositions

### Connections

Listed by `Get-ConnectionSecretOwnershipInventory.sql`. Repair needs no reference: in the Management UI or API, give the field a
new value (`secretValues`) to store a secret the connection owns and repoint the field, or remove the field (`clearSecretFields`).
Neither reads, rotates, disables or changes the secret the old reference named. A `foreign_provider` or `foreign_owner` row is an
incident: if the connection was ever verified, browsed or bound, rotate the credential the other secret holds.

### Settings

Listed by `Get-SettingSecretOwnershipInventory.sql`.

| Class | What it means | What to do |
|---|---|---|
| `legacy_integration` | An integration row names a secret in the pre-SEC-2.3 shape (provider `platform-setting`, name `platform-setting:K`). That shape cannot say which integration made the secret, so it is **never adopted or renamed**; the row is refused at read and the module sees the setting as not configured. | **Re-enter the value** of the setting (the integration settings screen or `PUT` with a secret replace). The platform stores a new secret the row owns and repoints the row. The legacy secret is then retired **only if** nothing else still refers to it: `SoleHolder` is `1` and no other row, connection or Entra registration names it. If another holder remains, the legacy secret is kept until the last holder has moved. |
| `shared` | A row owns its secret but another row, connection or Entra registration also names it. | Find the other holder (`OtherSettingRows`, `OtherConnections`, `OtherEntraRegistrations`). Each holder re-enters its value. Nothing is adopted; the secret is kept. |
| `key_mismatch` | The row names a secret made for another setting, integration or scope. | The row is refused at read. The other secret is never modified. Re-enter the value. |
| `foreign_provider` | The row names a connection's or an Entra registration's secret. | **Incident**: a module may have read another holder's credential. Re-enter the value, and rotate the credential that secret holds. |
| `ordinary_guid_text` | A key that is not in your sensitive list holds text that names a usable secret. | Reported only. Harmless until a module declares the key sensitive, and refused at read after that. If the key is in fact sensitive, add it to the list and run again. |
| `dangling` | A listed sensitive key names no secret, or one that is disabled. | It already reads as not configured. Re-enter the value or clear the setting. |
| `unreadable` | A listed sensitive key holds a value that is not a readable reference. | Clear the setting or re-enter the value. |

Re-entering a value, clearing or resetting a setting only ever disables a secret that is proven to belong to that setting (or, for a
legacy secret, that nothing else refers to). Clearing integration B's setting never disables integration A's secret, whatever B's
row names.

## What the platform records

Every refused read writes one audit row, with a fixed message and no reference, owner or secret:

| Action | Meaning |
|---|---|
| `connection.secret.resolution_refused` | A connection field's reference is not that field's own secret (SEC-2.2) |
| `integration.settings.secret.resolution_refused` | An integration setting's reference is not that setting's own secret |
| `platform.settings.secret.resolution_refused` | An instance setting's reference is not that setting's own secret |

These rows are not routine. After the repair above, expect none. A row means a setting or connection the inventory would have
listed.

## Limits worth knowing

- An **instance** setting whose row names a secret in the old integration shape **for the same key** cannot be told from the
  instance setting's own secret, because the two have the same provider and name by design. It is not listed.
- The inventories find other holders by searching row text for the secret's hyphenated and 32-digit forms. A reference written in
  the hexadecimal GUID form is not found; the product never wrote that form.
- Retirement on re-entry reads the other holders inside the change's transaction. Under locking `READ COMMITTED` a competing change
  that has not committed yet is waited for, and under snapshot isolation it is not seen, so the legacy secret may be **kept**
  longer than necessary. It is never retired while a committed row still names it.
