---
name: azure-sql-database-delete
description: >
  Safely find and delete Azure SQL databases in a specific Azure subscription. Use this skill
  whenever the user wants to delete, remove, drop, clean up or decommission one or more Azure SQL
  databases, or wants to find Azure SQL databases by name or partial name before removing them,
  even if they don't say "skill" or name a tool. Supports deletion by exact name and search by
  matching text followed by deletion of explicitly confirmed matches. The skill describes WHAT must
  be done as tool-agnostic tasks; the executing agent validates which command-line tooling is
  available (e.g. Azure CLI or PowerShell Az module) and derives the concrete commands at runtime.
  Deletion always requires explicit confirmation of exact database names.
---

# Azure SQL Database Delete Skill

This skill is intentionally tool-agnostic. It contains no hardcoded commands. Instead it defines
a sequence of tasks with clear inputs, expected results and safety gates. The executing agent is
responsible for:

1. validating which execution tooling is available and usable,
2. translating each task into the appropriate commands for that tooling,
3. executing the tasks in order and honoring every safety gate.

---

## Inputs

### Required
- `subscriptionId` – Azure subscription ID in which all operations take place.

### Exactly one of
- `databaseName` – exact Azure SQL database name to find and delete.
- `matchText` – text to search for (case-insensitive substring) in Azure SQL database names.

### Optional
- `confirmDelete` – explicit approval that deletion may proceed.
- `confirmedDatabaseNames` – exact list of database names approved for deletion after matches were shown.
- `serverName` / `resourceGroup` – narrow the search scope if known.

If both `databaseName` and `matchText` are provided, or neither is, stop and ask the user to clarify.

---

## Phase 0 – Validate the execution environment

Before any Azure operation, determine how the tasks can be executed. Do not assume a specific tool.

**Task 0.1 – Detect available tooling.**
Check, in order of preference, which of the following is installed and callable:
- Azure CLI (with the SQL command group available)
- PowerShell with the Az modules (Az.Accounts and Az.Sql)
- Any other Azure-capable tooling the environment exposes (e.g. an Azure MCP server)

Record the selected tool and its version. If none is available, stop and report what is missing
and how the user can install it. Do not attempt to install tooling without the user's approval.

**Task 0.2 – Validate authentication.**
Confirm there is an authenticated Azure session. If not authenticated, stop and ask the user to
sign in themselves. Never enter credentials, secrets or tokens on the user's behalf.

**Task 0.3 – Validate subscription access.**
Confirm that `subscriptionId` is visible to the signed-in identity. If not, stop and report it.

**Task 0.4 – Select a single execution tool.**
Use the same tool for the entire run so that results are consistent. Report to the user:
tool, version, signed-in identity, target subscription name and ID.

---

## Phase 1 – Discovery (read-only)

All tasks in this phase are read-only. No changes may be made.

**Task 1.1 – Set the subscription context.**
Switch the active context to `subscriptionId` and verify that the active subscription now matches
it. If it does not match, stop. All subsequent tasks must operate only in this subscription.

**Task 1.2 – Enumerate Azure SQL logical servers.**
List every Azure SQL server in the subscription (or only `serverName` / `resourceGroup` if given),
capturing server name, resource group and location.

**Task 1.3 – Enumerate databases per server.**
For each server, list its databases. Exclude the system database `master`.

**Task 1.4 – Filter candidates.**
- With `databaseName`: keep databases whose name equals it exactly.
- With `matchText`: keep databases whose name contains it (case-insensitive).

**Task 1.5 – Collect details for each match.**
For every match, capture: database name, server, resource group, location, status, edition/tier
(SKU), and where available: elastic pool membership, geo-replication / failover-group role,
and any resource locks on the database, server or resource group.

**Task 1.6 – Report matches.**
- No matches: report that nothing was found and stop.
- Matches found: present all of them in a table (name, server, resource group, location, status,
  tier, notes) and state clearly: *"No deletion has been performed yet."*

---

## Phase 2 – Confirmation gate

Deletion may proceed only when ALL of the following are true:

1. The user has explicitly approved deletion (`confirmDelete` or an unambiguous statement in chat).
2. Each database to delete is named exactly (`confirmedDatabaseNames`, or `databaseName` with a
   single match).
