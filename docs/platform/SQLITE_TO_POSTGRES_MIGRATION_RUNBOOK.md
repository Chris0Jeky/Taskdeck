# SQLite-to-PostgreSQL Migration Runbook

**Last updated**: 2026-04-09
**Related ADR**: ADR-0023 (SQLite-to-PostgreSQL Production Migration Strategy)
**Related issue**: #84 (PLAT-01)

This runbook provides step-by-step instructions for migrating an existing Taskdeck SQLite database to PostgreSQL. It is intended for operators deploying Taskdeck to a hosted environment.

> **Current repository state**: this is a preparatory runbook, not a fully executable cutover guide yet.
> The application runtime still hard-wires SQLite in `Taskdeck.Infrastructure.DependencyInjection`, and
> the `AddKnowledgeDocumentsAndFts` migration contains SQLite-only FTS5 SQL. Treat this document as the
> canonical operator checklist for the follow-up implementation work needed to make PostgreSQL migration real.

---

## Prerequisites

- PostgreSQL 15+ installed or a managed instance provisioned (AWS RDS, Azure Flexible Server, or GCP Cloud SQL)
- `psql` CLI available
- `dotnet` CLI (8.0+) available
- Access to the source SQLite database file (default: `taskdeck.db`)
- Taskdeck application stopped (no active writers to the SQLite database)
- Sufficient disk space for the SQLite database, the exported data, and the target PostgreSQL database

## Pre-Migration Checklist

1. **Stop the Taskdeck application** to prevent writes during migration.
2. **Back up the SQLite database file**:
   ```bash
   cp taskdeck.db taskdeck.db.pre-migration-backup
   ```
3. **Record row counts** for verification (save output for comparison):
   ```bash
   sqlite3 taskdeck.db <<'SQL'
   SELECT 'Users' AS tbl, COUNT(*) FROM Users
   UNION ALL SELECT 'Boards', COUNT(*) FROM Boards
   UNION ALL SELECT 'Columns', COUNT(*) FROM Columns
   UNION ALL SELECT 'Cards', COUNT(*) FROM Cards
   UNION ALL SELECT 'Labels', COUNT(*) FROM Labels
   UNION ALL SELECT 'CardLabels', COUNT(*) FROM CardLabels
   UNION ALL SELECT 'CardComments', COUNT(*) FROM CardComments
   UNION ALL SELECT 'BoardAccesses', COUNT(*) FROM BoardAccesses
   UNION ALL SELECT 'AuditLogs', COUNT(*) FROM AuditLogs
   UNION ALL SELECT 'AutomationProposals', COUNT(*) FROM AutomationProposals
   UNION ALL SELECT 'AutomationProposalOperations', COUNT(*) FROM AutomationProposalOperations
   UNION ALL SELECT 'ArchiveItems', COUNT(*) FROM ArchiveItems
   UNION ALL SELECT 'ChatSessions', COUNT(*) FROM ChatSessions
   UNION ALL SELECT 'ChatMessages', COUNT(*) FROM ChatMessages
   UNION ALL SELECT 'CommandRuns', COUNT(*) FROM CommandRuns
   UNION ALL SELECT 'Notifications', COUNT(*) FROM Notifications
   UNION ALL SELECT 'UserPreferences', COUNT(*) FROM UserPreferences
   UNION ALL SELECT 'LlmRequests', COUNT(*) FROM LlmRequests
   UNION ALL SELECT 'LlmUsageRecords', COUNT(*) FROM LlmUsageRecords
   UNION ALL SELECT 'OutboundWebhookSubscriptions', COUNT(*) FROM OutboundWebhookSubscriptions
   UNION ALL SELECT 'OutboundWebhookDeliveries', COUNT(*) FROM OutboundWebhookDeliveries
   UNION ALL SELECT 'AgentProfiles', COUNT(*) FROM AgentProfiles
   UNION ALL SELECT 'AgentRuns', COUNT(*) FROM AgentRuns
   UNION ALL SELECT 'KnowledgeDocuments', COUNT(*) FROM KnowledgeDocuments
   UNION ALL SELECT 'KnowledgeChunks', COUNT(*) FROM KnowledgeChunks
   UNION ALL SELECT 'ExternalLogins', COUNT(*) FROM ExternalLogins
   UNION ALL SELECT 'ApiKeys', COUNT(*) FROM ApiKeys
   UNION ALL SELECT 'CardCommentMentions', COUNT(*) FROM CardCommentMentions
   UNION ALL SELECT 'CommandRunLogs', COUNT(*) FROM CommandRunLogs
   UNION ALL SELECT 'AgentRunEvents', COUNT(*) FROM AgentRunEvents
   UNION ALL SELECT 'NotificationPreferences', COUNT(*) FROM NotificationPreferences;
   SQL
   ```

   **Note**: The `__EFMigrationsHistory` table tracks applied EF Core migrations. It is populated automatically by `dotnet ef database update` in Step 1 and should **not** be exported or imported as data — the schema step handles it.

