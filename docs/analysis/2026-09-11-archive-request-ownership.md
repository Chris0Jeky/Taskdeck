# Archive confirmation request ownership

Date: 2026-09-11. Issue: GH-2996. Base: `4c479a7ff311912ffb96da027f6e525521bb70df`.
Status: proposed repair; native frontend and browser qualification outstanding.

## Defect and contract

`CardArchiveAction.refreshChildren` previously assigned its response after the user dismissed the confirmation. A late success reopened the dialog; a late failure replaced the retained error and could move focus back after the user moved on.

The repair gives the component two small ownership counters. Card identity changes and unmount invalidate the component context. Escape, backdrop dismissal and Cancel additionally invalidate the confirmation. Every preview success/failure checks both owners after its await. Pending focus recovery checks ownership after `nextTick`, not just before scheduling. An old context cannot clear a newer context's busy state.

Dismissal is not cancellation of a submitted archive. A successful write for the same card still emits its existing completion receipt. A failed write still reports an error, but cannot reclaim focus after dismissal. Writes that settle after a card switch or unmount cannot notify an obsolete parent. A same-ID prop replacement does not invalidate a legitimate write receipt: the watcher compares board/card IDs separately rather than comparing a freshly allocated array.

No endpoint, payload, permission rule, expected-state token, retry behavior or confirmation wording changes. Escape remains unconditional. Current-dialog refresh continues to require a second explicit Confirm; no automatic board mutation is introduced.

## Regression coverage

`CardArchiveAction.requestOwnership.spec.ts` adds eight native component cases: late refresh success/failure after Escape; submitted write success/failure after Escape; old preview success/failure during a card switch with a newer request pending; same-card prop replacement; and unmount before write completion. The existing `CardArchiveAction.spec.ts` remains unchanged and retains ordinary confirmation, recovery, focus-cycle, and restore controls.

A separate dependency-free diagnostic executed the actual TypeScript script-setup functions with stubbed reactivity/lifecycle adapters on Node 22.16.0. Against the source blob `1b28bb1b3a22f9e56bda90da591cfcd3ab0b7c4a`, eight of twelve assertions failed. Against the repair, twelve of twelve passed. This is a script-state probe, NOT a mounted Vue, Vitest, browser, screen-reader, or full application result; it is not a replacement CI gate. Its source and TAP outputs are retained in the accompanying session evidence bundle.

## Required qualification

From `frontend/taskdeck-web`:

```sh
npx vitest --run --maxWorkers=2 src/tests/components/CardArchiveAction.spec.ts src/tests/components/CardArchiveAction.requestOwnership.spec.ts src/tests/components/BoardCardArchive.spec.ts
npm run typecheck
npm run build
npx vitest --run --maxWorkers=2
```

From the repository root:

```sh
node scripts/check-docs-governance.mjs
node scripts/check-doc-links.mjs
```

The editing environment had no installed Vue/Vitest dependencies, no .NET SDK and no direct repository/package-network access. Therefore those commands and real-browser Paper/Legacy journeys were NOT RUN locally. Exact-head hosted results and independent review remain necessary before merge; do not read the diagnostic's green result as that qualification.

## Boundaries and handoff

GH-2969 still owns preserving newer unsaved card drafts when a committed archive causes its parent editor to close. GH-2997 still owns assignment-save interactions with page-level Refresh/Delete. Neither is solved here. Server-side writes already submitted are not rolled back by dismissal or unmount.

Canonical STATUS and MASTERPLAN were not changed: this is an unmerged draft, and the docs rule reserves them for shipped reality and delivery history. The landing coordinator should add a bounded archive-recovery status note after qualification. No human-action item in OUTSTANDING_TASKS.md was checked off; release trust, private-instance, benefits/distribution, and CI control-plane decisions remain with their existing owners.
