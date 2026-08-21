# Taskdeck Demo Playbook

Taskdeck has a lot of capability under the hood. If you click around a fresh instance, some pages look empty because they are event-driven and only populate after specific flows.

This playbook gives you:

1. A one-command seed so the UI starts populated.
2. Scenario harness commands for repeatable demos.
3. A short stakeholder flow and an opt-in recorder.

Use [START_HERE.md](../START_HERE.md) first if you are trying to understand the product.
This playbook is for seeded demos, stakeholder walkthroughs, and regression/operator use, not the main onboarding path.

Core story:

Capture -> Triage -> Proposal -> Apply -> Board

Saul-facing recording contract:
- `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md`

## Quick Start (source-only seeded demo)

This playbook is for a source checkout. The packaged Windows release has no seeded credentials and
uses a different Production run/data contract; follow its
[archive quick start](../releases/WINDOWS_QUICK_START.md) instead. In particular, use that guide's
current-process-only OpenAI setup rather than copying demo-key shortcuts into the packaged app.

From the repository root, start the source stack and seed it with one launcher:

```powershell
.\scripts\dev-up.ps1 -Seed
```

```bash
scripts/dev-up.sh --seed
```

The launcher intentionally leaves the API and Vite frontend running in the background. It waits for
API readiness, prints the API URL, expected frontend entry point, and PIDs, and records the process
trees so the matching stop command can close both safely:

```powershell
.\scripts\dev-up.ps1 -Stop
```

```bash
scripts/dev-up.sh --stop
```

Closing the shell that launched the stack is not the documented stop path. Default URLs are:

- API: `http://localhost:5000/api`
- UI: `http://localhost:5173` (if Vite selects a fallback, its `Local:` line in the frontend
  dev-server output is authoritative)
- Health checks (not under `/api`): `http://localhost:5000/health/live` and
  `http://localhost:5000/health/ready`

The source-only seeded accounts are `demo` / `demo123` and `collab` / `demo123`. They do not exist in
the Windows release. If a launcher-owned source stack is already running, stop it with the matching
`-Stop` / `--stop` command, then rerun the seeded launcher above. The protected seeder reuses
recognised baseline artifacts instead of appending a duplicate copy on every run. Do not refresh a
running stack through the lower-level bare seed command, which has no listener-identity proof.

### Clean rehearsal reset

To restore the source-demo story after a rehearsal changes it, first stop the launcher-owned stack,
then use the protected reset-and-seed form from the repository root:

```powershell
.\scripts\dev-up.ps1 -Seed -ResetSeed
```

```bash
scripts/dev-up.sh --seed --reset-seed
```

`-ResetSeed` / `--reset-seed` is invalid without the corresponding seed flag and is never implicit.
After the launcher proves its exact run identity, the one-socket seeder authenticates the demo owner so
it can read that user's complete board list. A first-ever seed may create that source-only demo owner;
after owner authentication, an invalid reset preflight performs no board, artifact, or collaborator-account
write. Unknown, duplicate, or malformed `DEMO:*` candidates and malformed reserved tombstones fail
closed. The exact script-owned temporary name `DEMO: Client Onboarding Demo (Chat)` is recognised as
the capture-board family so an interrupted chat rename can be recovered, while duplicate and near-match
names still fail closed. Each documented demo board is atomically renamed to an ID-bound non-demo
tombstone and archived; prior valid tombstones and non-demo boards are preserved. The seeder re-fetches
and verifies that quarantine before creating four fresh canonical boards with new IDs, including a new
intentionally archived board, and verifies that persisted set before provisioning the collaborator or
seeding child artifacts. A 403 or any quarantine/fresh-state failure exits nonzero without collaborator
provisioning or artifact seeding. Because an earlier transition or fresh-board creation may already have
succeeded, inspect the remaining demo state before retrying. The launcher then cleans only its own
API/frontend process trees and does not claim that old tombstone data was physically deleted.

### Database location

The canonical source-launcher database is stable and independent of the repository working directory:

- Windows: `%LOCALAPPDATA%\Taskdeck\taskdeck-dev.db`
- Linux/macOS: `${XDG_DATA_HOME:-$HOME/.local/share}/taskdeck/taskdeck-dev.db`

`dev-up` prints the exact path and passes it only to the API process it starts. A raw developer
`dotnet run` is an alternative developer-only path: its relative `Data Source=taskdeck.db` resolves
from that command's working directory, so it is not the canonical seeded-demo database. Likewise,
`npm run demo:reset-db` targets the legacy repository-local raw-`dotnet run` database; it does not
reset the stable `dev-up` database. Lower-level reset/seed commands are not the canonical demo path
and must be confined to an isolated developer environment whose API and database you explicitly own.

Other repository-local DB files are per-purpose:

- `taskdeck.e2e*.db` — E2E test databases (Playwright)
- `taskdeck.demo*.db` — demo director/CI databases
- `backend/tests/**/taskdeck.db` — backend test databases created by test runs
- a repo-root or API-directory `taskdeck.db` — a raw developer `dotnet run` artifact, not `dev-up`

### Source startup troubleshooting

