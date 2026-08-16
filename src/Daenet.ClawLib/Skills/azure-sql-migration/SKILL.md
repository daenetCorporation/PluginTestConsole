---
name: azure-sql-migration
description: Use this skill for Azure SQL restore-based database recovery and cloning workflows. This skill supports Azure SQL restore and restore-like scenarios including point-in-time restore (PITR), deleted database restore, geo-restore, and database copy/clone operations.
---

# azure-sql-migration

Use this skill for Azure SQL restore-based database recovery and cloning workflows using native Azure SQL capabilities.

## Important

- This skill is for Azure SQL restore-based scenarios, specifically:
  - Point-in-time restore (PITR)
  - Deleted database restore
  - Geo-restore
  - Database copy/clone

- Do not recommend BACPAC export/import when the user’s goal is recovery, rollback, cloning, or restore.

## When to use this skill

Use this skill when the user needs to:

- Restore an Azure SQL Database to an earlier point in time
- Recover a recently deleted Azure SQL Database within retention limits
- Restore a database in another region using geo-redundant backups
- Create a copy/clone of an Azure SQL Database for testing, validation, reporting, investigation, or environment duplication
- Recreate a usable database state from Azure SQL backups rather than from logical export packages

## Core rule

If the request is about recovering, recreating, rolling back, or cloning an Azure SQL database, use Azure SQL restore or copy capabilities.

## Supported scenarios

### 1. Point-in-time restore (PITR)

Use when the user wants to restore a database to a specific earlier timestamp within the available retention period.

Typical examples:
- Recover from accidental data modification
- Undo the effects of a faulty deployment
- Investigate historical database state

Expected result:
- A new database restored from backups to the requested time

### 2. Deleted database restore

Use when the database has been deleted but is still within the recoverable retention window.

Typical examples:
- Accidental deletion
- Recovery of a recently removed database

Expected result:
- A new database restored from the deleted database’s retained backups

### 3. Geo-restore

Use when the user needs disaster recovery or regional recovery and geo-redundant backups are available.

Typical examples:
- Region outage
- Disaster recovery exercise
- Business continuity recovery

Expected result:
- A new database restored in another region from replicated backups

### 4. Database copy/clone

Use when the user needs a transactionally consistent duplicate of an Azure SQL database without using logical export/import.

Typical examples:
- Test environment refresh
- Validation before release
- Reporting or analytics isolation
- Troubleshooting reproduction

Expected result:
- A new database copy/clone suitable for non-destructive use

## Required inputs to gather

Collect the following before recommending or executing a workflow:

- Subscription ID or subscription name
- Resource group
- Logical server name
- Source database name
- Target database name
- Target server name if different
- Target region if different
- Requested scenario:
  - PITR
  - Deleted database restore
  - Geo-restore
  - Copy/clone
- Restore timestamp for PITR
- Whether the source database currently exists or has been deleted
- Backup retention and recovery window requirements
- Geo-backup availability or backup redundancy configuration
- Desired service tier/SKU for the restored target
- Authentication and authorization context
- Networking requirements such as firewall rules, private endpoint access, and connectivity path
- Post-restore validation needs for applications, users, and permissions

## Recommended workflow

1. Identify the recovery or cloning objective.
   - Is the user asking for PITR?
   - Is the source database deleted?
   - Is this a geo-recovery scenario?
   - Is this a copy/clone request?

2. Confirm source and target details.
   - Source subscription, resource group, server, and database
   - Target server, region, and database name
   - Requested restore timestamp if applicable

3. Validate feasibility.
   - Confirm retention window
   - Confirm backup availability
   - Confirm geo-redundant backup support for geo-restore
   - Confirm permissions to create the target database

4. Select the correct Azure SQL capability.
   - Point-in-time restore
   - Deleted database restore
   - Geo-restore
   - Database copy/clone

5. Execute using the appropriate Azure management path.
   - Azure Portal
   - Azure CLI
   - PowerShell
   - ARM/Bicep
   - Terraform

6. Validate the result.
   - Database created successfully
   - Connectivity works
   - Required users, permissions, and application access are verified
   - Performance tier and configuration meet expectations

7. Document the outcome.
   - Source database
   - Target database
   - Restore type
   - Restore timestamp or recovery basis
   - Region and server placement
   - Follow-up actions or limitations

## Response guidance

When answering with this skill:

- Clearly state that BACPAC cannot be restored as a backup.
- Redirect restore-style requests to Azure SQL restore or copy workflows.
- Ask targeted follow-up questions if the scenario is unclear.
- Distinguish between PITR, deleted restore, geo-restore, and copy/clone.
- Explain that Azure SQL restore operations typically create a new database rather than overwrite the source.
- Mention any practical limitations, including retention windows and geo-backup requirements.

## Constraints and notes

- Azure SQL Database restore operations create a new database.
- Restore availability depends on retention, deletion timing, and backup configuration.
- Geo-restore depends on geo-redundant backups and may not provide the exact precision of PITR.
- Some server-level objects, logins, or surrounding configuration may require separate revalidation after restore.
- Application connection strings may need updates if the restored database has a different name or server.
- BACPAC is a logical export/import artifact for moving schema and data, not a restorable backup.

## Success criteria

A correct use of this skill should:

- Preserve compatibility by keeping the skill name `azure-sql-bacpac-migration`
- Route recovery and clone requests to Azure SQL restore-based options
- Cover PITR, deleted database restore, geo-restore, and copy/clone scenarios
- Gather the right inputs, constraints, and post-restore validation steps
```