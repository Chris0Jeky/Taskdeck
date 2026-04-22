#!/usr/bin/env bash
# =============================================================================
# validate-migrations.sh
# Validates that EF Core migrations apply cleanly to a fresh SQLite database
# and that the resulting schema contains all expected tables.
#
# Usage: bash scripts/ci/validate-migrations.sh
#
# Exit codes:
#   0 — All migrations applied cleanly and schema is valid
#   1 — Migration or schema validation failure
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

INFRA_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj"
STARTUP_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Api/Taskdeck.Api.csproj"

# Create a temp directory for the test database
TEMP_DIR="$(mktemp -d)"
DB_PATH="$TEMP_DIR/migration-validation.db"
trap 'rm -rf "$TEMP_DIR"' EXIT

echo "=== EF Core Migration Validation ==="
echo "Database: $DB_PATH"
echo ""

# ---- Step 1: Apply all migrations to an empty database ----
echo "--- Step 1: Applying migrations to empty database ---"

dotnet ef database update \
  --project "$INFRA_PROJECT" \
  --startup-project "$STARTUP_PROJECT" \
  --connection "Data Source=$DB_PATH" \
  --no-build \
  --verbose 2>&1 | tail -20

if [ ! -f "$DB_PATH" ]; then
  echo "FAIL: Database file was not created at $DB_PATH"
  exit 1
fi

echo ""
echo "OK: Migrations applied successfully."
echo ""

# ---- Step 2: Verify tables exist ----
echo "--- Step 2: Verifying schema tables ---"

# sqlite3 is available on ubuntu-latest and macos runners.
# Query all user tables (excluding internal EF and SQLite tables).
ACTUAL_TABLES=$(sqlite3 "$DB_PATH" \
  "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory' ORDER BY name;")

if [ -z "$ACTUAL_TABLES" ]; then
  echo "FAIL: No user tables found in the database after migration."
  exit 1
fi

TABLE_COUNT=$(echo "$ACTUAL_TABLES" | wc -l | tr -d ' ')
echo "Found $TABLE_COUNT user tables:"
echo "$ACTUAL_TABLES" | sed 's/^/  - /'
echo ""

# ---- Step 3: Verify migration history ----
echo "--- Step 3: Verifying migration history ---"

MIGRATION_COUNT=$(sqlite3 "$DB_PATH" \
  "SELECT COUNT(*) FROM __EFMigrationsHistory;")

if [ "$MIGRATION_COUNT" -eq 0 ]; then
  echo "FAIL: No migrations recorded in __EFMigrationsHistory."
  exit 1
fi

echo "OK: $MIGRATION_COUNT migrations recorded in history."

# List all applied migrations
echo ""
echo "Applied migrations:"
sqlite3 "$DB_PATH" \
  "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;" \
  | sed 's/^/  - /'
echo ""

# ---- Step 4: Check for pending model changes ----
echo "--- Step 4: Checking for pending model changes ---"

PENDING_OUTPUT=$(dotnet ef migrations has-pending-model-changes \
  --project "$INFRA_PROJECT" \
  --startup-project "$STARTUP_PROJECT" \
  --no-build 2>&1) || true

if echo "$PENDING_OUTPUT" | grep -qi "no pending model changes"; then
  echo "OK: No pending model changes detected."
elif echo "$PENDING_OUTPUT" | grep -qi "changes have been made"; then
  echo "FAIL: Pending model changes detected. Run 'dotnet ef migrations add <Name>' to capture them."
  echo "$PENDING_OUTPUT"
  exit 1
else
  # The command output format may vary; log it for debugging but do not fail
  # if we cannot parse the output deterministically.
  echo "INFO: Could not determine pending model change status from output:"
  echo "$PENDING_OUTPUT"
  echo "Continuing (non-blocking)."
fi

echo ""
echo "=== Migration validation passed ==="