- If the launcher reports a live recorded stack, run its `-Stop` / `--stop` command before starting
  another one; it refuses to overwrite live PIDs because that would orphan the old processes.
- If the API exits before readiness, read the API window/output named by the launcher. Do not keep
  restarting over an unexamined database or configuration error.
- The launcher passes its selected API base URL and run identity to the seeder, and passes the same
  API base to Vite. General seeded demos may use PowerShell's checked `-ApiPort N` option or Bash's
  `TASKDECK_API_PORT=N ./scripts/dev-up.sh --seed` form and must use the printed URLs. Only the Saul
  rehearsal contract is deliberately restricted to port 5000. For a UI collision, use the printed
  frontend URL rather than assuming 5173.
- Confirm the printed database path before deleting or resetting anything. Stop the stack first so
  SQLite can checkpoint its WAL cleanly.

## Managed-Key Mode Disclosure

When running demos with an operator-managed, deployment-global OpenAI key, presenters should be aware:

- User chat messages and bounded transcript-source triage chunks are sent to OpenAI
- Per-user quota limits apply (default: 60 requests/hour, 100K tokens/day)
- Operator kill switches can throttle or block LLM access per user, per surface, or globally

Full policy details: `docs/security/MANAGED_KEY_USAGE_POLICY.md`

## Runtime Preconditions

- Demo scripts are local-safe by default. They target `http://localhost:5000/api` unless you override `TASKDECK_API_BASE_URL` or `TASKDECK_E2E_API_BASE_URL`.
- Non-local API targets are rejected unless you explicitly set `TASKDECK_DEMO_ALLOW_NON_LOCAL_API=1`.
- UI links and Playwright bootstrap default to `http://localhost:5173`; local fallback ports `4173` and `5001` are also supported.
- Demo harness credentials default to `demo` / `demo123` and `collab` / `demo123` unless you override the `TASKDECK_DEMO_*` / `TASKDECK_COLLAB_*` environment variables.
- Full Playwright-backed demos (`demo:director` or the opt-in stakeholder recorder) auto-enable OpenAI when LLM steps are enabled and a usable OpenAI key is present.
- An ambient `GEMINI_API_KEY` is ignored because it may belong to CLI tooling. A retired Taskdeck Gemini selector or provider-specific setting fails fast with migration guidance.
- Demo-specific live keys now take effect even when the base development environment is pinned to `Llm__Provider=Mock`; use `TASKDECK_DEMO_LLM_PROVIDER=Mock` or `TASKDECK_DEMO_DISABLE_LIVE_LLM=1` to force mock instead.
- When a full demo injects live-provider overrides, Playwright also disables existing-server reuse by default so the intended backend process is launched instead of silently inheriting a stale mock server.
- `taskdeck-chat` autopilot and scenario steps marked `requiresLlm: true` still need a usable live-provider key. Use `--skip-llm` for deterministic local or CI runs.
- `demo:director` and the stakeholder recorder require Playwright Chromium (`npx playwright install chromium`) and write access to `frontend/taskdeck-web/demo-artifacts/`.
- `demo:director:smoke` also owns a dedicated Playwright/demo database (`frontend/taskdeck-web/taskdeck.demo.ci.db`) and forces fresh backend/frontend startup so repeated runs do not inherit local `taskdeck.e2e.db` state.
- In fresh-server mode, the director keeps `http://localhost:5000/api` when it is free and otherwise auto-selects a free local API port before starting the backend.
- Unknown scenario IDs now fail fast during director/recorder setup so autopilot and walkthrough selection do not silently target the engineering board by fallback.
- Director-specific flags must appear before `--`; anything after `--` is forwarded to Playwright unchanged. Unknown director flags now fail fast instead of being silently forwarded.

## Scenario Harness

Scenario reference: [SCENARIOS.md](SCENARIOS.md)

List scenarios:

```bash
cd frontend/taskdeck-web
npm run demo:run -- --list
```

Execute a scenario in an isolated fresh-server director run:

```bash
cd frontend/taskdeck-web
npm run demo:director -- --output-dir ./demo-artifacts/engineering-sprint --e2e-db ./taskdeck.demo.engineering-sprint.db --reset-e2e-db --fresh-servers --scenario engineering-sprint --skip-llm --turns 0 --rng-seed engineering-sprint
```

Change the scenario/output/database/rng names together for another deterministic story. The lower-level
scenario and autopilot commands remain useful implementation tools, but they do not carry the
launcher's listener-identity proof and are not copyable operator paths. Their flags and schemas are
documented in [SCENARIOS.md](SCENARIOS.md).

## 5-Minute Stakeholder Flow

Saul-facing default (recording path):

1. Home
- Confirm the product teaches `Inbox -> Review -> Board`.
- Open the `DEMO: Client Onboarding Demo` board path.

2. Inbox/Capture
- Show ACME capture lineage and the proposal handoff action.

3. Review
- Confirm review-first trust cues (`nothing changes until approval`).
- Show the proposal in business wording and apply deliberately.

4. Board
- Show the clean onboarding reveal on `DEMO: Client Onboarding Demo`.

Extended walkthrough (optional):

