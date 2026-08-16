---
name: azure-sql-bacpac-migration
description: >
  Migrates an Azure SQL Database from one subscription/server to another using the Azure CLI (az sql db export/import) via a BACPAC staged in Blob Storage. Use this skill whenever the user wants to move, migrate, or copy an Azure SQL Database across subscriptions or servers, mentions BACPAC export/import, or provides migration parameters with SOURCE_/TARGET_ fields. Handles export to blob, creating the target database with matching edition/service tier/collation, importing schema and data, and optionally recreating SQL server logins on the target, since logins are not part of a .bacpac. Trigger even if the user just says "migrate my Azure SQL database to another subscription" without mentioning BACPAC by name.
---

# Azure SQL BACPAC Migration

Moves an Azure SQL Database between subscriptions/servers via BACPAC export/import,
driven entirely by a single parameter file (never hardcode credentials into scripts
or into chat output).

## Prerequisites

- Azure CLI installed and logged in (`az login`), with access to both the source and
  target subscriptions.
- The **target logical server must already exist** (this skill does not create SQL
  servers, only databases). If it doesn't exist yet, run `az sql server create` first.
- A storage account (any account/container works) used as a staging area for the
  `.bacpac` file. It can be in either subscription.
- For login migration (step 4 only): `sqlcmd` on PATH (mssql-tools18).

## Step 0: Get the parameters from the user

The required fields are the SOURCE_/TARGET_ server, database, credential, and
storage-account values are provided by user. There are two ways the
user can supply them — accept either one:

1. **Pasted directly in the prompt.** The user types or pastes the values
   (as `KEY=value` lines, a table, prose, or any other reasonably parseable
   format) straight into the chat message. Ask a follow-up
   only for fields that are genuinely missing or ambiguous — don't ask them
   to reformat what they already gave you.

2. **A file to load them from.** The user points you to a file (path, or an
   uploaded file) that already contains the parameter values — e.g. an
   exported `.txt`, a notes file, a JSON/YAML config, etc. Read that file
   yourself and map its contents; don't ask the
   user to manually reformat it first.

Either way, parsed values should be included in the conversation history, independent if credentials are there.


## Workflow

Use parameter values to build and run the actual CLI commands yourself (Azure CLI or PowerShell/Az module — match whatever the user already has on PATH, defaulting to Azure CLI if unclear). Run steps individually so you and the user can sanity-check between them before moving on.
Output every step in the worklow to the user and explain what is happening as next.
Provide a brief error message if a command fails, and ask the user to confirm before retrying or moving on.

| Step | What it does | Command to build and run |
|---|---|---|
| 1 | Exports the source DB to `<container>/<dbname>-<timestamp>.bacpac` in blob storage. Poll until the export operation succeeds before moving on. | Switch to the source subscription (`az account set --subscription <SOURCE_SUBSCRIPTION>`), then run `az sql db export` against the source server/database with `--storage-key`, `--storage-key-type`, `--storage-uri` pointing at `<container>/<dbname>-<timestamp>.bacpac`, and the source SQL admin `--admin-user`/`--admin-password`. Capture the returned operation/request name and poll it (`az sql db op-list` / repeated `az sql db op-show` or `az storage blob show` on the target blob) until status is `Succeeded` — don't proceed to step 2 while it's still `InProgress`. PowerShell equivalent: `New-AzSqlDatabaseExport`, polled with `Get-AzSqlDatabaseImportExportStatus`. |
| 2 | Reads the source DB's edition, service objective, max size, and collation, then creates an empty database with matching specs on the target server. | While still on the source subscription, run `az sql db show` on the source database and note `edition`/`currentServiceObjectiveName`/`maxSizeBytes`/`collation`. Switch to the target subscription (`az account set --subscription <TARGET_SUBSCRIPTION>`), then run `az sql db create` on the target server with those same edition, service-objective, max-size, and collation values so the empty database matches the source. PowerShell equivalent: `Get-AzSqlDatabase` then `New-AzSqlDatabase`. |
| 3 | Imports the BACPAC (schema + all data) into the database created in step 2. Poll until the import succeeds. | Still on the target subscription, run `az sql db import` against the target server/database just created, pointing `--storage-uri` at the same blob written in step 1, with the target SQL admin `--admin-user`/`--admin-password` and the same `--storage-key`/`--storage-key-type`. Poll the returned operation the same way as step 1 until `Succeeded`. PowerShell equivalent: `New-AzSqlDatabaseImport`, polled with `Get-AzSqlDatabaseImportExportStatus`. |
| 4 (optional) | Recreates SQL-authentication server logins on the target, preserving password (via hash) and SID. See "About logins" below - most users don't need this. | On the source server, connect with `sqlcmd` (or `Invoke-Sqlcmd`) to `master` and query `sys.sql_logins` for the login's `name`, `password_hash`, and `sid`. Build a `CREATE LOGIN [name] WITH PASSWORD = 0x<hash> HASHED, SID = 0x<sid>;` statement per login and run it against `master` on the target server with `sqlcmd`/`Invoke-Sqlcmd`. Confirm with the user (y/N) before executing anything against the target's `master` database — don't skip that confirmation. |Confirm with the user before running step 4 in particular, since it modifies the target server's master database.

## About logins - read this before promising the user "logins are migrated"

A `.bacpac` file contains **schema + data + contained database users** — it does
**not** contain server-level SQL logins, and it does not contain Azure AD logins.
This is an Azure SQL Database (and BACPAC format) limitation, not a limitation of
this skill. Concretely:

- If the source database uses **contained database users with passwords**
  (`CREATE USER ... WITH PASSWORD`), those users and their access come across
  automatically with the BACPAC import in step 3. No extra work needed.
- If the source database uses **server logins mapped to database users**
  (`CREATE LOGIN` on master + `CREATE USER ... FOR LOGIN` on the db), the login
  itself lives outside the database and must be recreated on the target server.
  That's what step 4 does, for SQL-auth logins, by copying the password hash and
  SID from `sys.sql_logins` so existing user-to-login mappings keep working.
- **Azure AD logins** aren't hash-based and can't be scripted this way — recreate
  them manually on the target with `CREATE LOGIN [user@domain.com] FROM EXTERNAL PROVIDER;`.

Ask the user which kind of logins they're using if it's not obvious, rather than
assuming step 4 is needed or unneeded.


## Troubleshooting

- **Export/import "InProgress" for a long time**: normal for larger databases: this is
  a DTU/vCore-based serverless operation, size and tier affect duration heavily.
- **Import fails with a collation or edition mismatch**: step 2 mirrors the source's
  edition/service-objective/collation automatically; if the target server has policy
  restrictions (e.g. only allows Gen5 vCore), you may need to edit
  `02-create-target-db.sh` to override the SKU rather than copy it verbatim.
- **`az sql db import` errors that the database already contains objects**: the
  target database from step 2 must be empty; don't reuse a database that already
  has schema in it.