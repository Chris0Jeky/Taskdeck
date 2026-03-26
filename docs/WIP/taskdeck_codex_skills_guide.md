# Taskdeck Codex Skills Guide

## Why these skills

Taskdeck already has strong always-on repository instructions in `AGENTS.md`, explicit MCP rules in `docs/MCP_TOOLING_GUIDE.md`, a project-scoped Codex config in `.codex/config.toml`, and authoritative product/testing docs in `docs/STATUS.md`, `docs/START_HERE.md`, and `docs/TESTING_GUIDE.md`.

That means the missing layer is not more global rules. It is **situational playbooks** for recurring Taskdeck work:

- orientation and planning
- backend slices
- frontend/product-legibility slices
- capture/review/board loop work
- demo and regression evidence
- verification and canonical doc sync

Use `AGENTS.md` for repo laws. Use these skills for heavier workflows that should only load when relevant.

## Review outcome

The six-skill shape is right for Taskdeck, but the implementation should be a little tighter than the first draft:

- make `taskdeck-repo-onramp` read `docs/ISSUE_EXECUTION_GUIDE.md` and `docs/MCP_TOOLING_GUIDE.md` as first-class inputs
- keep `taskdeck-frontend-workspace-slice` focused on shell, navigation, and workspace coherence
- keep `taskdeck-capture-review-loop` focused on capture, proposal review, provenance, execute flow, and board handoff semantics
- add a short `do not use this skill when` rule to reduce double-loading
- prefer the smallest evidence path first in demo/regression work
- update `STATUS.md` and `IMPLEMENTATION_MASTERPLAN.md` only when shipped reality or sequencing actually changed

---

## Recommended layout

```text
.codex/
  config.toml
  skills/
    taskdeck-repo-onramp/
      SKILL.md
    taskdeck-backend-slice/
      SKILL.md
    taskdeck-frontend-workspace-slice/
      SKILL.md
    taskdeck-capture-review-loop/
      SKILL.md
    taskdeck-demo-regression/
      SKILL.md
    taskdeck-verification-doc-sync/
      SKILL.md
```

---

## Small AGENTS.md add-on

Add a short section near the top-level work protocol so Codex knows these skills exist and when to use them.

```md
## Local skill packs
- Use `.codex/skills/taskdeck-repo-onramp` when starting work in an unfamiliar Taskdeck area or when the current roadmap/thesis needs to be reconciled before planning.
- Use `.codex/skills/taskdeck-backend-slice` for backend/API/application/infrastructure changes.
- Use `.codex/skills/taskdeck-frontend-workspace-slice` for frontend/product-shell/workspace UX changes outside the core capture/review semantics.
- Use `.codex/skills/taskdeck-capture-review-loop` for any work touching capture, inbox, proposal review, execute flow, provenance, or board handoff.
- Use `.codex/skills/taskdeck-demo-regression` when a task needs seeded demo evidence, Playwright validation, or stakeholder walkthrough proof.
- Use `.codex/skills/taskdeck-verification-doc-sync` before handoff for final verification, canonical docs updates, and the required Taskdeck work summary.
```

---

## Skill 1: `.codex/skills/taskdeck-repo-onramp/SKILL.md`

```md
# Taskdeck Repo Onramp

Use this skill when:
- starting a new Taskdeck session
- entering an unfamiliar area of the repo
- planning a change that may conflict with current roadmap or shipped reality
- converting a broad request into a scoped implementation slice

## Goal
Establish the current Taskdeck truth before editing anything.

## Read first
1. `AGENTS.md`
2. `docs/STATUS.md`
3. `docs/IMPLEMENTATION_MASTERPLAN.md`
4. `docs/GOLDEN_PRINCIPLES.md`
5. `docs/ISSUE_EXECUTION_GUIDE.md`
6. `docs/MCP_TOOLING_GUIDE.md`
7. `docs/TESTING_GUIDE.md`

Read `docs/START_HERE.md` when the task is product-facing or UX-facing.

## What to extract
Produce a short working summary for yourself covering:
- current product thesis
- shipped loop vs planned surfaces
- current near-horizon priorities
- constraints and guardrails that matter to this task
- likely layers/files/tests affected
- whether docs must change if implementation changes

## Taskdeck-specific framing
Treat these as non-negotiable unless the task explicitly changes them:
- capture should stay near-zero friction
- automation remains review-first
- no silent/destructive apply by default
- novice-first product legibility beats breadth for near-horizon work
- active docs beat archive docs when they conflict

## Planning output
Before edits, write a short plan with:
- files likely touched
- risks
- tests to run
- docs likely requiring sync

## Stop and reframe if
- the proposed change conflicts with `docs/STATUS.md`
- the request pushes a broad autonomy surface while current docs say product legibility comes first
- the task would silently bypass proposal-first trust

## Deliverables for the calling task
Return to the main task with:
- concise repo-state summary
- scoped execution plan
- explicit assumptions
```

---

## Skill 2: `.codex/skills/taskdeck-backend-slice/SKILL.md`