4. **Provision the PostgreSQL database**:
   ```bash
   psql -h <host> -U <admin_user> -c "CREATE DATABASE taskdeck ENCODING 'UTF8' LC_COLLATE 'en_US.UTF-8';"
   psql -h <host> -U <admin_user> -c "CREATE USER taskdeck_app WITH PASSWORD '<strong-password>';"
   psql -h <host> -U <admin_user> -c "GRANT CONNECT ON DATABASE taskdeck TO taskdeck_app;"
   psql -h <host> -U <admin_user> -d taskdeck -c "GRANT USAGE, CREATE ON SCHEMA public TO taskdeck_app;"
   psql -h <host> -U <admin_user> -d taskdeck -c "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO taskdeck_app;"
   psql -h <host> -U <admin_user> -d taskdeck -c "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO taskdeck_app;"
   ```

   Use `CREATE` on the schema only while bootstrapping the PostgreSQL schema with EF Core. After the schema is in place, tighten the application user back to DML-only permissions if migrations are handled by an admin/operator identity.

## Step 1: Apply PostgreSQL Schema via EF Core Migrations

Prepare the application to target PostgreSQL and apply migrations.

> **Blocked today**: `Taskdeck.Infrastructure.DependencyInjection.AddInfrastructure()` still calls
> `UseSqlite()` unconditionally. There is no shipped `Taskdeck__DatabaseProvider` switch or runtime
> `UseNpgsql()` path in the application projects yet, so the commands below are follow-up implementation
> steps rather than something the current branch can execute successfully as-is.

> **Warning — FTS5 migration blocker**: The migration `AddKnowledgeDocumentsAndFts` contains
> raw SQLite-specific SQL (`CREATE VIRTUAL TABLE ... USING fts5`, `CREATE TRIGGER`). These
> statements will fail on PostgreSQL. Before running `dotnet ef database update`, you must
> add provider-conditional guards to that migration:
>
> ```csharp
> if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
> {
>     migrationBuilder.Sql(@"CREATE VIRTUAL TABLE IF NOT EXISTS ...");
>     migrationBuilder.Sql(@"CREATE TRIGGER IF NOT EXISTS ...");
> }
> ```
>
> Apply the same guard to the `Down()` method. See the "Full-Text Search Migration Note"
> section below for PostgreSQL FTS setup guidance.

```bash
# Follow-up implementation required before this step can work:
# 1. Add Npgsql to the runtime application infrastructure path
# 2. Add configuration-based provider selection in AddInfrastructure()
# 3. Guard SQLite-only migration SQL with ActiveProvider checks
# 4. Then run the PostgreSQL schema apply command against the empty database
```

Verify the schema was created:
```bash
psql -h <host> -U taskdeck_app -d taskdeck -c "\dt"
```

All expected tables should appear.

## Step 2: Export Data from SQLite

Export each table to CSV using `sqlite3`:

```bash
mkdir -p migration-export

sqlite3 -header -csv taskdeck.db "SELECT * FROM Users;" > migration-export/Users.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM Boards;" > migration-export/Boards.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM Columns;" > migration-export/Columns.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM Cards;" > migration-export/Cards.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM Labels;" > migration-export/Labels.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM CardLabels;" > migration-export/CardLabels.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM CardComments;" > migration-export/CardComments.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM CardCommentMentions;" > migration-export/CardCommentMentions.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM BoardAccesses;" > migration-export/BoardAccesses.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AuditLogs;" > migration-export/AuditLogs.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM LlmRequests;" > migration-export/LlmRequests.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AutomationProposals;" > migration-export/AutomationProposals.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AutomationProposalOperations;" > migration-export/AutomationProposalOperations.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM ArchiveItems;" > migration-export/ArchiveItems.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM ChatSessions;" > migration-export/ChatSessions.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM ChatMessages;" > migration-export/ChatMessages.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM CommandRuns;" > migration-export/CommandRuns.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM CommandRunLogs;" > migration-export/CommandRunLogs.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM Notifications;" > migration-export/Notifications.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM NotificationPreferences;" > migration-export/NotificationPreferences.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM UserPreferences;" > migration-export/UserPreferences.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM OutboundWebhookSubscriptions;" > migration-export/OutboundWebhookSubscriptions.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM OutboundWebhookDeliveries;" > migration-export/OutboundWebhookDeliveries.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM LlmUsageRecords;" > migration-export/LlmUsageRecords.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AgentProfiles;" > migration-export/AgentProfiles.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AgentRuns;" > migration-export/AgentRuns.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM AgentRunEvents;" > migration-export/AgentRunEvents.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM KnowledgeDocuments;" > migration-export/KnowledgeDocuments.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM KnowledgeChunks;" > migration-export/KnowledgeChunks.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM ExternalLogins;" > migration-export/ExternalLogins.csv
sqlite3 -header -csv taskdeck.db "SELECT * FROM ApiKeys;" > migration-export/ApiKeys.csv
```

## Step 3: Import Data into PostgreSQL

**Important**: Import tables in dependency order (parents before children) to respect foreign key constraints.

**Schema note**: EF Core Npgsql creates tables in the `public` schema by default with PascalCase names. Before importing, verify the table names match by running `\dt` in `psql`. If a custom schema was configured, adjust the table names in the import script accordingly.

```bash
# Order matters: parent tables first, then child tables
TABLES=(
  Users
  Boards
  Columns
  Cards
  Labels
  CardLabels
  CardComments
  CardCommentMentions
  BoardAccesses
  AuditLogs
  LlmRequests
  AutomationProposals
  AutomationProposalOperations
  ArchiveItems
  ChatSessions
  ChatMessages
  CommandRuns
  CommandRunLogs
  Notifications
  NotificationPreferences
  UserPreferences
  OutboundWebhookSubscriptions
  OutboundWebhookDeliveries
  LlmUsageRecords
  AgentProfiles
  AgentRuns
  AgentRunEvents
  KnowledgeDocuments
  KnowledgeChunks
  ExternalLogins
  ApiKeys
)

PGCONN="host=<host> dbname=taskdeck user=taskdeck_app password=<password>"

for table in "${TABLES[@]}"; do
  if [ -f "migration-export/${table}.csv" ] && [ -s "migration-export/${table}.csv" ]; then
    echo "Importing ${table}..."
    # Use \COPY to import CSV (runs client-side, no server file access needed)
    psql "$PGCONN" -c "\\COPY \"${table}\" FROM 'migration-export/${table}.csv' WITH (FORMAT csv, HEADER true)"
    if [ $? -ne 0 ]; then
      echo "ERROR: Failed to import ${table}. Stopping." >&2
      exit 1
    fi
  else
    echo "Skipping ${table} (empty or missing CSV)."
  fi
done

```

Managed PostgreSQL services often do not allow blanket trigger disabling, and `session_replication_role`
is generally too privileged for an application migration user. Keep constraints enabled, import in
dependency order, and treat any FK failure as a migration defect to fix before retrying.

**GUID column handling**: SQLite stores GUIDs as text strings. PostgreSQL with Npgsql maps `Guid` properties to the native `uuid` type. EF Core's Npgsql provider accepts standard UUID text format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`), so CSV import should work directly. If you encounter format errors, verify the GUID format in the CSV matches PostgreSQL's expected input.

**DateTimeOffset handling**: SQLite stores `DateTimeOffset` as ISO 8601 text strings. PostgreSQL stores them as `timestamptz`. The CSV import should parse ISO 8601 strings correctly. If timezone information is missing from SQLite values, PostgreSQL will assume UTC.

## Step 4: Verify Data Integrity

Run row-count verification against PostgreSQL and compare with the pre-migration counts from the checklist:

```bash
psql "$PGCONN" <<'SQL'
SELECT 'Users' AS tbl, COUNT(*) FROM "Users"
UNION ALL SELECT 'Boards', COUNT(*) FROM "Boards"
UNION ALL SELECT 'Columns', COUNT(*) FROM "Columns"
UNION ALL SELECT 'Cards', COUNT(*) FROM "Cards"
UNION ALL SELECT 'Labels', COUNT(*) FROM "Labels"
UNION ALL SELECT 'CardLabels', COUNT(*) FROM "CardLabels"
UNION ALL SELECT 'CardComments', COUNT(*) FROM "CardComments"
UNION ALL SELECT 'BoardAccesses', COUNT(*) FROM "BoardAccesses"
UNION ALL SELECT 'AuditLogs', COUNT(*) FROM "AuditLogs"
UNION ALL SELECT 'AutomationProposals', COUNT(*) FROM "AutomationProposals"
UNION ALL SELECT 'AutomationProposalOperations', COUNT(*) FROM "AutomationProposalOperations"
UNION ALL SELECT 'ArchiveItems', COUNT(*) FROM "ArchiveItems"
UNION ALL SELECT 'ChatSessions', COUNT(*) FROM "ChatSessions"
UNION ALL SELECT 'ChatMessages', COUNT(*) FROM "ChatMessages"
UNION ALL SELECT 'CommandRuns', COUNT(*) FROM "CommandRuns"
UNION ALL SELECT 'Notifications', COUNT(*) FROM "Notifications"
UNION ALL SELECT 'UserPreferences', COUNT(*) FROM "UserPreferences"
UNION ALL SELECT 'LlmRequests', COUNT(*) FROM "LlmRequests"
UNION ALL SELECT 'LlmUsageRecords', COUNT(*) FROM "LlmUsageRecords"
UNION ALL SELECT 'OutboundWebhookSubscriptions', COUNT(*) FROM "OutboundWebhookSubscriptions"
UNION ALL SELECT 'OutboundWebhookDeliveries', COUNT(*) FROM "OutboundWebhookDeliveries"
UNION ALL SELECT 'AgentProfiles', COUNT(*) FROM "AgentProfiles"
UNION ALL SELECT 'AgentRuns', COUNT(*) FROM "AgentRuns"
UNION ALL SELECT 'KnowledgeDocuments', COUNT(*) FROM "KnowledgeDocuments"
UNION ALL SELECT 'KnowledgeChunks', COUNT(*) FROM "KnowledgeChunks"
UNION ALL SELECT 'ExternalLogins', COUNT(*) FROM "ExternalLogins"
UNION ALL SELECT 'ApiKeys', COUNT(*) FROM "ApiKeys"
UNION ALL SELECT 'NotificationPreferences', COUNT(*) FROM "NotificationPreferences"
UNION ALL SELECT 'CommandRunLogs', COUNT(*) FROM "CommandRunLogs"
UNION ALL SELECT 'CardCommentMentions', COUNT(*) FROM "CardCommentMentions"
UNION ALL SELECT 'AgentRunEvents', COUNT(*) FROM "AgentRunEvents";
SQL
```

Verify foreign key integrity:
```bash
psql "$PGCONN" <<'SQL'
-- Cards reference valid columns
SELECT COUNT(*) AS orphaned_cards
FROM "Cards" c
LEFT JOIN "Columns" col ON c."ColumnId" = col."Id"
WHERE col."Id" IS NULL;

-- Columns reference valid boards
SELECT COUNT(*) AS orphaned_columns
FROM "Columns" col
LEFT JOIN "Boards" b ON col."BoardId" = b."Id"
WHERE b."Id" IS NULL;

-- BoardAccesses reference valid boards and users
SELECT COUNT(*) AS orphaned_accesses
FROM "BoardAccesses" ba
LEFT JOIN "Boards" b ON ba."BoardId" = b."Id"
LEFT JOIN "Users" u ON ba."UserId" = u."Id"
WHERE b."Id" IS NULL OR u."Id" IS NULL;

-- AutomationProposalOperations reference valid proposals
SELECT COUNT(*) AS orphaned_operations
FROM "AutomationProposalOperations" apo
LEFT JOIN "AutomationProposals" ap ON apo."ProposalId" = ap."Id"
WHERE ap."Id" IS NULL;
SQL
```

All orphan counts must be **zero**. If any are non-zero, the migration has data integrity issues — do not proceed to Step 5.

## Step 5: Smoke Test the Application

1. Configure the application for PostgreSQL:
   ```bash
   # Requires the follow-up runtime provider switch described in Step 1.
   # The current codebase cannot run the API against PostgreSQL yet.
   ```
