# CI-17: conservative runner and reusable-workflow inventory

This is an implementation prerequisite for [#3170](https://github.com/Chris0Jeky/Taskdeck/issues/3170),
not the private-cutover rehearsal mechanism. It does not change a workflow, activate a mode,
suppress a job, configure a runner, authorize a visibility change, or close an acceptance box.
The existing [hard-issue map PR](https://github.com/Chris0Jeky/Taskdeck/pull/3281) identifies this
workflow-graph proof as the smallest useful independent slice of that issue.

## What this proves

The dependency-free [inventory module](../../scripts/ci/smart-ci/workflow-runner-inventory.mjs)
discovers runner-bearing jobs and job-level reusable-workflow calls across the supplied source
set. The existing Smart CI test glob runs its regression suite and compares the Windows/opaque
runner sites and all their ancestor call jobs with a checked-in review snapshot. A new site,
changed selector, matrix, condition, input, trigger or route changes that surface and requires
explicit review. Ordinary step bodies are not copied into the report.

The source boundary is deliberately narrow: top-level, `jobs`, and individual jobs must be
unquoted block mappings at indentation 0/2/4. Duplicate keys, merge keys, aliased/inline job
mappings, unexpected structural indentation and multiple-document syntax make discovery
incomplete. This is not a general YAML parser or an Actions schema validator; actionlint
remains separate. Runner arrays, groups, aliases, custom labels and expressions are retained
as opaque candidates, not discarded. Nested matrix and caller input text is preserved,
including blank and hash-prefixed lines inside block scalars.

Only the finite reviewed scalar spellings `ubuntu-latest`, `ubuntu-22.04` and `ubuntu-24.04`
are classified as Linux literals. That is a statement about source spelling, not verified
runner identity. Every other selector remains on the conservative review surface. Conditions
and matrix exclusions never remove candidates: the module does not evaluate expressions,
`if: false`, include/exclude precedence, environment or repository variables, or caller inputs.
For example, even the API caller that supplies `platform: linux` remains visible.
To keep large projections from expanding the JSON report without bound, the inventory fails
closed with `projection-limit` when the projected trigger text exceeds 8 MiB, and the reviewed
surface returns `graphComplete: false` with no candidates when candidate-plus-ancestor caller
projections exceed its 8 MiB bound. These are discovery failures, not qualification results.

Local calls are resolved within the supplied workflow set. Missing callees, external or
unresolved references, cycles and duplicate/non-canonical files make `graphComplete` false.
All disconnected runner sites are included, not just jobs reachable from `ci-required.yml`.
Ancestor traversal retains separate caller job IDs, including parallel calls from one file,
without enumerating exponentially many paths. Source bounds are 256 files, 2 MiB per file,
16 MiB aggregate and 4,096 jobs. The loader rejects non-regular workflow entries, including
file symlinks; this is not a filesystem sandbox or a concurrent-file-mutation guarantee.

GitHub supports multiple [runner-selector shapes](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idruns-on),
[matrices](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idstrategymatrix)
and [reusable calls](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_iduses).
The conservative treatment is intentional: resolving some expression strings would create a
second, weaker scheduler rather than evidence that no Windows job can run.

## Measured source inventory

At main `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`, there are **36 workflow files, 50 runner jobs
and 41 job-level reusable calls**. Five runner sites need Windows/dynamic review:

| Workflow and job | Selector | Known caller jobs |
| --- | --- | --- |
| `release-desktop.yml / build-backend` | `matrix.os`, with Windows in `include` | Direct release workflow entry |
| `reusable-api-integration.yml / api-integration` | `matrix.os`, supplied by an input-dependent expression | `ci-required / api-integration` and `api-integration-windows` |
| `reusable-backend-unit.yml / backend-unit` | `matrix.os`, Linux and Windows matrix | `ci-required / backend-unit` |
| `reusable-docs-governance.yml / worktree-helper-windows` | Literal `windows-latest` | `ci-required / docs-governance` |
| `reusable-frontend-unit.yml / frontend-unit` | `matrix.os`, Linux and Windows matrix | `ci-required / frontend-unit` |

These are source sites, not executed job counts or proof of a particular private-mode run.
The four matrix selectors remain opaque even though their current text mentions Windows.

## Running and updating the inventory

```sh
node scripts/ci/smart-ci/workflow-runner-inventory.mjs
node --test scripts/ci/smart-ci/workflow-runner-inventory.test.mjs
node --test scripts/ci/smart-ci/*.test.mjs
```

The CLI prints JSON and exits 2 for incomplete or unavailable source. Exit 0 means discovery
completed, **not that a Linux-only rehearsal is safe**. Neither the CLI nor the report emits
qualification, bounded-execution or guard-authority flags. Never use `graphComplete`, exit 0,
this snapshot test, or an empty candidate array to authorize suppressing CI work. The source
set itself has no authenticated commit or protected-base provenance supplied by this module.

Review the changed runner site and its caller controls before updating
`test-support/workflow-runner-inventory.snapshot.json`. A snapshot is a drift alarm, not a
trust boundary: a PR can edit both source and snapshot. Trusted-base execution and approval
of a real rehearsal guard still have to be implemented and separately reviewed.

## Verification and residuals

The supplied ZIP identifies `6818072c413609fd2b9a9e37c778e866998d5b1e`. GitHub comparison against
the above main found 45 subsequent commits, with no changes to the workflows, Smart CI scripts
or policy used here. Publication must use real GitHub ancestry, not the synthetic local ZIP
commit. The unrelated live backend and frontend changes are not replaced.

Local Linux / Node 22.16.0: **47 focused tests and all 601 Smart CI tests pass**, with zero
failures or skips. A separate local PyYAML 6.0.3 structural cross-check found exactly the same
50 runner-job and 41 reusable-call identities; PyYAML is not a shipped dependency. Mutation
checks dropping opaque candidates, dropping ancestor calls and ignoring duplicate keys were
rejected by 11, 4 and 3 focused tests respectively. Every mutation was removed before the final
suite. The initial literal-only negative control was deliberately insufficient, not existing
production code; it is not committed.

This slice does not test guard coverage, authenticated mode propagation, actual scheduling,
non-vacuous Linux/security evidence, private-mode runs or trusted receipts. It does not prove
Windows runtime behavior from a Linux junction test. Exact-head hosted results and fresh
review belong on the PR; local results are additive, not R4 qualification. #3170 and #2337
remain open. R4 maintainer/fresh-context review and the human decisions in OUTSTANDING_TASKS.md
J.3(b)/J.4 remain unchanged; canonical J.2 registration is a coordinator follow-up.
