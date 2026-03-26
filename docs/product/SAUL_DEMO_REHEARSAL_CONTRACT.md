# Saul-Facing Demo Rehearsal Contract

Last Updated: 2026-03-26

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

## Preconditions

- backend API is reachable at `http://localhost:5000/api`
- frontend is running from `frontend/taskdeck-web`
- demo credentials are valid (`demo` / `demo123` unless overridden)
- local run uses deterministic/mock-safe mode (`--skip-llm`)

## Canonical Bootstrap

Run from `frontend/taskdeck-web`:

```bash
npm run demo:seed
npm run demo:run -- --clean --skip-llm client-onboarding
```

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

For a deterministic artifact bundle before recording:

```bash
npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal
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

1. Rerun bootstrap commands exactly (`demo:seed` then `demo:run -- --clean --skip-llm client-onboarding`).
2. Confirm backend auto-processing is enabled for local Development (`Workers:EnableAutoQueueProcessing=true`, or env `Workers__EnableAutoQueueProcessing=true`).
3. Confirm API target is local and reachable (`http://localhost:5000/api` unless explicitly overridden).
4. If using director artifacts, rerun with `--fresh-servers --reset-e2e-db` to clear stale server/db state.
5. Treat any missing trust cue, missing ACME lineage, or unclear board reveal as fail-no-recording.

## Pass/Fail Checklist

- [ ] Bootstrap commands completed without errors.
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