2. Start the application:
   ```bash
   dotnet run --project backend/src/Taskdeck.Api
   ```
3. Verify core operations:
   - [ ] Login with an existing user
   - [ ] List boards (GET /api/boards)
   - [ ] Open a board with cards and columns
   - [ ] Create a new card
   - [ ] Move a card between columns
   - [ ] Submit a capture and verify it appears in the inbox
   - [ ] Check audit log entries are being written
   - [ ] Verify chat session loads with history

## Rollback Procedure

If the migration fails or the application does not function correctly against PostgreSQL:

1. **Stop the application** targeting PostgreSQL.
2. **Restore the SQLite configuration**:
   ```bash
   export ConnectionStrings__DefaultConnection="Data Source=taskdeck.db"
   ```
3. **Restore the SQLite backup** if the original file was modified:
   ```bash
   cp taskdeck.db.pre-migration-backup taskdeck.db
   ```
4. **Restart the application** — it will use the SQLite database.
5. **Investigate the failure** using logs and the integrity verification queries above.
6. The PostgreSQL database can be dropped and recreated for a fresh retry:
   ```bash
   psql -h <host> -U <admin_user> -c "DROP DATABASE taskdeck;"
   ```

## Known Provider Differences

These differences are handled by EF Core's provider abstraction but are worth noting:

| Concern | SQLite | PostgreSQL |
|---------|--------|------------|
| GUID storage | Text (string) | Native `uuid` type |
| DateTimeOffset | ISO 8601 text | `timestamptz` |
| String comparison | Case-sensitive by default | Case-sensitive by default (use `ILIKE` for insensitive) |
| Auto-increment | `AUTOINCREMENT` keyword | `SERIAL` / `GENERATED ALWAYS AS IDENTITY` |
| JSON columns | Text with no validation | `jsonb` with indexing support |
| Full-text search | FTS5 virtual tables | `tsvector` / `tsquery` (requires different setup) |
| Concurrency | Database-level write lock | Row-level locking |
| Max connections | Single writer | Configurable (default 100) |

**Sequence note**: the current Taskdeck schema uses GUID primary keys, so no PostgreSQL sequence reset
step is needed after import. If future tables introduce integer identity columns, add `setval(...)`
calls after the CSV import to advance those sequences to the current max values.

## Full-Text Search Migration Note

The current `KnowledgeDocuments` and `KnowledgeChunks` tables use SQLite FTS5 for full-text search. PostgreSQL uses a different FTS mechanism (`tsvector`/`tsquery`). The `IKnowledgeSearchService` interface abstracts this, so the migration requires a PostgreSQL-specific implementation of that interface — no domain or application layer changes.

Key details for the FTS migration:

- **`KnowledgeDocumentsFts`** is a SQLite FTS5 virtual table. It does **not** exist in the EF Core model and should **not** be exported or imported. It is populated by a SQLite trigger (`KnowledgeDocuments_ai`) that also does not exist in PostgreSQL.
- **Do not export** `KnowledgeDocumentsFts` — it is not a regular table and `SELECT *` on an FTS5 table may produce unexpected results.
- For PostgreSQL, full-text search can be implemented using `tsvector`/`tsquery` columns with GIN indexes, or using `pg_trgm` for simpler similarity-based search. The PostgreSQL-specific `IKnowledgeSearchService` implementation is a separate work item.
- Until the PostgreSQL FTS implementation is built, knowledge document search will be non-functional on PostgreSQL. The `KnowledgeDocuments` and `KnowledgeChunks` data tables themselves will migrate normally.

## Security Considerations

- **Never store database credentials in source control.** Use environment variables, secrets managers (AWS Secrets Manager, Azure Key Vault), or mounted secret files.
- Prefer `~/.pgpass`, `PGPASSFILE`, or a secrets-mounted appsettings file over inline passwords in shell history when running `psql` commands.
- **Use TLS for PostgreSQL connections** in production (`SslMode=Require` in the connection string).
- **Restrict the `taskdeck_app` database user** to only the permissions needed (SELECT, INSERT, UPDATE, DELETE on application tables). Do not grant `SUPERUSER` or `CREATEDB`.
- **The migration-export directory contains all application data** including password hashes. Delete it securely after a successful migration:
  ```bash
  rm -rf migration-export/
  ```
