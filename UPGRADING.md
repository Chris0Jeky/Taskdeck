# Upgrading Taskdeck

Taskdeck is local-first: **your workspace data is a single SQLite file that you own.** The packaged
Windows app also keeps generated local identity in `appsettings.local.json`; preserve that file with
the database so sessions and encrypted connector credentials remain usable. Read the section for the
version you are moving *to* before you upgrade — each one leads with whether it contains breaking
changes.

- Release notes and downloads: <https://github.com/Chris0Jeky/Taskdeck/releases>
- Configuration keys referenced below: `docs/platform/CONFIGURATION_REFERENCE.md`
- Migration mechanics for contributors: `docs/platform/EF_MIGRATION_WORKFLOW.md`

---

## Backup the database and packaged identity

Everything Taskdeck stores — boards, cards, captures, proposals, audit history, API keys — lives
in one SQLite database file. To back the workspace up, **stop Taskdeck and copy that file.** A
packaged Windows install also generates secrets in `appsettings.local.json`; copy the whole
`%LOCALAPPDATA%\Taskdeck` directory so the database and its local identity stay together.

**Where the file is**

| How you run Taskdeck | Database file |
| --- | --- |
| Supported Windows 0.1.x release | `%LOCALAPPDATA%\Taskdeck\taskdeck.db` |
| Source `dev-up` launcher | `%LOCALAPPDATA%\Taskdeck\taskdeck-dev.db` on Windows; `${XDG_DATA_HOME:-$HOME/.local/share}/taskdeck/taskdeck-dev.db` on Linux/macOS |
| Raw developer `dotnet run` | A relative path resolves from that command's working directory; use `dev-up` for the stable source path above |
| Explicit connection string | Whatever path `ConnectionStrings:DefaultConnection` points at |
| Docker Compose (`deploy/docker-compose.yml`) | `/app/data/taskdeck.db` inside the container, on the `taskdeck-db` volume |

If `FirstRun:ResolveAppDataDbPath` is left at its default, a *relative* connection-string path is
resolved into the OS app-data directory rather than the working directory — so check the startup
log if you are unsure which file is live.

**Stop Taskdeck first.** Taskdeck runs SQLite in WAL mode, so a running instance may hold recent
commits in `taskdeck.db-wal` alongside the main file. Copying only `taskdeck.db` while the app is
running can silently miss them. With every Taskdeck process stopped (API, CLI, and any MCP
server), the WAL is checkpointed away and the single `.db` file is complete. If you see leftover
`taskdeck.db-wal` / `taskdeck.db-shm` files, copy those too, or simply start and cleanly stop
Taskdeck once more.

**To restore:** stop Taskdeck and move the current `taskdeck.db` plus any `-wal`/`-shm` sidecars aside
until the backup is known-good. Put the backup set under the original names; never leave a sidecar from
a different database copy in place. Restore the matching packaged `appsettings.local.json` when
applicable, then start Taskdeck again. Do not pair an older local-config identity with a newer database
that may contain connector credentials encrypted by a different key.

### Automatic pre-migration backups

Since **v0.1.1**, Taskdeck protects the upgrade itself. When a release ships a database schema
change, the host takes a consistent snapshot of your database file **before** applying the
migration:

- Snapshots land in a `backups/` folder next to the database file, named
  `<database file name>-pre-migration-<UTC timestamp>-<sequence>.db` — for the default database
  that is `taskdeck.db-pre-migration-20260819T101530000Z-000001.db`. Each one is a complete,
  standalone SQLite file — copy it back over `taskdeck.db` to return to the pre-upgrade state.
  **The highest sequence number is the newest snapshot**; prefer it over the timestamp, which is
  only descriptive and can move backwards if the host clock is corrected.
- It runs **only when migrations are actually pending.** Ordinary restarts copy nothing.
- The last **5** snapshots are kept; older ones are pruned automatically. Only files matching the
  managed name above are ever deleted — your own copies in the same folder are left alone.
- If you tracked `main` during the v0.1.1 development window, you may also have snapshots in the
  older `taskdeck-pre-migration-<UTC timestamp>.db` shape (no sequence). Those are still
  recognised and still pruned, so they age out instead of accumulating; no released version wrote
  them, so a host upgrading from v0.1.0 will not have any.
- **If the snapshot cannot be written, the upgrade stops.** Taskdeck refuses to migrate a database
  it could not back up, and the error names the file, the directory, and the cause. Free some disk
  space or fix directory permissions, then start again.

