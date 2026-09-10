# Repository adapters and conservative onboarding

Date: 2026-09-10. Parent: [engineering contract](README.md). The portable CLI and Taskdeck adapter produce observations only; neither executes the task commands it models, writes GitHub checks, edits workflows, or enables result reuse.

## Portable CLI

Node 22+ and Git are required. There are no external package dependencies. Run a reviewed copy of this directory from trusted tooling; `--repo` is an object database to inspect, not a source of executable plugins.

```sh
node scripts/ci/smart-ci/continuation/cli.mjs init --kind node --repository-id 123456 --root web --out candidate-config.json
node scripts/ci/smart-ci/continuation/cli.mjs validate --manifest candidate-config.json
```

Supported starter ecosystems are `node`, `dotnet` and `python`. The root is a literal repository-relative directory, or `.`. Starters deliberately model a complete component suite with `reviewed: false`. The sample command is a reviewable identity, not a promise that every repository exposes that script. Adapt working directory, exact command, runtime and dependency resolution to the actual workflow. Add transitive shared-code/configuration/fixture dependencies before narrowing anything.

After review, commit the configuration at `.ci/continuation.json` on the trusted base. Obtain the immutable numeric repository ID from authenticated provider metadata, not from candidate-authored configuration. Then:

```sh
node scripts/ci/smart-ci/continuation/cli.mjs plan --repo /path/to/repository --base FULL_BASE_COMMIT_SHA --candidate FULL_CANDIDATE_COMMIT_SHA --repository-id 123456 --out advisory.json
```

`--config` may select another file only under a protected control path. The CLI reads configuration bytes from the **base commit**, never the candidate or dirty working tree. Candidate edits to the control policy select full qualification. Missing Git objects, invalid JSON, repository mismatches and incomplete inventories fail visibly. The output is a new file; existing output files are never overwritten.

The source API `adviseRepository` accepts an additive canonical selection floor, exact environments/context and event type. `inspectRepository` is the immutable-object, observation-only command path. Non-PR events, explicit full qualification, unknown ownership or changed control paths escalate. Default output has `authority: none` and all executable actions remain `run`.

## Manifest protocol

`ci.repository-adapter.v1` contains an immutable repository ID, human description, declarative policy, and the core input-contract graph. It contains no JavaScript plugin or shell hook.

The policy declares `alwaysTasks`, `controlPaths`, and ownership `rules`. The minimum `.github/**`, `.ci/**`, `ci/**` and `scripts/ci/**` control coverage cannot be removed. Rules may add checks; the canonical floor cannot be reduced. Input closure may add further dependent consumers. Unknown top-level/policy fields, duplicate task references, unsupported patterns and cyclic dependencies are rejected.

The contract graph declares components, transitive dependencies, task input patterns, required files, command identity, platform, exact environment/context keys, TTL and review/reuse state. See the parent engineering document for fingerprint and evidence semantics. The adapter policy and complete contract content affect the fingerprint; editing configuration invalidates applicable observations rather than silently retaining earlier approval.

For a monorepo, create distinct components for shared libraries and each deployable/service, then declare tests and integration tasks over their transitive inputs. For a single application, keep one complete task until measured costs justify a split. Never assume that folder names alone capture test harnesses, generated clients, containers, process launchers, imported build targets or external data.

## Taskdeck-specific bridge

```sh
node scripts/ci/smart-ci/continuation/adapters/taskdeck.mjs --repo /path/to/Taskdeck --plan ci-plan.json --out taskdeck-advisory.json
```

Run the bridge from reviewed Taskdeck tooling. It imports canonical validators relative to its own trusted module, **not** from the `--repo` candidate checkout. It reads `ci/policy.v1.json` from the plan's immutable control-base SHA. Canonical plan/schema validation happens before the auxiliary report. The comparison is bound to the observed merge first parent and exact merge commit/tree.

Every Taskdeck task contract is still explicitly unreviewed. The adapter derives all canonical lane IDs instead of inventing a parallel policy. It only adds affected lanes; risk/trust escalation remains full. The initial broad contracts account for the Linux frontend job's backend-launcher dependency. Unknown future lane IDs receive a whole-repository input boundary, not an optimistic empty contract.

This bridge is not evidence of safe omission. A local invocation can inspect a supplied plan, but production acceptance still requires authenticated canonical provenance. No report produced here is fed into the canonical gate as successful task evidence.

## Dependency-only workflow proposal

```sh
node scripts/ci/smart-ci/continuation/tools/stage-taskdeck.mjs --repo /path/to/Taskdeck --out ci-required.proposed.yml --mode minimal
```

The transformer handles the reviewed 13-job block-mapping shape only. It changes `needs`, retaining commands, matrices, names, permissions, immutable pins and event triggers. It refuses unfamiliar structures, anchors, cycles, duplicate dependencies and unconditional dependence on the PR-only secret scan. Apply only the reviewed diff in a separate PR. The optional `compute` mode waits for the entire backend unit matrix before API integration; this is not the default because it may worsen healthy-candidate latency.

The transformer is intentionally **Taskdeck-specific**. Do not use it as a generic YAML migration engine. Other repositories should use their own reviewed workflow adapter over the provider-neutral contracts/core.

## Validation and limitations

Combined core + original Taskdeck/staging + new repository adapter/CLI tests: **253 passed, zero failed/skipped/cancelled**, local Node 22.16.0/Linux. A real temporary Git repository proves candidate and dirty-worktree policy changes cannot replace the base policy; output overwrite and configuration-budget checks are exercised. These fixtures do not constitute adoption in a second real repository, hosted configured-Node qualification, Windows testing or a full Taskdeck governance/product pass.

Porting requires a second real-repository shadow trial before claiming cross-repository effectiveness. No new repository, external service, registry publication or license grant is created. The kit inherits the repository license; separately review licensing before distributing it as a standalone product.
