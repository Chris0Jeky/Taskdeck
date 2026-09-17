# Timezone test contract and convention guard (#3013)

Last Updated: 2026-09-13

Status: draft PR #3090; the standalone fake-timer ordering assertion is repaired
locally. No hosted, Stryker, browser, physical-device or screen-reader
qualification is claimed. Do not merge until the exact-head frontend gate passes.

## Decision and boundaries

Keep the existing timezone fixture API and its offset-correction algorithm. Do
not silently impose a new earlier/later disambiguation policy on existing tests.
Pin New York's fall-back to 2026-11-01T05:30Z and Berlin's to
2026-10-25T01:30Z, and round-trip both back to the requested local clock.
The helper comment no longer promises that two corrections solve every zone.
For an intentionally chosen overlap occurrence, use an explicit ISO instant.

The fake-timer ordering regression resets timer state, captures the host zone,
chooses an installed zone that differs, and enforces the required ordering:
fake timers are enabled before the timezone helper is installed. It no longer
relies on the runner not living in Pacific/Kiritimati. The positive
fake-timers-before-install case remains in place. A separate regression proves
the mixed Date model: local constructors, zone-less parsing and local setters
retain their native identities and host-time behavior while local getters and
Intl defaults use the installed fixture zone. The region guidance names these
limits rather than describing a globally virtualized clock.

## Source guard architecture

`src/tests/guards/timezoneEnvironment.spec.ts` reads the test tree through Vite's
raw globs, following the existing source-guard pattern. Both `src/tests` and root
`tests` sources are covered; root E2E, visual and generated-worker exclusions match
`vitest.config.ts`. Named sentinels include a root unit spec, and excluded suites
must not appear in the scan. This closes the first automated review's finding:
the original glob covered only `src/tests` although ordinary Vitest also executes
root unit specs. A small test-only helper
uses the already-present TypeScript compiler API to find literal calls. Unlike
comment-stripping regexes, AST traversal distinguishes calls from prose and
fixture strings, including multiline and bracket-property forms. `.ts` is parsed
as TypeScript, not TSX, so generic arrows do not consume later calls as JSX.

There is exactly one scoped exception: the known `vi.stubEnv('TZ',
'Pacific/Midway')` in the specifically named environment-independence test in
`timeZone.spec.ts`. The guard checks that exception still exists exactly once;
it does not exempt the whole helper file. Its scope, count and scanner behavior
have fixtures, and the scan must include named sentinel files and over 200 files
so an empty glob cannot pass silently.

This is a coding-convention check, not a security or data-flow proof. Computed
keys, renamed functions, indirect wrappers, and direct process.env mutation are
not covered. It conservatively recognizes a literal `.stubEnv` on any receiver.
No dependencies, workflows, mutation targets, package files or app runtime change.
The mutation lane may instrument source layout; this guard inspects call nodes,
not facade return-block formatting. Its own fixtures are strings, not executed
TZ mutations.

## Validation evidence and commands

Local environment: Node 22.16.0, globally available TypeScript 5.8.3; repository
frontend dependencies are absent and registry DNS did not resolve. This is not
the repository's Node 24/Vitest runtime. All three changed pre-existing files
match remote main `2cdc4525766101211fe04787e23bfb46b6aa4011` blob hashes. Other
local files are the supplied older snapshot, not a complete exact-main checkout.

Executed standalone probes over the actual transpiled helper and scanner:

- Four fresh processes started under UTC, Pacific/Kiritimati, Pacific/Midway and
  Europe/Amsterdam: both overlap instants, gap refusal, mixed-Date invariants and
  restoration passed (10 assertions in each process).
- 12 literal-call/non-call scanner fixtures passed, and a deliberate forbidden
  ordinary-file call was detected rather than exempted by its test title.
- The local source-tree scan examined 418 files and found exactly the one intended
  self-test. This count describes that local snapshot, not current main.

The review follow-up expanded the standalone scan to 445 local files, again
finding exactly one self-test; deliberate forbidden calls under both component
and root-unit paths were detected. The count remains local-snapshot evidence.

On the current PR merge, the targeted ordering test failed independently under
both `forks` and `threads` because it installed the timezone helper before fake
timers and expected the discarded host zone. The fix resets timers and enforces
the documented fake-timers-before-helper ordering. After changing the test and
cleanup order, the targeted and combined helper/guard tests pass independently
in both pools. The full matrix and hosted gate remain pending.

The initial hosted run 34725757086 at 062a35e32500d7692198d8b117c42e6f0447d5
passed frontend lint, typecheck and build on both operating systems. The Linux
suite reported 6,912 passed, three skipped and one failure, in the fake-timer
ordering regression; the new source guard passed all 16 cases. The log reported
`ReferenceError: NativeDateTimeFormat is not defined`. Its displayed helper
excerpt does not match the fetched head/merge helper body, so no root-cause or
repair is inferred from that trace. The first automated review separately found
the root-unit scan gap, corrected above. That historical run predates the
ordering repair below and does not certify the current exact head. No assertion
is skipped or weakened.

The standalone probes do not execute fake timers, the Vite raw glob, the full
type checker, or Vitest. Qualify the new guard (16 cases) and expanded helper
suite (17 cases) in both relevant pools on the exact head:

```sh
cd frontend/taskdeck-web
npx vitest --run --maxWorkers=2 src/tests/utils/timeZone.spec.ts src/tests/guards/timezoneEnvironment.spec.ts
npx vitest --run --pool=threads --maxWorkers=1 src/tests/utils/timeZone.spec.ts src/tests/guards/timezoneEnvironment.spec.ts
npm run lint
npm run typecheck
npm run build
```

Then run the repository's complete frontend gate on the exact PR head. A green
ordinary suite is not evidence that Stryker's runner or dry run has been repaired:
#3009/#3040 and #3038 remain separate. The remaining environment-cleanup concern
in #3013 is not changed globally: no new environment stub is introduced, and the
one retained self-test already calls `vi.unstubAllEnvs()` after each case.

## Primary references and handoff

- [Vitest vi API](https://vitest.dev/api/vi): fake timers and environment stubbing.
- [Node worker threads](https://nodejs.org/api/worker_threads.html): worker-local
  environment state; the repository's specific TZ behavior is the measurement
  recorded on #2943, not an assertion that every environment has identical hooks.
- [TypeScript compiler API](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API):
  source files and AST traversal. A future compiler API migration must qualify
  this test helper; it is not a production dependency.

Canonical STATUS/MASTERPLAN and OUTSTANDING_TASKS.md are unchanged for this
unmerged candidate. Human device/keyboard, screen-reader, translation quality,
provider, release/hosting and CI-control decisions remain open. No merge or
settings change is included.
