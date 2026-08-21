# Saul-Facing Demo Rehearsal Contract

Last Updated: 2026-08-21

## Purpose

Define one repeatable pre-recording contract for the Saul-facing demo story:

`Home -> Inbox/Capture -> Review -> Board`

This is an operator guide for deterministic rehearsal, not a marketing script.

## Scope

In scope:
- exact reset/bootstrap commands
- exact scenario and capture input
- per-stage proof points
- artifact expectations
- pass/fail checks

Out of scope:
- broad demo-tour coverage (`Ops`, `Activity`, `Access`, `Archive`)
- new architecture or feature expansion
- public GTM/landing narrative (`#216`)

This rehearsal is local product evidence only. It does not authorize or prove a public release.
The clean-machine Windows walkthrough, Explorer/shortcut/default-browser behavior, and
SmartScreen disposition remain open human gates (`#1242`, `#1876`, and `#1877`); do not check
them off from this rehearsal.

## Preconditions

- run the launcher from the repository root; it owns both backend and frontend processes
- the canonical Saul rehearsal is default-port only: the launcher's `API` line must report `http://localhost:5000`
- if the launcher selects or is given another API port, stop; do not continue this rehearsal against it
- demo credentials are valid (`demo` / `demo123` unless overridden)
- the commands below scope deterministic Mock/live-disabled settings to a child process tree; they
  do not display, read, persist, or overwrite any provider key value

## Canonical Bootstrap

From the repository root, choose the command for the current shell. Each command scopes only the
non-secret provider selectors to a child process; no interactive-shell setting or configuration file
is changed.

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -Command '& {
  $env:Llm__Provider = "Mock"
  $env:Llm__EnableLiveProviders = "false"
  $env:Llm__AllowLiveProvidersInDevelopment = "false"
  & ".\scripts\dev-up.ps1" -Seed
}'
```

```bash
env \
  Llm__Provider=Mock \
  Llm__EnableLiveProviders=false \
  Llm__AllowLiveProvidersInDevelopment=false \
  ./scripts/dev-up.sh --seed
```

Continue only after the launcher prints `Stack is up.` and its `API` line reports
`http://localhost:5000`. The launcher passes a fresh run identity to the seeder and binds
every seed request to the API process it started. Do not replace this step with bare
`npm run demo:seed` or `npm run demo:run`; those commands do not carry the launcher-owned
connection proof. If port 5000 is unavailable, stop the launcher, free the port, and rerun this
default-port contract rather than switching the rehearsal to another listener.

This is the canonical rehearsal state:
- board: `DEMO: Client Onboarding Demo`
- starter pack: `board-blueprint-client-onboarding`
- capture source text:

```text
New client onboarding - ACME Ltd

- Request director ID documents
- Send engagement letter
- Ask for prior year accounts
- Request bookkeeping / software access
- Schedule onboarding call
- Confirm which records are still missing
- Prepare internal review once documents arrive
```

## Artifact Capture Command (Recommended)

For a deterministic artifact bundle before recording, run the npm command from its declared
working directory, `frontend/taskdeck-web`. The wrappers below keep the Mock/live-disabled posture
on the director, Playwright, and fresh-server process tree without exposing or changing any key
value.

```bash
env \
  Llm__Provider=Mock \
  Llm__EnableLiveProviders=false \
  Llm__AllowLiveProvidersInDevelopment=false \
  TASKDECK_DEMO_LLM_PROVIDER=Mock \
  TASKDECK_DEMO_DISABLE_LIVE_LLM=1 \
  bash -c 'cd frontend/taskdeck-web && exec npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal'
```

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -Command '& {
  $env:Llm__Provider = "Mock"
  $env:Llm__EnableLiveProviders = "false"
  $env:Llm__AllowLiveProvidersInDevelopment = "false"
  $env:TASKDECK_DEMO_LLM_PROVIDER = "Mock"
  $env:TASKDECK_DEMO_DISABLE_LIVE_LLM = "1"
  Set-Location ".\frontend\taskdeck-web"
  npm.cmd run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal
}'
```

Expected artifact root:
- `frontend/taskdeck-web/demo-artifacts/saul-rehearsal/`

Required files:
- `run-summary.json`
- `trace.ndjson`
- `snapshot.json`
- `logs/playwright.log`

## Stage Checks

| Stage | Operator Action | Pass Criteria |
| --- | --- | --- |
| Home | Open `/workspace/home`. | Hero and next-step cues make `Inbox -> Review -> Board` explicit; `DEMO: Client Onboarding Demo` is visible as the intentional demo board. |
| Inbox/Capture | Open `/workspace/inbox`. | ACME capture lineage is visible and proposal handoff is legible (`Open in Review` / ready-for-review wording). |
| Review | Open `/workspace/review`. | Trust-first cue is visible (`Changes stay in review until you approve them.`); proposal language is business-legible (task-card creation from captured note); actions are explicit (`Approve for board`, `Apply to board`). |
| Board | Open `DEMO: Client Onboarding Demo`. | `Demo board` cue is visible; onboarding tasks are present with clean titles; board state reads as intentional operational work, not test noise. |

## Proposal/Board Acceptance Detail

- review does not imply auto-apply; approval remains explicit
- applying the proposal yields visible onboarding tasks on the demo board
- created task titles map to ACME capture bullets
- no malformed titles and no accidental duplicate burst in clean-run rehearsal

## Failure Handling

If rehearsal fails, use this order:

1. Stop only the launcher-owned stack with `.\scripts\dev-up.ps1 -Stop` or `./scripts/dev-up.sh --stop`.
2. Confirm port 5000 is free, then rerun the matching `dev-up` seed command above; do not fall back to bare `demo:seed` or `demo:run`.
3. Confirm backend auto-processing is enabled for local Development (`Workers:EnableAutoQueueProcessing=true`, or env `Workers__EnableAutoQueueProcessing=true`).
4. Confirm the launcher's `API` line again reports exactly `http://localhost:5000` before opening the rehearsal.
5. If using director artifacts, rerun with `--fresh-servers --reset-e2e-db` to clear stale server/db state.
6. Treat any missing trust cue, missing ACME lineage, or unclear board reveal as fail-no-recording.

## Pass/Fail Checklist

- [ ] The launcher seed completed without errors and its `API` line reported `http://localhost:5000`.
- [ ] Canonical board is `DEMO: Client Onboarding Demo`.
- [ ] ACME capture text is present and maps to proposal output.
- [ ] Review screen shows explicit trust-first gating language.
- [ ] Proposal wording is stakeholder-legible and board-oriented.
- [ ] Applying proposal produces clean onboarding tasks on the demo board.
- [ ] Artifact bundle includes `run-summary.json`, `trace.ndjson`, `snapshot.json`, and logs.
- [ ] No out-of-scope fallback narrative was needed to explain the core loop.

## Related References

- `docs/WIP/Taskdeck_Demo_Capability_Specification.md`
- `docs/analysis/2026-03-26_saul-demo-capability-reconciliation.md`
- `docs/product/DEMO_PLAYBOOK.md`
- `docs/TESTING_GUIDE.md`