3. Each confirmed name maps to exactly one match from Phase 1. If the same name exists on more than
   one server, the user must also confirm the server (and resource group).
4. Each confirmed name was part of the Phase 1 results in this run.

If any condition fails, stop and ask for confirmation using this pattern:

```text
I found these Azure SQL databases in subscription <SubscriptionName> (<SubscriptionId>):

- <DatabaseName> | Server: <ServerName> | Resource Group: <ResourceGroup> | Tier: <Tier>
- ...

No deletion has been performed yet.
Please confirm the exact database name(s) (and server, if ambiguous) to delete.
```

Never infer, guess or expand confirmation to unconfirmed matches.

---

## Phase 3 – Pre-deletion checks

For each confirmed database:

**Task 3.1 – Re-verify existence.** Confirm the database still exists on the expected server and
resource group. If not, skip it and report why.

**Task 3.2 – Check blockers.** Identify anything that will block or complicate deletion and report
it instead of working around it:
- resource locks (do not remove locks; ask the user to handle them)
- the database is a geo-replication primary with active secondaries, or part of a failover group
- the database is a secondary replica (deleting it affects replication, not the primary)

**Task 3.3 – Inform about recoverability.** Tell the user whether the database can be restored
after deletion (deleted-database restore within the backup retention window, long-term retention
backups) based on what the tooling reports. Do not claim recoverability that cannot be verified.

**Task 3.4 – Show the execution plan.** Before execution, list the exact delete operations that
will run (database, server, resource group) and the concrete commands derived for the selected tool.

---

## Phase 4 – Delete confirmed targets

**Task 4.1 – Delete.** Delete only the confirmed databases, one at a time, each addressed by the
fully qualified triple: database name + server + resource group. Use the tool's non-interactive
mode only for targets that passed Phases 2 and 3.

**Task 4.2 – Handle errors.** If a deletion fails, report the tool's error output exactly, do not
retry with different parameters, and continue with the remaining confirmed targets only if the user
agreed upfront that failures should not stop the run; otherwise stop.

---

## Phase 5 – Verify

**Task 5.1 – Verify removal.** For each deleted database, query it again on the same server and
resource group. A "not found" result confirms deletion.

**Task 5.2 – Final report.** Summarize per database: Deleted / Skipped (with reason) / Failed
(with exact error), plus the verification result.

---

## Safety rules

1. Never delete without explicit confirmation of exact database names.
2. Never guess between multiple matches or multiple servers with the same database name.
3. Operate only in the requested subscription; verify the active context before every phase.
4. Always show server and resource group before deleting.
5. Verify existence before deletion and absence after deletion.
6. Never remove resource locks, break replication or change failover groups to make a delete succeed.
7. Never handle credentials; authentication is done by the user.
8. Report tool errors verbatim.
9. Never touch the `master` database or delete servers, pools or resource groups.

---

## Outputs

### Environment validation
```text
Tool: <tool> <version> | Identity: <account> | Subscription: <name> (<id>)
```

### Discovery
```json
[
  {
    "Name": "<DatabaseName>",
    "Server": "<ServerName>",
    "ResourceGroup": "<ResourceGroup>",
    "Location": "<Region>",
    "Status": "Online",
    "Edition": "<Tier>",
    "Notes": "<locks / replication / elastic pool, if any>"
  }
]
```

### Deletion and verification
```text
<DatabaseName> | Server: <ServerName> | RG: <ResourceGroup> | Result: Deleted | Verified: not found
```

---

## Example inputs

### Search only
```json
{ "subscriptionId": "<subscription-id>", "matchText": "umbraco" }
```

### Delete exact database
```json
{ "subscriptionId": "<subscription-id>", "databaseName": "UmbracoCmsPrototype_import", "confirmDelete": true }
```

### Search, then delete selected matches
```json
{
  "subscriptionId": "<subscription-id>",
  "matchText": "UmbracoCmsPrototype",
  "confirmedDatabaseNames": ["UmbracoCmsPrototype", "UmbracoCmsPrototype_ad_loginTest"],
  "confirmDelete": true
}
```