1. Boards
- Open `DEMO: Client Onboarding Demo`.
- Explain reviewed proposals are the mutation gate.

2. Inbox
- Show ignored and triaged items.
- Follow provenance from capture item to proposal.

3. Automations -> Proposals
- Show pending/applied proposals.
- Explain review-first safety and explicit operations.

4. Notifications
- Show mention and proposal outcome notifications.

5. Activity and Ops (optional)
- Show audit/activity events.
- Show seeded Ops runs/log entries.

## MVP Dogfooding Loop

1. Capture in Inbox.
2. Start triage.
3. Review in Proposals.
4. Approve and execute.
5. Continue board execution.

## Why Some Pages Start Empty

These surfaces are event-driven:

- `Activity` needs audit events.
- `Notifications` needs mentions/proposal outcomes.
- `Ops -> Logs` needs Ops runs.
- `Access` needs board-specific entries.

Use the identity-bound `dev-up -Seed` / `dev-up.sh --seed` path before a manual walkthrough, or use
the isolated fresh-server director command above for a scenario-specific rehearsal.

## Feature Flags for Demos

`Activity`, `Ops`, `Access`, and `Archive` are default-off on first run.
Enable them in `Settings -> Feature Flags` when needed for walkthrough coverage.

## API Walkthrough (No UI)

Use:

- `demo/http/taskdeck-demo.http`

It is designed for VS Code REST Client and exercises register/login, board creation, capture triage, queue, proposals, and Ops templates.

## Stakeholder Recorder (Opt-In Playwright)

Spec:

- `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts`

Skipped by default. Run only when explicitly requested.

PowerShell:

```powershell
$env:TASKDECK_RUN_DEMO='1'
cd frontend/taskdeck-web
npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed
```

Bash:

```bash
TASKDECK_RUN_DEMO=1 npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed
```

CI policy:

- `stakeholder-demo.spec.ts` remains opt-in only. Default Playwright CI lanes set `TASKDECK_RUN_DEMO=0` and do not execute the recorder.
- Use the deterministic smoke command below for explicit regression proof instead of adding the full recorder to required CI.

## Demo Director (Scenario -> Autopilot -> Recorder -> Artifacts)

For a one-command, repeatable stakeholder run (including artifacts), use:

```bash
cd frontend/taskdeck-web

# Full run with deterministic autopilot seed
npm run demo:director -- --scenario engineering-sprint --turns 18 --brain heuristic --loop mixed --rng-seed demo-1

# Saul-facing deterministic rehearsal artifacts (no autoplay turns)
env Llm__Provider=Mock Llm__EnableLiveProviders=false Llm__AllowLiveProvidersInDevelopment=false TASKDECK_DEMO_LLM_PROVIDER=Mock TASKDECK_DEMO_DISABLE_LIVE_LLM=1 npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal

# CI-style deterministic run without LLM-required steps
npm run demo:director -- --scenario engineering-sprint --turns 12 --skip-llm --rng-seed ci-1

# Headed run if you want to watch the clickthrough
npm run demo:director -- --scenario engineering-sprint --turns 10 --headed

# Forward Playwright flags after `--`
npm run demo:director -- --scenario engineering-sprint --turns 10 -- --project=chromium

# Deterministic smoke path used for explicit CI/manual regression proof
npm run demo:director:smoke
```

Artifacts are written to:

```text
frontend/taskdeck-web/demo-artifacts/run-<timestamp>/
  README.md
  run-summary.json
  snapshot.json
  trace.ndjson
  logs/
  screenshots/
  playwright/
```

`trace.ndjson` contains structured scenario/autopilot events and is useful for debugging failed demo runs.

`demo:director:smoke` writes to `frontend/taskdeck-web/demo-artifacts/ci-smoke/`, resets `frontend/taskdeck-web/taskdeck.demo.ci.db`, auto-selects a free local API port when `5000` is occupied, and disables Playwright server reuse so artifact upload paths and seeded board state stay stable across reruns.

If startup still fails because you forced conflicting overrides, the director now prints a remediation hint that points to `TASKDECK_E2E_API_BASE_URL` and `TASKDECK_E2E_FRONTEND_PORT`.

When LLM steps are enabled, the full director flow will automatically pass live-provider settings to the backend web server when a usable demo key is present. Smoke runs still stay deterministic because `--skip-llm` suppresses that auto-enable path.

If you override the board name (`--autopilot-board` or equivalent env), the recorder walkthrough now follows that same selected board instead of falling back to the scenario default board during the UI clickthrough.

## Demo CI Policy

- Required CI and nightly Playwright lanes stay focused on baseline product regressions and explicitly keep the stakeholder recorder off.
- `ci-extended.yml` exposes `demo-director-smoke` as an opt-in lane via `workflow_dispatch` or a PR labeled `automation` when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`.
- Full demo walkthrough recording stays manual/headed by default; use `TASKDECK_RUN_DEMO=1` only when you intentionally want the recorder.

## Constraints

Treat these as advanced or diagnostic surfaces in MVP demos:

- Ops
- Activity
- Access
- Archive

Primary narrative remains capture-to-proposal with explicit review.