Tune it with `Database:Backup:Enabled`, `Database:Backup:RetainCount`, and
`Database:Backup:Directory` (see `docs/platform/CONFIGURATION_REFERENCE.md`). These snapshots are
an upgrade safety net, not a backup strategy: they sit on the same disk as the database and are
only taken on schema changes. Keep your own copy somewhere else.

## Export: your data leaves whenever you want

Taskdeck ships full data export today. You are never locked in, and an export is a good thing to
take before a major upgrade.

| What | Where |
| --- | --- |
| **Full account export (GDPR JSON)** — everything associated with your account | `GET /api/account/export`, or `GET /api/account/export/stream` for large accounts |
| **Board export** — one board as JSON or CSV | `GET /api/export/boards/{boardId}/json`, `GET /api/export/boards/{boardId}` |
| **Board import** | `POST /api/import/boards/json`, `POST /api/import/boards` |
| **In the UI** | Settings → **Export & Import** (`/workspace/settings/export-import`) |

There is also a whole-database export/import pair (`GET /api/export/database`,
`POST /api/import/database`). It is a developer/sandbox convenience, not the supported migration
path — for moving a workspace, copy the database file as described above.

## General upgrade procedure

1. Read the section for your target version below. If it has a **BREAKING** entry, follow it.
2. Stop every Taskdeck process (API, CLI, MCP servers).
3. Copy `taskdeck.db` somewhere safe. Windows release users should copy the entire
   `%LOCALAPPDATA%\Taskdeck` folder so `appsettings.local.json` stays with it. (Optional but
   recommended: also take an account export.)
4. Replace the binaries / pull the new container image.
5. Start Taskdeck. Pending migrations are applied automatically on startup, after the automatic
   pre-migration snapshot.
6. Check the startup log. A failed migration or a failed backup stops startup with an explicit
   error rather than continuing in a half-upgraded state.

**Downgrading is not supported.** EF Core migrations are applied forward only; an older binary
started against a newer database may fail or behave incorrectly. To go back, restore your backup
copy of the database file alongside the older binary.

**Skipping versions** is fine. Migrations are applied in order, so upgrading from v0.1.0 straight
to a later release applies every intervening migration in one startup.

---

# Version notes

## v0.1.1 — 2026-08-21

**BREAKING (configuration):** Gemini live-provider support is removed. Before upgrading, replace
`Llm:Provider=Gemini` with `OpenAI` (or `Mock` for offline use) and remove any `Llm:Gemini`
configuration section or retired Compose wrapper. Taskdeck fails startup with fixed migration
guidance when those retired selectors remain; an ambient `GEMINI_API_KEY` no longer selects a live
provider. OpenAI live use requires the `Llm:OpenAi` configuration described in
[`docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`](docs/platform/LLM_PROVIDER_SETUP_GUIDE.md).

- **Automatic pre-migration database backup.** Taskdeck now snapshots the SQLite file before
  applying pending migrations, keeps the last `Database:Backup:RetainCount` (default 5) snapshots,
  and refuses to migrate if the snapshot cannot be written. See
  [Automatic pre-migration backups](#automatic-pre-migration-backups). No action required.
- **New configuration keys:** `Database:Backup:Enabled`, `Database:Backup:RetainCount`,
  `Database:Backup:Directory`. All optional; the defaults are the recommended settings.
- **Desktop support is Windows-only for 0.1.x.** Use the `win-x64` ZIP on Windows 10/11 x64 and
  follow its archive-local `QUICK_START.md`. The four v0.1.0 platform archives remain immutable
  historical release evidence, not a continuing cross-platform support promise.

## v0.1.0 — 2026-08-19

**BREAKING: none.** First public release, so there is nothing to upgrade from.

- First tagged open-beta release: 4-platform binaries plus a public GHCR container image.
- Starting Taskdeck for the first time creates the database and applies the full migration chain.
  Nothing is backed up on that first run because there is no prior state to protect.
- Everything under [Backup the database and packaged identity](#backup-the-database-and-packaged-identity) and
  [Export](#export-your-data-leaves-whenever-you-want) applies to v0.1.0. The *automatic*
  pre-migration snapshot arrives in v0.1.1 — when upgrading away from v0.1.0, take the manual copy
  in step 3 of the general procedure.
