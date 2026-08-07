# ADR-0049: Type-Check the Frontend Spec Tree via a Separate Project with an Explicit Quarantine

- Status: Accepted (tooling/verification-contract decision taken under standing autonomy; open to maintainer revision)
- Date: 2026-08-07
- Deciders: Overnight coordinator; maintainer may revise
- Related: `#1468` (the gap), `#1607` (the burn-down this creates), `#1462` (the regression that exposed it), `#1469` (the per-field workaround this generalises but deliberately retains), ADR-0030 (Storybook baseline — `src/stories/**` is excluded on the same line and is out of scope here), `docs/TESTING_GUIDE.md` §Frontend Unit + Build

## Context

`frontend/taskdeck-web/tsconfig.app.json` excludes `src/tests/**`, and `npm run typecheck` is
`vue-tsc -b`, which builds only the projects referenced from `tsconfig.json`. Vitest transpiles
specs without type-checking them — `--typecheck` is opt-in and targets `*.test-d.ts` files. Between
those two facts, **no gate type-checked a single frontend spec file**: not `typecheck`, not `lint`,
not `build`, not the vitest run, and therefore not CI.

The practical consequence is that type-level regressions were invisible in exactly the files whose
job is catching regressions. `#1462` is the worked example: a spec asserted
`Proposal.approvedRevisionId`, and deleting that member from the interface left `npm run typecheck`
green, because the only file referencing it lived under `src/tests/`. Types erase at runtime, so the
vitest run passed too.

`#1469` mitigated that single field with an exported derived type alias
(`ProposalApprovedRevisionId = Proposal['approvedRevisionId']`) placed in production source, where it
*is* checked. That works, and it is deliberately retained, but it only protects fields somebody
remembers to pin by hand. It is not a substitute for checking the specs.

The reason the exclusion existed is real and was measured before this decision, not assumed:
**415 type errors across 64 of the 286 TypeScript files under `src/tests/`** (284 specs plus
`setup.ts` and one mock; repo `types` unchanged, 2026-08-07, reproducing the figure recorded on
`#1468`). Zero outside `src/tests/`. The count is also sensitive to the `types` array — 415 as-is,
399 with `"node"` added, 379 with `"node"` and `"vitest/globals"` — so "just turn it on" was never
available; the spec type environment has to be decided, not defaulted.

There is also a **second, separate spec tree**: Vitest runs 18 specs from the frontend-root `tests/`
directory (`demo-*`, `scenario-*`, `playwright.*`) alongside the 284 under `src/tests/` — 302 files
in a full run, which is exactly what a full run reports. Those 18 are equally unchecked and are
*not* addressed by this decision; see Alternatives.

## Decision

**Add `frontend/taskdeck-web/tsconfig.vitest.json` as a third referenced project covering
`src/tests/**`, mirroring `tsconfig.app.json`'s compiler options exactly, and quarantine the 64
already-broken files in its `exclude` array.**

Three parts, each load-bearing:

1. **A separate project, not a relaxed one.** `tsconfig.vitest.json` extends the same
   `@vue/tsconfig/tsconfig.dom.json` base and repeats `tsconfig.app.json`'s options verbatim —
   including `noUnusedLocals`, `noUnusedParameters` and `erasableSyntaxOnly`, which together account
   for 4 of the 415 errors (`TS6133` ×4; `erasableSyntaxOnly` contributes none). One strictness bar
   across the codebase is easier to reason about than two, and the errors those options catch are
   real. The separation exists so the *file sets* can differ, not the standards.

   The `types` array stays `["vite/client", "vite-plugin-pwa/client"]`. Adding `"node"` is the
   obvious-looking move — it clears the 13 `TS2591` `process` errors — and it is wrong: the spec
   project pulls production source in as a dependency, and with node types in scope it emits exactly
   one error, `PaperHomeView.vue(238,5): TS2322: Type 'number' is not assignable to type 'Timeout'`.
   The direction matters and is easy to state backwards: `greetingTimer` is *annotated*
   `ReturnType<typeof window.setInterval>` (`:225`), which resolves to node's `Timeout` once node
   types are in scope, while the *call* still returns the DOM `number`. `"vitest/globals"` is also
   declined: 283 of the 284 spec files import their vitest symbols explicitly, so adding the globals
   would exist to serve one non-conforming file (`config/PaperBranding.spec.ts`, itself
   quarantined).

   `include` additionally carries `src/**/*.d.ts`. `src/types/web-speech.d.ts` is an *ambient*
   declaration file — global scope, imported by nothing — so `tsconfig.app.json` only picks it up
   because it globs `src/**/*.ts`. A `src/tests/**`-only include would drop it from this program,
   and production source pulled in as a dependency would compile without those globals. That is not
   theoretical: un-quarantining `composables/useVoiceCapture.spec.ts` without this line reports 3
   errors inside untouched production source, and *masks* 2 of the spec's own by making two
   `@ts-expect-error` directives spuriously "used". With the line, the same experiment reports only
   the spec's own 4 errors.

2. **The quarantine is a list of files, not a loosened rule.** The 64 files that already failed are
   named individually in `exclude`. The other 222 are gated. Critically, **a new spec file is
   checked by default**, because a file that does not exist cannot be on a list written today. That
   is the property that stops further rot; the pre-existing 415 are a debt, not a leak.

   The list may only shrink. `#1607` tracks emptying it, with the per-file counts and the three
   config constraints recorded there so the next person does not rediscover them.

3. **No CI workflow change.** `vue-tsc -b` builds whatever `tsconfig.json` references, and
   `reusable-frontend-unit.yml` already runs `npm run typecheck` on both Ubuntu and Windows. Adding
   the reference is the entire wiring. Nothing under `.github/` is touched, which also keeps this
   change clear of the root `CODEOWNERS` maintainer gate.