```md
# Taskdeck Backend Slice

Use this skill when:
- changing backend API, application, domain, infrastructure, workers, or auth behavior
- adding/changing endpoints
- modifying LLM provider behavior, queueing, automation proposals, notifications, archive, ops, or import/export paths

## Goal
Implement backend changes in Taskdeck without breaking clean layering, auth/error contracts, or deterministic local/test posture.

## Read first
1. `AGENTS.md`
2. `docs/STATUS.md`
3. `docs/TESTING_GUIDE.md`
4. `docs/GOLDEN_PRINCIPLES.md`
5. any feature-specific docs for the touched slice

## Architecture guardrails
Respect the existing layered structure:
- `Taskdeck.Domain`: pure core rules/entities
- `Taskdeck.Application`: use cases/services
- `Taskdeck.Infrastructure`: persistence/adapters
- `Taskdeck.Api`: HTTP endpoints/integration wiring

Do not leak infrastructure concerns into Domain.
Do not bypass application-layer contracts just to make controller work easier.

## Behavioral guardrails
- keep claims-first identity and authz intact
- preserve `ApiErrorResponse` style behavior and stable `401/403/404/409` semantics
- do not trust caller-supplied actor identity
- handle failure branches explicitly
- keep local/test runs deterministic; prefer mock providers unless the task explicitly needs live-provider behavior

## Backend workflow
1. Identify the exact layer boundary where the change belongs.
2. Search for existing patterns before inventing a new one.
3. Implement the smallest cohesive slice.
4. Add or update tests in the nearest appropriate test project.
5. Run targeted tests first, then broader backend verification if surface area justifies it.

## Test routing
Use the smallest set that still proves the change:
- domain logic -> `Taskdeck.Domain.Tests`
- application/service rules -> `Taskdeck.Application.Tests`
- HTTP contracts/authz/error mapping -> `Taskdeck.Api.Tests`
- CLI behavior -> `Taskdeck.Cli.Tests`
- architecture constraints -> `Taskdeck.Architecture.Tests`

## Common commands
- `dotnet test backend/Taskdeck.sln -c Release -m:1`
- targeted project test commands from `docs/TESTING_GUIDE.md`

If provider behavior is involved, force deterministic mock mode unless live-provider testing is the explicit task.

## Done checklist
- layer placement is clean
- authz and error behavior are explicit
- tests cover happy path plus meaningful failure path
- docs are flagged for update if shipped reality changed
```

---

## Skill 3: `.codex/skills/taskdeck-frontend-workspace-slice/SKILL.md`

```md
# Taskdeck Frontend Workspace Slice

Use this skill when:
- changing Vue components, stores, routes, shell navigation, help states, board flows, review UX, Home/Today/Inbox/Boards surfaces, or keyboard/accessibility behavior
- improving product legibility, navigation clarity, or empty states

## Goal
Make frontend changes that strengthen the shipped Taskdeck product loop instead of adding disconnected UI.

## Read first
1. `docs/STATUS.md`
2. `docs/START_HERE.md`
3. `docs/TESTING_GUIDE.md`
4. relevant product/manual docs under `docs/product` and `docs/manual`
5. `frontend/taskdeck-web/package.json`

## Product framing
Prefer changes that reinforce the current shipped path:
- `Home -> Inbox/capture -> Review -> Board`
- `Today` as daily reset and action routing
- advanced surfaces remain secondary unless the task explicitly targets them

When the task changes capture, proposal review, provenance, or explicit board handoff semantics, pair this skill with the capture-review skill instead of letting the two blur together.

## Frontend guardrails
- preserve board-centered continuity across routes
- preserve review-first trust posture in UI copy and actions
- favor readable, discoverable, novice-first states
- keep keyboard behavior and escape-flow sane
- avoid adding surfaces that claim capabilities not actually shipped

## Workflow
1. Identify which product surface is primary and which routes/stores/components support it.
2. Check whether the requested change strengthens or weakens product legibility.
3. Reuse existing patterns before adding new state or abstractions.
4. Add or update unit tests for components/stores/composables.
5. Use Playwright when the change affects route flow, keyboard behavior, or multi-step UX.

## Common commands
From `frontend/taskdeck-web`:
- `npm run lint`
- `npx vitest --run`
- `npm run typecheck`
- `npm run build`
- `npx playwright test --reporter=line`

## Done checklist
- UX stays aligned with current product thesis
- empty/help states point users toward the loop
- tests cover behavior, not just rendering
- if the shipped UX meaning changed, docs/help/manual content is flagged for sync
```

---

## Skill 4: `.codex/skills/taskdeck-capture-review-loop/SKILL.md`

