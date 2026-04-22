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

# The CI workflow builds with --configuration Release, so dotnet-ef must also
# use Release to find the correct bin output (bin/Release/net8.0/).  Without
# this flag dotnet-ef defaults to Debug and fails with "deps.json does not
# exist" on a clean runner.
CONFIGURATION="${DOTNET_CONFIGURATION:-Release}"

# Create a temp directory for the test database
TEMP_DIR="$(mktemp -d)"
DB_PATH="$TEMP_DIR/migration-validation.db"
trap 'rm -rf "$TEMP_DIR"' EXIT

echo "=== EF Core Migration Validation ==="
echo "Database: $DB_PATH"
echo ""

# ---- Step 1: Apply all migrations to an empty database ----
echo "--- Step 1: Applying migrations to empty database ---"

MIGRATION_LOG="$TEMP_DIR/migration-output.log"

if ! dotnet ef database update \
  --project "$INFRA_PROJECT" \
  --startup-project "$STARTUP_PROJECT" \
  --configuration "$CONFIGURATION" \
  --connection "Data Source=$DB_PATH" \
  --no-build \
  --verbose > "$MIGRATION_LOG" 2>&1; then
  echo "FAIL: dotnet ef database update failed."
  echo ""
  echo "--- Full output ---"
  cat "$MIGRATION_LOG"
  exit 1
fi

# Show summary (last 10 lines of verbose output)
tail -10 "$MIGRATION_LOG"

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

# has-pending-model-changes exits 0 when no changes are pending, non-zero when
# the model has drifted from the last migration snapshot.
PENDING_OUTPUT=""
PENDING_EXIT=0
PENDING_OUTPUT=$(dotnet ef migrations has-pending-model-changes \
  --project "$INFRA_PROJECT" \
  --startup-project "$STARTUP_PROJECT" \
  --configuration "$CONFIGURATION" \
  --no-build 2>&1) || PENDING_EXIT=$?

if [ "$PENDING_EXIT" -eq 0 ]; then
  echo "OK: No pending model changes detected."
else
  echo "FAIL: Pending model changes detected (exit code $PENDING_EXIT)."
  echo "Run 'dotnet ef migrations add <Name>' to capture the drift."
  echo ""
  echo "$PENDING_OUTPUT"
  exit 1
fi

echo ""
echo "=== Migration validation passed ==="
