# Verification continuation: engineering and integration contract

Date: 2026-09-10. Related work: CI-02 #2326, CI-03 #2327, CI-05 #2329, CI-12 #2336 and CI-15 #2339. Programme: [Smart CI Fabric](../SMART_CI.md). Historical measurement: [CI baseline](../CI_BASELINE.md).

## Scope and authority

The continuation kit extends the existing protected planner; it does not replace `ci/policy.v1.json`, `ci-plan.v1`, `ci-run.v1`, or `Smart CI / Required Gate`. Importing the kit changes no Taskdeck job selection. Default `observe` mode returns `action: run` for every task, even when it reports that a proof could have been reused.

Keep three separate decisions: which checks the protected policy requires; whether earlier evidence applies to those checks; and when the remaining work should start. Mixing them makes it possible for a cache miss, an unknown path or an orchestration cancellation to masquerade as successful verification.

The supplied `enforce` library mode is for explicitly reviewed integrations. It is exercised against a fictional repository in tests. It is not an activation flag for Taskdeck. Maintainer review and independent review remain required for these R4 changes; this PR does not merge itself, register required contexts or close human gates.

## Module map

| Module | Responsibility | Failure behaviour |
| --- | --- | --- |
| `core/primitives.mjs` | Canonical JSON, domain-separated hashes, bounded glob cache and graph traversal | Invalid values, unsupported glob syntax and cycles throw |
| `core/snapshot.mjs` | Full immutable Git inventories and endpoint comparisons | Incomplete, malformed or ambiguous input is rejected |
| `core/contracts.mjs` | Transitive task inputs, additive impact and task fingerprints | Unknown changes expand the plan; unresolved inputs disable reuse |
| `core/evidence.mjs` | Protected-producer assertions and Ed25519 envelope verification | Invalid, revoked, stale or incorrectly bound proof is a miss |
| `core/planner.mjs` | Reconcile canonical selection, fingerprints and evidence | A planning error returns the full trusted universe |
| `core/execution.mjs` | Dependency readiness, feedback ordering and affinity-aware sharding | Missing work, cycles and incomplete test partitions cannot satisfy execution |
| `core/audit.mjs` | Compare frozen selection with complete oracle outcomes, sampling and baseline age | Missing evidence or an omitted failure requests full qualification |

Source lives under `scripts/ci/smart-ci/continuation/`. Imports in `scripts/ci/smart-ci/continuation.test.mjs` make nested tests part of the existing head-side self-test context without adding a permanent workflow.

## Task input identity

A fingerprint covers repository ID, task ID, the entire reviewed input-contract graph, the canonical policy digest, platform, exact command identity, all matching Git paths/blob identities/modes, declared environment identities and declared context values. It includes test sources and configuration through their input patterns. File additions, deletion, moves and executable-bit changes are significant.

The overall commit/tree ID is recorded in the plan binding but does not by itself invalidate an independent task. Otherwise an unrelated frontend correction would invalidate backend evidence. Tasks that derive their output from a version, event, commit SHA or Git history must declare that dependence in `contextKeys`; the producer must verify the actual value. A source-tree match alone is not sufficient for such a task.

The glob language is deliberately limited to `*`, `**` and `?`. Unsupported bracket, brace and negation expressions fail rather than being approximated. Input enumeration uses complete `git ls-tree -rz` snapshots, not rename heuristics or a potentially truncated changed-files API. Dirty working-tree contents do not affect a snapshot. Candidate code is never executed by the object reader. The reader assumes a trusted Git executable, tooling checkout and process environment; it is not a sandbox for an attacker-controlled local machine.

A contract has component dependencies and separate task-level inputs. Changed input propagates to every consumer. `expandAffected` may add to the protected canonical floor, never subtract from it. A dependency barrier used for scheduling is not an input dependency: record both when both are true.

Unreviewed contracts, zero matching inputs, missing required patterns, unresolved runtime/image values, and symlinks or submodules without an explicit closure model disable reuse. This initial version is intentionally conservative about links anywhere in the tree. A floating runner label such as `ubuntu-latest` is not an immutable environment identity.

## Evidence and trust boundary

A valid signature proves who issued a record, not that the record is truthful. `assertProducer` is a contract for a protected collector: its provenance flags must be derived from authenticated provider metadata, reviewed workflow definitions, immutable input inventories, the actual command and environment, and complete result inventory. Never read those flags from a PR-produced JSON object and then call `signRecord`.

