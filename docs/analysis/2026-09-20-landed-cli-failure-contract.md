# Landed verifier CLI: exact commit and failure contract

Scope: the CLI residuals on [#3227](https://github.com/Chris0Jeky/Taskdeck/issues/3227),
stacked after [PR #3295](https://github.com/Chris0Jeky/Taskdeck/pull/3295).
The receipt decision and collector-repository checks remain in that parent PR.
No workflow starts using the verifier as part of either change.

## Commit identity

Without `--head-tree-sha`, the CLI validates a full 40-character requested commit
SHA and resolves `<headSha>^{tree}`. It never borrows the current checkout's
`HEAD^{tree}`. Missing objects, invalid SHAs and Git errors return a null tree and
full qualification. A real two-commit fixture proves both directions: an older
requested commit can use its own receipt while HEAD moves, and a newer checkout's
receipt cannot qualify that older commit.

The optional explicit `--head-tree-sha` remains an input from the trusted caller;
it is not a tree assertion to copy from a candidate artifact. Repository identity
must come from `--repo` or `GITHUB_REPOSITORY`; the CLI no longer assumes Taskdeck
outside Actions. This does not authenticate caller-supplied JSON or replace the
future collector's checks.

## Output is affirmative-only

The CLI finds the output destinations before parsing the remaining arguments and
first writes `qualification=full` and `bounded=false`, with no receipt identity.
Argument errors therefore cannot leave a writable output channel uninitialized.
It creates nested summary directories, writes the report, and only then appends
the final decision. `bounded=true` is the last field in that final append. A
handled parse, filesystem or reporting failure writes a full verdict where
possible, leaves or appends false outputs, and exits nonzero. Failure diagnostics
never echo raw arguments, evidence text, provider exception messages, or paths.

A future Actions consumer must require **both** successful execution and an
explicit affirmative flag:

```yaml
if: ${{ steps.landed.outcome == 'success' && steps.landed.outputs.bounded == 'true' }}
```

Every other result requires full qualification. In particular, never authorize
bounded execution with `qualification != 'full'`: a process that could not start,
an unavailable output sink or a cancelled job can have no output at all. The
workflow integration must preserve a full-qualification fallback despite the
verifier step failing; this PR does not implement that integration.

JSON and the Actions output file are not a cross-file atomic transaction. If a
sink is unwritable, an error handler cannot promise to repair it. Use a fresh
output path for each invocation and the successful-step/explicit-true rule, not a
stale verdict file, partial output, summary text or a negative string comparison.

The entry-point check resolves real paths, so a directory symlink or Windows
junction runs the CLI; importing the decision module does not run it.

## Verification boundary

The first CLI negative-control run had 24 behavioral failures across 25 tests;
the import-without-side-effects control already passed. Explicit tree assertions
reproduced a mismatch between requested commit and checkout tree, and acceptance
of the newer checkout's receipt for the older requested commit. The other cases
cover output flags, missing/malformed/non-array/unreadable evidence, unavailable
policy, malformed arguments before or after output options, summary/verdict/
output write failures, nested directories, environment binding and symlink entry.

After the implementation, all 25 CLI tests and all 589 Smart CI tests passed on
Linux / Node 22.16.0, with no failed or skipped tests. The full-suite invocation is
`node --test scripts/ci/smart-ci/*.test.mjs`; the focused invocation is
`node --test scripts/ci/smart-ci/landed-verifier-cli.test.mjs`.

These local results are additive only. Exact-head hosted results belong on the
PR, and the Windows-junction test is not Windows qualification merely because it
passes on Linux. The R4 maintainer plus fresh-context review gate remains in
force. Neither policy mode nor main/full-CI topology, repository settings, private
runner acceptance, the observation window or any human-action checkbox changes.

## Guard-isolation follow-up

A local mutation check removed only the `wouldFail` predicate from receipt
admission. All 21 parent receipt tests still passed because the negative fixtures
also had another rejecting field. A new `ok: true`, `wouldFail: true`, empty-
failures fixture now fails under that mutation and passes with the original
production guard restored. This was a coverage gap, not a change to production
acceptance. The final stack has 22 focused receipt tests and 590 Smart CI tests;
the new fixture belongs with this follow-up rather than changing the parent PR's
already-recorded head. No mutation is committed.
