# Archive confirmation request ownership

Date: 2026-09-11 (repair), 2026-09-12 (native qualification). Issue: GH-2996.
Original base: `4c479a7ff311912ffb96da027f6e525521bb70df`; re-based on `main` after GH-2969/PR #3015 landed the
`archived` prop override in the same component.
Original qualification: native Vitest suite, typecheck and build. GH-3033 adds a targeted Chromium
focus regression below; full Paper/Legacy journeys and screen-reader qualification remain unrun.

## Defect and contract

`CardArchiveAction.refreshChildren` previously assigned its response after the user dismissed the confirmation. A late success reopened the dialog; a late failure replaced the retained error and could move focus back after the user moved on.

The repair gives the component two small ownership counters. Card identity changes and unmount invalidate the component context. Escape, backdrop dismissal and Cancel additionally invalidate the confirmation. Every preview success/failure checks both owners after its await. Pending focus recovery checks ownership after `nextTick`, not just before scheduling. An old context cannot clear a newer context's busy state.

Dismissal is not cancellation of a submitted archive. A successful write for the same card still emits its existing completion receipt. A failed write for the same card reports an error; after dismissal it rescues body, removed or disabled focus, while leaving a usable newly focused control alone. Writes that settle after a card switch or unmount cannot emit events to an obsolete parent. A failed write after a card switch now creates a generic persistent toast without old-card content or changes to the new editor; unmount stays silent. A same-ID prop replacement does not invalidate a legitimate write receipt: the watcher compares board/card IDs separately rather than comparing a freshly allocated array.

No endpoint, payload, permission rule, expected-state token, retry behavior or confirmation wording changes. Escape remains unconditional. Current-dialog refresh continues to require a second explicit Confirm; no automatic board mutation is introduced.

## Regression coverage

`CardArchiveAction.requestOwnership.spec.ts` adds eight native component cases: late refresh success/failure after Escape; submitted write success/failure after Escape; old preview success/failure during a card switch with a newer request pending; same-card prop replacement; and unmount before write completion. The existing `CardArchiveAction.spec.ts` remains unchanged and retains ordinary confirmation, recovery, focus-cycle, and restore controls.

The original draft was written without installed frontend dependencies and carried only a dependency-free
Node probe with stubbed reactivity adapters. That probe is superseded and is **not** evidence for this
change; every claim below comes from the real suite.

All eight cases execute and pass under Vitest 5.0.0 with happy-dom on the merged head. Their regression
value was measured directly: with the component reverted to `main`'s version and the spec unchanged,
**five of the eight fail** — both late-refresh-after-Escape cases, the late write *failure* after Escape,
and both card-switch cases. The remaining three (late write *success* after Escape, same-card prop
replacement, unmount before the write settles) pass against the unfixed component as well; they pin
invariants the repair must not break rather than reproducing the reported defect. In particular,
Vue's runtime already suppresses component emits after unmount. That successful-write unmount case
does not prove that the ownership counters caused suppression; the new failed-write unmount case
separately proves that the global toast stays silent.

## Qualification actually executed

From `frontend/taskdeck-web`, on the merged head:

```sh
npx vitest --run --maxWorkers=2 src/tests/components/CardArchiveAction.requestOwnership.spec.ts   src/tests/components/CardArchiveAction.spec.ts src/tests/components/BoardCardArchive.spec.ts   # 18/18
npx vitest --run --maxWorkers=2 src/tests/components/CardModal.spec.ts   src/tests/components/CardModalAssignmentSave.spec.ts src/tests/views/paper/PaperBoardCard.spec.ts  # 75/75
npm run typecheck   # clean
npm run build       # clean
```

The original full frontend suite was not run locally on this box (it exhausts memory); the hosted
Frontend Unit job supplied broad evidence. Full Paper/Legacy recovery journeys and screen-reader
announcement order remain **NOT RUN**.

## GH-3033 focus and submitted-write follow-up

Before changing the component, the repository Playwright regression
`tests/e2e/card-archive-focus.spec.ts` reproduced the real Chromium sequence: focus Archive card,
confirm while the archive POST is held pending, press Escape, then release a 409 response. The
opener was disabled, `document.activeElement` was `document.body`, and Refresh card state was visible
but unfocused. The original `focusElsewhere()` unit setup bypassed this failure path.

Recovery still checks card context after `nextTick`. If confirmation ownership changed, it moves
focus only when no usable focus remains and no newer confirmation is open. The existing valid-control
case remains covered. Native tests additionally cover removed/disabled targets and submitted archive
and restore success/failure after a card switch while a newer preview stays pending.

The browser fixture mounts the real Vue component, TdDialog and board store through Vite. HTTP
responses are synthetic and the local runner uses one Chromium worker with no backend. This proves
native DOM focus ownership, not server persistence, route integration or screen-reader behavior.
The stale-write notice is observed through the real toast store in the fixture, not a toast renderer.

The pre-fix failure and initial three-case Chromium pass are preserved under the worker's ignored
`frontend/taskdeck-web/test-results/archive-focus-evidence/` directory for coordinator handoff.

## Boundaries and handoff

GH-2969 still owns preserving newer unsaved card drafts when a committed archive causes its parent editor to close. GH-2997 still owns assignment-save interactions with page-level Refresh/Delete. Neither is solved here. Server-side writes already submitted are not rolled back by dismissal or unmount.

The coordinator owns canonical STATUS and MASTERPLAN integration for this follow-up. No human-action
item in OUTSTANDING_TASKS.md was checked off; release trust, private-instance, benefits/distribution,
and CI control-plane decisions remain with their existing owners.