The executor receives no signing key, release secret or credential capable of altering the evidence ledger. The protected collector must not check out/execute head code or restore its executable caches. Workflow identity includes the reviewed definition and actual invocation, not just a user-editable check name. Reusable workflow callees, matrix/input values, allow-failure behaviour and exact checkout identity must be accounted for.

An independently successful backend job may be reusable after a frontend sibling fails. Failed, skipped, cancelled, timed-out, empty and incomplete executions are not successful task evidence. First-attempt policy is intentionally strict: reruns cannot erase a failure. `executedTests: null` is reserved for an independently verified non-test check, never for unknown test discovery.

The verifier checks repository/task/platform, input/policy/graph digests, issuer/key, lifetime, attempt and revocation/circuit state. Reusing a proof keeps its ORIGINAL completion time and expiry. An old proof does not become fresh because a new revision referenced it. Collector outage, incomplete revocation view or missing proof means execute again, not success.

Signatures, key rotation, revocation storage and provider collection are separate integration concerns. The core API exposes them so they can be tested; importing it does not establish a production issuer or durable ledger.

## Scheduling and sharding

`nextWave` runs only tasks whose prerequisites succeeded or have explicitly verified reused evidence. It retains successful siblings after a failure. The failure barrier stops launching pending work; it does not cancel already-running GitHub jobs or promise that their last artifact upload will survive cancellation.

Ordering uses failure probability divided by duration only to order ready tasks. It cannot remove any task. Good compute savings and low healthy-candidate latency are distinct objectives: waiting for the entire Windows unit matrix before starting Linux API integration may save failing-run compute but worsen green latency.

`shardTests` uses deterministic longest-processing-time packing and keeps affinity groups together. `validatePartition` requires every discovered test exactly once and rejects empty shards. Affinity groups represent tests sharing mutable fixtures or other isolation constraints; they are not automatically discovered. Before integration, separately prove runtime isolation, test-ID stability and full union coverage. Partial-suite coverage percentages are not interchangeable with full-suite coverage thresholds.

## Independent audit and fallback

An oracle must run independently of the optimisation it measures: disable result reuse and run the complete comparison universe. Compare against the frozen pre-execution selected set and retain all relevant attempts. A later green retry cannot remove an earlier omitted failure. Unbound or incomplete oracle results are unusable, not successful zero-miss samples.

Sampling accepts a controller-only secret so a PR author cannot trivially grind a SHA to choose a non-audited candidate. Sampling does not replace maximum baseline age, bounded merge exposure or mandatory release/control qualification. A circuit-breaker implementation must durably disable affected optimisations after an audit miss and require an explicitly verified recovery, not merely the next green retry.

Twenty zero-miss observations do not prove rare-event safety. Under an idealised independent Bernoulli model, `zeroMissUpperBound(20)` is about 13.9% at one-sided 95% confidence. Actual development changes are not identically distributed independent trials. Keep Taskdeck's existing family-specific failure-recall requirements and add seeded mapping defects; do not substitute a generic statistical score.

## Validation of this import

Executed locally on Node 22.16.0/Linux with Git 2.47.3:

```sh
node --test scripts/ci/smart-ci/continuation.test.mjs
```

Result: 192 passed, 0 failed, 0 skipped/cancelled. These are continuation-library regressions, including a real temporary Git repository and ephemeral Ed25519 keys. They are not Taskdeck product tests, a full-repository governance pass, a Windows run, an independent review or configured-Node hosted qualification.

The original bundle's complete 223-test baseline also passed locally before import; its 31 adapter/workflow-transformer tests arrive with the corresponding integration slice. Hosted qualification on the exact PR head remains owed. No speedup, billing saving or production reuse hit is claimed from these tests.

## Rollout and rollback

Land reviewable slices: core/self-test discovery; repository adapters; dependency-only workflow staging; authenticated observation and telemetry; continuation adjudication/ledger/audit integration; operator documentation. Use `Refs` for umbrella issues. Do not close the programme because its primitives exist.

Keep observation until the input contracts, provenance, full-audit recall and maintainer requirements are actually met. Activate one task family at a time. Unknown state must always escalate; security feed freshness, release clean-room qualification and branch-current verification remain independent constraints.

Rollback the import by reverting its own files and test bridge. No production configuration is required for rollback because this slice changes no task selection, required context, runner registration, signing setup or repository setting. Leave existing human items in `OUTSTANDING_TASKS.md` open; visibility, spend, branch protection/required checks, runner registration and SC-10 reviews remain outside this implementation's authority.