A secondary consequence is deliberately claimed: `expectTypeOf` becomes usable in ordinary specs.
It erases at runtime, so the assertion is discharged by `vue-tsc -b` rather than by the vitest run.
`src/tests/api/automationApi.spec.ts` now uses it to pin `Proposal.approvedRevisionId` — the thing
`#1462`'s runtime tests structurally could not do.

## Alternatives Considered

- **Fold `src/tests/**` into `tsconfig.app.json` and fix all 415 errors first (issue option 2).**
  Rejected as the *first* step, not on principle. It couples a config decision to a 64-file spec
  rewrite, produces an unreviewable diff, and — worst — leaves the gate absent for however long the
  rewrite takes, which is when new rot lands. The quarantine gets the gate in place today and turns
  the 415 into a tracked, shrinking list. Folding the projects together is a reasonable end state
  once `#1607` empties, and is not foreclosed by this decision.

- **`vitest --typecheck` (issue option 3).** Rejected as the primary mechanism. It moves checking
  into the test run, which is slower and lands the failure in a different lane from every other type
  error; it primarily targets `*.test-d.ts`; and it would leave `npm run typecheck` still silently
  skipping the spec tree, so the misleading signal that caused `#1462` would survive. The one thing
  it was wanted for — `expectTypeOf` — works without it, because `expectTypeOf` is discharged by any
  compiler that reads the file. Option 3's actual benefit is therefore obtained by option 1 alone.

- **Relax `noUnusedLocals` (and friends) for specs, as the issue's option 1 sketch suggested.**
  Rejected: it would clear 4 of 415 errors while permanently splitting the strictness bar. Not a
  trade worth making. (The figure is 4, not the 6 an earlier revision of this ADR carried: the two
  `TS2578` "unused `@ts-expect-error`" diagnostics are not governed by any of those three options
  and survive with all three off.)

- **Keep extending the `#1469` derived-alias workaround.** Rejected as a general answer. It is
  per-field, requires foresight about which field will be dropped, and does nothing for wrong-typed
  arguments, mis-typed mocks, or any other spec-local error. It is kept for
  `approvedRevisionId` specifically, as a belt-and-braces guard that holds from inside production
  source even if that spec were ever quarantined.

- **Also un-exclude `src/stories/**`.** Deferred, not rejected. The 17 story files sit on the same
  `exclude` line and are equally unchecked, but they are a distinct surface with a distinct type
  environment (ADR-0030). Bundling them would widen this slice for no shared benefit.

- **Also cover the frontend-root `tests/` specs.** Deferred, and for a stronger reason than the
  stories: they cannot go in *this* project even if we wanted them there. They import the `.mjs`
  scripts under `scripts/` and use `process`/`NodeJS`, so they need `types: ["node"]` — the exact
  setting that breaks production source here. Measured 2026-08-07: **54 errors across 15 of the 18
  files**, dominated by `TS7016` (untyped `.mjs` imports), `TS7006` and `TS2503`. Covering them
  means a fourth project with a Node type environment, and probably `allowJs`/declarations for the
  scripts they import. Tracked in `#1607`.

## Consequences

**Positive.** Type errors in 222 files under `src/tests/` now fail CI on both matrix legs. New spec
files placed there are gated from the moment they are written. Type-level assertions (`expectTypeOf`)
are available and already used. The `#1468` gap is closed as a *mechanism*, with the residue explicit
and tracked rather than implicit and forgotten.

**Scope, stated so it is not overread.** This does not make "the frontend test suite type-checked".
A full Vitest run executes **302** spec files — 284 under `src/tests/` and 18 under the
frontend-root `tests/` directory. This project gates **220** of them (284 − 64 quarantined). The
**82** it does not gate are those 64 plus those 18. Both residues are named above and tracked in
`#1607`. (Do not subtract 222 from 302: 222 is the number of *files* the project resolves under
`src/tests/`, which includes `setup.ts` and a mock that are not specs. The two counts have different
units.)

**Negative / accepted.** `npm run typecheck` does more work: the spec project re-compiles the
production source its specs import, so the two projects overlap. Measured at ~33 s warm on the
development box for the whole `vue-tsc -b`, which is not a meaningful change to the CI job. The
quarantine list is 64 lines of config that must be maintained honestly — its failure mode is
somebody appending to it to make a red build green, which is why both the file and `#1607` state the
shrink-only rule explicitly.

**Residual risk.** The quarantine is enforced by convention, not by a check: nothing mechanically
prevents a future PR from adding a file to `exclude` or from widening `types`. A guard script could
be added if that ever happens; adding one now would be speculative scaffolding for a failure that
has not occurred.

## Verification

- `npm run typecheck` (`vue-tsc -b`) exits 0 with the new project referenced. 415 errors reproduced
  exactly by the documented method before the change; 0 after, with the 64 files quarantined.
- **Mutation-verified, three probes.** (a) A type error introduced in a non-quarantined spec fails
  the gate (`TS2322` + `TS6133`, exit 2). (b) A brand-new spec file placed under `src/tests/` fails
  the gate (`TS2322`), confirming new files are checked by default. (c) The `expectTypeOf` pin fires
  on all three mutations of its target: deleting `Proposal.approvedRevisionId` (`TS2339`), making it
  optional (`TS2344`), and dropping its nullability (`TS2344`).
- `npm run lint` 0 errors / 6 pre-existing warnings; `npx vite build` green; the full
  `npx vitest --run --maxWorkers=2` **302 files / 3,925 tests passed, 0 failed** (206 s), which is
  also where the 302-vs-284 file discrepancy that exposed the frontend-root `tests/` tree came from.
