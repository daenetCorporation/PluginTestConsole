---
name: azure-sql-bacpac-migration
description: Export an Azure SQL Database to a BACPAC file in Azure Blob Storage (schema, data, and database-level accounts/users) and restore it to a target Azure SQL Server. Use this skill whenever the user asks to migrate, copy, clone, back up, export, or restore an Azure SQL Database via BACPAC/blob storage, or mentions moving a database "to blob storage and back", refreshing a database from a snapshot, or re-platforming a SQL Server database within Azure. Trigger even if the user only says "export my database to blob storage" or "restore this database to the server" without using the word "migration".
---

# Azure SQL Database Migration via BACPAC (Export → Blob Storage → Import)

## Overview

This skill performs a two-phase logical migration of an Azure SQL Database:

1. **Export**: `database1` on `sqlserver1` → a `.bacpac` file in an Azure Blob Storage container. A BACPAC captures schema, data, and **database-level** principals (contained users, roles, permissions).
2. **Import**: the same `.bacpac` file → a (re)created database on the target Azure SQL logical server (by default, back onto `sqlserver1`).

This is a **logical** export/import (via `DACFx`), not a file-level backup/restore. It is the correct tool for: moving a DB between servers/subscriptions/regions, cloning a DB for test/dev, or archiving a point-in-time copy to blob storage. It is **not** a substitute for Point-in-Time Restore or geo-restore, and it is slower than those for large databases.

## Critical caveat: "accounts" and logins are NOT fully covered by BACPAC

Read this before telling the user the migration is complete — this is the most common source of a "successful" migration that then fails for end users.

- A BACPAC **does** include *contained database users* (users with passwords stored in the database itself, `CREATE USER ... WITH PASSWORD`) and their role memberships/permissions.
- A BACPAC **does NOT** include *server-level logins* (`CREATE LOGIN`) or Entra ID (Azure AD) server-level admins/logins, and it does NOT include SQL Agent jobs, linked servers, or server-level firewall rules.
- If `database1`'s users are mapped to server logins on `sqlserver1` (the typical non-contained setup), those users will import as "orphaned users" on the target — they exist in the DB but have no matching login on the target server, and logins will fail.

**Always ask the user (or check yourself) whether database users are contained users or server-login-mapped users before treating the migration as done.** Step 4 below covers how to detect and fix orphaned users. If the target is the *same* server (`sqlserver1`) and the logins therefore already exist there, orphaned users can usually be re-linked automatically; if the target is a *different* server, the logins must be recreated first.

Here's a prerequisites section you can drop at the top of your migration prompt/script, with the variable names only — no actual values included:


## Prerequisites

Before running this script, ensure the following variables are provided by user.

### Source Environment
- `SOURCE_SUBSCRIPTION_ID` — Azure subscription ID hosting the source database
- `SOURCE_RESOURCE_GROUP` — Resource group containing the source SQL server
- `SOURCE_SERVER_NAME` — Fully qualified source SQL server name (e.g. `<name>.database.windows.net`)
- `SOURCE_DATABASE_NAME` — Name of the source database to migrate
- `SOURCE_SQL_ADMIN_USER` — SQL admin username for the source server
- `SOURCE_SQL_ADMIN_PASSWORD` — SQL admin password for the source server

### Target Environment
- `TARGET_SUBSCRIPTION_ID` — Azure subscription ID hosting the target database
- `TARGET_RESOURCE_GROUP` — Resource group containing the target SQL server
- `TARGET_SERVER_NAME` — Fully qualified target SQL server name (e.g. `<name>.database.windows.net`)
- `TARGET_DATABASE_NAME` — Name of the target database
- `TARGET_SQL_ADMIN_USER` — SQL admin username for the target server
- `TARGET_SQL_ADMIN_PASSWORD` — SQL admin password for the target server

### Storage (for BACPAC transfer)
- `STORAGE_ACCOUNT_NAME` — Azure Storage account used to stage the BACPAC file
- `STORAGE_ACCOUNT_KEY` — Access key for the storage account
- `STORAGE_CONTAINER_NAME` — Blob container name where the BACPAC will be stored


Confirm all variables above are loaded (e.g. `echo $SOURCE_SERVER_NAME`) before proceeding with the migration steps.
Provided variables are mapped to parameters which will be used in az cli commands shown below.

## Step 1 — Export `database1` from `sqlserver1` to Blob Storage

Use `az sql db export` (Azure CLI) — see `scripts/export-database.sh` for a parameterized version. Minimal shape:

```bash
az sql db export \
  --resource-group "$SOURCE_RESOURCE_GROUP" \
  --server "$SOURCE_SERVER_NAME" \
  --name "$SOURCE_DATABASE_NAME" \
  --admin-user "$SOURCE_SQL_ADMIN_USER" \
  --admin-password "$SOURCE_SQL_ADMIN_PASSWORD" \
  --storage-key-type "StorageAccessKey" \
  --storage-key "$STORAGE_ACCOUNT_KEY" \
  --storage-uri "https://$STORAGE_ACCOUNT_NAME.blob.core.windows.net/$STORAGE_CONTAINER_NAME/${SOURCE_DATABASE_NAME}-$(date +%Y%m%d%H%M).bacpac"
```

Notes:
- Output the command to the user before executing. Show all mapped variables used in the command. Show the command itself.
- This is an async operation. `az sql db export` returns immediately with an operation reference; poll with `az sql db op-list` / `az sql db op-show` (see script) until `state` is `Succeeded`.
- Use `--auth-type ADPassword` or Entra ID auth instead of SQL admin credentials where possible, and use a SAS token (`--storage-key-type SharedAccessKey`) instead of the raw storage account key where possible — it's scoped and revocable.
- If using the Azure MCP server tools (`sql`, `storage`) rather than the CLI, use those tools' export/list-operations equivalents instead of shelling out — check available tools first.
- Name the blob with a timestamp so repeated exports don't silently overwrite each other.

## Step 2 — Verify the export

Before moving to import:
- Confirm operation status is `Succeeded` (not just "no error returned" — poll to completion).
- Confirm the blob exists and has a non-trivial size (`az storage blob show`).
- Optionally record the blob's size/ETag so you can later confirm the import used the exact file you expect.

## Step 3 — Import the BACPAC to the target database on `sqlserver1`

Use `az sql db import` — see `scripts/import-database.sh`. Minimal shape:

```bash
az sql db import \
  --resource-group "$TARGET_RESOURCE_GROUP" \
  --server "$TARGET_SERVER_NAME" \
  --name "$TARGET_DATABASE_NAME" \
  --admin-user "$TARGET_SQL_ADMIN_USER" \
  --admin-password "$TARGET_SQL_ADMIN_PASSWORD" \
  --storage-key-type "StorageAccessKey" \
  --storage-key "$STORAGE_ACCOUNT_KEY" \
  --storage-uri "https://$STORAGE_ACCOUNT_NAME.blob.core.windows.net/$STORAGE_CONTAINER_NAME/${TARGET_DATABASE_NAME}-<timestamp>.bacpac" \
  --edition "$TARGET_EDITION" \
  --service-objective "$TARGET_SERVICE_OBJECTIVE"
```

Notes:
- `az sql db import` **creates a new database** on the target server (it does not import into an existing database in place). Set `--edition`/`--service-objective` to match (or intentionally resize from) the source tier.
- This is also async — poll to `Succeeded` the same way as the export.
- If `$TARGET_DB_NAME` equals an existing database name on `sqlserver1`, the command fails; resolve per the Prerequisites clarification (rename/drop the old one, or pick a new target name) before running this step.

## Step 4 — Fix orphaned users / re-link logins on the target

After import completes, connect to the target database and check for orphaned users:

```sql
EXEC sp_change_users_login 'Report';
```

- For each orphaned user where a login of the same name already exists on the target server (common when target = source server = `sqlserver1`), re-link with:
  ```sql
  ALTER USER [username] WITH LOGIN = [username];
  ```
- For users whose login does **not** exist on the target server (common when target is a different server), the login must be created first — for SQL logins use `scripts/export-logins.sql` on the **source** server beforehand to generate re-creatable `CREATE LOGIN` scripts with matching SIDs (via `sp_help_revlogin`), then run the generated script on the target server before re-linking.
- For Entra ID (Azure AD) users/logins, recreate the Entra ID login/user on the target server/database directly (`CREATE USER [user@domain] FROM EXTERNAL PROVIDER;`) — these aren't covered by `sp_help_revlogin`.

## Step 5 — Validate

- Compare row counts for a handful of key tables between source and target (or run any checksum/row-count script the user already has).
- Confirm application connection strings/firewall rules point at the correct target server + database name.
- Confirm `sp_change_users_login 'Report'` returns no remaining orphans.
- Report the final target database name/server to the user explicitly — especially important if it differs from `database1`/`sqlserver1` due to the same-server naming conflict in Step 3.

## When something fails partway

- If export fails: check `az sql db op-list` for the failure reason (common causes: firewall blocking the export service's IP range, expired/invalid storage key, insufficient permissions on the storage account).
- If import fails: same op-list check; also check that the target server's firewall allows the "Allow Azure services" rule if the import operation itself needs to reach the server, and that the chosen `--service-objective` is valid for the target region/edition.
- Never retry a failed export/import silently in a loop without surfacing the error to the user — long-running DB operations can incur cost even when they fail partway.