```md
# Taskdeck Capture Review Loop

Use this skill when:
- touching capture, inbox, triage, automation proposals, proposal summaries, execution, provenance, or board handoff
- working on anything that could affect the core trust loop

## Goal
Protect the central Taskdeck loop:
`capture -> review -> apply explicitly -> continue on board`

## Read first
1. `docs/STATUS.md`
2. `docs/START_HERE.md`
3. `docs/TESTING_GUIDE.md`
4. any capture/review/product docs for the touched flow

## Non-negotiable guardrails
- no silent board mutation from triage/model output
- review remains the trust gate
- provenance should remain visible and navigable
- capture should remain low-friction
- resulting UX should make the loop easier to understand, not more system-shaped

## Evaluation questions
Before changing code, answer:
- does this reduce or increase capture friction?
- does this keep proposal review explicit?
- does this preserve provenance from capture to proposal to board/card?
- does this improve the user’s understanding of what happened?

## Recommended verification
Prefer a mix of:
- targeted backend/frontend tests for the touched slice
- Playwright coverage for the end-to-end flow when route/interaction behavior changes
- manual sanity check of the golden path if the change is user-facing

## Strong regression candidates
- capture creation
- triage enqueue
- proposal rendering and navigation
- approve/reject/execute
- provenance deep-links
- board/card result after execution

## Done checklist
- proposal-first trust preserved
- provenance preserved
- copy and UI reinforce the loop
- tests prove the loop did not regress
```

---

## Skill 5: `.codex/skills/taskdeck-demo-regression/SKILL.md`

```md
# Taskdeck Demo Regression

Use this skill when:
- you need seeded demo evidence
- you need to validate UI behavior with Taskdeck’s demo harness
- you need stakeholder-friendly proof, screenshots, or reproducible walkthrough state
- a change touches the golden path or first-run/product-legibility surfaces

## Goal
Use Taskdeck’s existing seeded/demo tooling as evidence, not as a substitute for product truth.

## Read first
1. `docs/TESTING_GUIDE.md`
2. `docs/START_HERE.md`
3. `docs/product/DEMO_PLAYBOOK.md`
4. `docs/product/SCENARIOS.md`
5. `frontend/taskdeck-web/package.json`

## Key principle
Demo tooling supports proof and repeatability. It does not replace the required product smoke path.

## Evidence ladder
Prefer the smallest path that proves the change:
1. targeted unit/integration tests
2. targeted Playwright coverage
3. `npm run demo:director:smoke`
4. full/manual seeded demo only when stakeholder-proof is actually needed

## Primary commands
From `frontend/taskdeck-web`:
- `npm run demo:seed`
- `npm run demo:director:smoke`
- `npx playwright test --reporter=line`

Use the deterministic smoke path first when you need stable evidence.
Use full/manual demo runs only when the task actually needs richer walkthrough behavior.

## When to use Playwright
Use Playwright for:
- route flow regressions
- keyboard/interaction behavior
- evidence for user-facing state changes
- golden-path validation

## Evidence expectations
Capture enough evidence for handoff:
- commands run
- pass/fail result
- screenshots only when useful
- note whether run used seeded data, smoke path, or full/manual demo path

## Done checklist
- evidence matches the actual changed surface
- demo harness choice was deliberate
- required smoke/regression path was not skipped for convenience
```

---

## Skill 6: `.codex/skills/taskdeck-verification-doc-sync/SKILL.md`

```md
# Taskdeck Verification and Doc Sync

Use this skill when:
- implementation is complete
- preparing final handoff
- deciding which docs must change because repo reality changed

## Goal
Finish Taskdeck work properly: verify behavior, update canonical docs when needed, and report the work in the repo’s expected format.

## Read first
1. `AGENTS.md`
2. `docs/STATUS.md`
3. `docs/TESTING_GUIDE.md`
4. `docs/IMPLEMENTATION_MASTERPLAN.md`
5. `docs/MANUAL_TEST_CHECKLIST.md` if manual validation is relevant

## Verification workflow
1. Run targeted tests for the touched area.
2. Run broader checks if the blast radius justifies them.
3. Decide whether shipped reality changed.
4. Update canonical docs if it did.
5. Prepare final work summary in the repo’s expected shape.

## Canonical doc update rule
If reality changed, update the right active docs rather than burying truth in a PR comment.
Likely candidates:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- relevant product/manual docs

Use a stricter rule for the two canonical planning docs:
- update `docs/STATUS.md` only when shipped product or engineering reality changed
- update `docs/IMPLEMENTATION_MASTERPLAN.md` only when the active roadmap or sequencing changed
- for local tooling, skill-pack, or narrow workflow guidance changes, prefer `AGENTS.md` or the directly affected doc instead

## Required handoff shape
Provide:
- summary of changes
- files touched
- tests added/updated
- commands run and results
- docs updated
- notable risks or follow-ups

## Do not claim
- a feature is shipped if only demo tooling changed
- a path is verified if you only reasoned about it
- docs are current if implementation changed and canonical docs were left stale
```

---

## Suggested implementation order

1. add the six skill folders and `SKILL.md` files
2. add the small `AGENTS.md` routing section
3. keep the skills lean; do not duplicate large chunks of `STATUS.md`
4. after one or two sessions, trim any skill that is too generic or too large

---

## What not to do

- do not move all of `AGENTS.md` into skills
- do not create a separate skill for every tiny subsystem yet
- do not duplicate huge repo docs inside every skill
- do not make a skill that is just “run tests” with no Taskdeck-specific judgment

---

## Best next refinement after this

After these land, the next good wave is usually:
- one issue-seeding / backlog-shaping skill
- one authz/error-contract backend hardening skill
- one first-run / novice-legibility frontend skill

Only add those once the six core skills are actually being used.
