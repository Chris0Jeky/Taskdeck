# Taskdeck Smart CI Fabric

## 1. Thesis

Taskdeck's CI should be treated as a **verification policy engine**, not a static list of jobs.

The system receives a proposed repository state and must answer:

1. What changed semantically?
2. Who or what produced the change, and how much should the execution environment trust it?
3. Which failures could this change plausibly cause?
4. Which tests and platforms provide the highest information gain for those risks?
5. Where can those tests run safely and economically?
6. What evidence proves the resulting commit is admissible?

The target pipeline is:

```text
Event
  → immutable change inventory
  → trust classification
  → risk and impact plan
  → selected verification graph
  → hosted/self-hosted execution
  → result and evidence aggregation
  → stable required gate
  → merge / landed verification / deep qualification / release
```

The objective is not “minimum compute.” It is **minimum compute for a stated assurance level**, with conservative escalation whenever the planner is uncertain.

## 2. Non-negotiable invariants

1. **No required check disappears because a path filter skipped the workflow.** The planner and aggregate gate always report.
2. **Unknown change = full escalation.** An unmapped file, planner error, or policy-schema mismatch selects the conservative plan.
3. **Self-hosted never means trusted by default.** Trust is derived from event, actor, changed control files, and repository policy.
4. **A persistent personal machine holds no CI secret that repository code can extract.** Release/signing credentials are never present on ordinary runners.
5. **The plan is evidence.** Every run persists policy version, changed paths, risk reasons, selected and skipped lanes, runner class, and budget forecast.
6. **The gate is stable.** Branch protection requires one named aggregate check, not a changing set of reusable-workflow child names.
7. **Release evidence is clean-room evidence.** Release artifacts are rebuilt from the tag in a protected release context; PR artifacts are not promoted blindly.
8. **Platform coverage follows platform risk.** Linux is the normal semantic baseline; Windows/macOS verify their distinct contracts.
9. **Flakes are defects, not retries disguised as health.** Automatic reruns may classify, but cannot silently turn a failed required run green.
10. **Policy changes are code changes with a higher trust class.** Workflow, action, planner, dependency-bootstrap, and release changes run on hosted clean environments.

## 3. Assurance tiers

### Tier R0 — metadata and documentation

Typical changes:

- ordinary Markdown;
- non-executable design assets;
- issue templates;
- copy that cannot affect generated/runtime contracts.

Required evidence:

- docs governance;
- links and generated-index checks where applicable;
- secret scan over the diff;
- planner/gate.

### Tier R1 — isolated component

Typical changes:

- one frontend component and its tests;
- one pure domain helper;
- a local script with no release/security effect.

Required evidence:

- affected unit/component tests;
- lint/type/build for the affected ecosystem when compilation can change;
- relevant architecture/static contract;
- secret scan;
- planner/gate.

### Tier R2 — normal product change

Typical changes:

- application service;
- API endpoint;
- store/repository behavior;
- frontend feature flow;
- proposal or capture behavior without migration/security boundary changes.

Required evidence:

- full affected semantic suites on Linux;
- API or frontend integration slice;
- selected E2E journey if the user-visible contract changes;
- selected Windows contract if platform-sensitive files are touched;
- scanners selected by changed dependency/security surface.

### Tier R3 — cross-cutting or stateful

Typical changes:

- persistence/migrations;
- auth/authorization;
- executor/proposal application;
- MCP process/transport;
- SignalR/realtime;
- desktop startup/runtime;
- container/deployment;
- Context Fabric worker protocol;
- cross-cutting shared DTO or serialization contract.

Required evidence:

- full Linux semantic qualification;
- migrations where relevant;
- targeted Windows compatibility;
- integration and golden E2E;
- clean static/security checks;
- potentially container integration;
- hosted clean-control lane when execution controls change.

### Tier R4 — CI, release, supply chain, security control

Typical changes:

- `.github/workflows/**`;
- `actions/**`, CI planner/policy, runner broker;
- package bootstrap/install scripts;
- release/signing/provenance;
- security allowlists and enforcement levels;
- repository governance scripts.

Required evidence:

- GitHub-hosted execution only for control validation;
- actionlint/workflow contract tests;
- planner self-tests and policy-schema tests;
- full SHA-pin policy;
- representative plan fixtures;
- complete required qualification where runtime selection could be changed;
- maintainer review before enabling new self-hosted/release behavior.

## 4. Trust classes

Risk and trust are independent. A small workflow change is R4 even if it changes one line.

| Trust class | Input | Execution eligibility |
|---|---|---|
| T0 | trusted control-plane code from protected base | GitHub-hosted control jobs |
| T1 | same-repository PR by maintainer/approved agent, no CI-control changes | isolated self-hosted heavy jobs allowed; no secrets |
| T2 | same-repository PR changing workflow, planner, package bootstrap, release, security controls | GitHub-hosted only until merged and requalified |
| T3 | fork, Dependabot-like untrusted branch, collaborator without elevated trust, ambiguous actor | GitHub-hosted read-only; no secrets; no persistent self-hosted runner |
| T4 | protected tag/release | dedicated release environment and clean runner; explicit credentials only in protected steps |

The planner must not rely solely on `actor == maintainer`. A compromised dependency script or agent-generated workflow can be dangerous even when committed by the owner.

## 5. Control-plane integrity

A pull request can change `.github/workflows/**`. Runner labels and an in-workflow trust check are therefore **not a hard security boundary**: the code under review can attempt to create a different job that targets the same repository-level self-hosted runner.

### Preferred organization-owned topology

For the strongest practical design, transfer the private repository to a GitHub organization and use two GitHub-native controls:

1. **Runner group restricted to selected workflows.** The self-hosted Linux/Windows runners belong to an organization runner group that accepts jobs only from one trusted reusable workflow, for example:

   ```text
   ORGANIZATION/Taskdeck/.github/workflows/ci-heavy-trusted.yml@refs/heads/main
   ```

   A Taskdeck PR may request this workflow, but cannot replace it with an arbitrary self-hosted job. The trusted callee revalidates PR number, current head/merge SHA, actor/trust class, and control-path changes before checking out or executing the proposed code.

2. **Ruleset-required workflow.** The merge gate is an organization ruleset workflow sourced from a separately protected private CI-policy repository or protected organization workflow. Because the product PR cannot edit that workflow, it cannot rename a trivial job to spoof `Smart CI / Required Gate`.

The trusted CI-policy repository should be small:

```text
.github/workflows/taskdeck-required.yml
.github/workflows/taskdeck-heavy-trusted.yml
policy/max-assurance.v1.json
locked action references
```

Taskdeck retains repository-specific test ownership and commands. The control workflow treats those files as untrusted data, validates them, and applies a conservative policy ceiling. A product PR may ask for more validation; it may not ask for less than the trusted ceiling.

### Personal-account fallback

A personal repository has no organization runner-group workflow restriction. In that mode:

- do not treat runner labels as authorization;
- keep the self-hosted path owner-only, no external collaborators/forks, no secrets;
- start a clean or disposable VM only after reviewing the PR's workflow/control diff;
- prefer one-job ephemeral registration or a frequently reset isolated VM;
- keep a hosted-only full-qualification mode;
- move to an organization before accepting external contributions or attaching valuable credentials.

GitHub workflow execution protections can add actor/event restrictions where available, but they complement rather than replace isolation and trusted-workflow restriction.

## 6. Event topology

### Hosted job granularity

GitHub-hosted billing rounds each job's execution to whole minutes. Consolidate compatible sub-minute checks—docs governance, policy/schema validation, workflow contract, small static audits—into one hosted control job when they share the same trust and permission boundary. Keep jobs separate when they need different permissions, independent required evidence, isolation, or meaningful parallelism. Do not combine long semantic jobs merely to reduce job count; optimize critical path and failure diagnosis as well as rounding.

### Pull request: substantive qualification

```text
pull_request
  → hosted planner/control job
  → selected fast and semantic checks
  → selected isolated self-hosted heavy checks
  → stable aggregate gate
  → optional auto-merge after branch is current
```

Use the pull-request merge ref so tests see the proposed merge result. Require the branch to be up to date before merge because a private personal repository cannot rely on merge queue.

Draft behavior:

- new draft: R0/R1 feedback and planner only;
- ready-for-review or `ci:qualify`: full selected plan;
- R3/R4: qualify regardless of draft if policy demands early feedback.

Push updates cancel superseded runs for the same PR.

### Push to `main`: landed verifier

Do **not** repeat the full PR plan by default.

Run:

- verify the landed commit is associated with a successfully qualified PR and expected policy version;
- docs/index/repository invariant sanity;
- one very small startup/smoke contract;
- direct-push/bypass detection.

If no valid PR qualification receipt exists, escalate to the full plan. This protects emergency or accidental direct pushes without charging every normal merge twice.

### Nightly: change-driven depth

One nightly coordinator should:

1. identify the last successfully deep-qualified `main` SHA;
2. calculate changes since that SHA;
3. skip expensive suites when no relevant changes exist;
4. run changed-domain full regressions;
5. collect slow-test, flake, dependency, and performance signals;
6. update the CI ledger.

Use a weekly full entropy sweep for:

- all Linux semantic suites;
- full Windows matrix;
- cross-browser E2E;
- full dependency and SAST scans;
- containers;
- performance/load;
- scheduled drift checks.

### Release

A tag or release candidate triggers:

```text
clean checkout of exact tag
→ full semantic qualification
→ platform packaging smoke
→ dependency and secret enforcement
→ SBOM/provenance
→ build artifacts
→ signing in protected environment when configured
→ artifact verification
→ release receipt
```

Do not treat a successful PR artifact as the release artifact. Rebuild from the immutable source state.

## 7. Runner architecture

### Recommended physical topology

The preferred form assumes an organization runner group restricted to the trusted reusable workflow on `main`.

```text
Organization ruleset-required workflow / protected CI-policy repository
  - immutable merge-control ceiling
  - invokes trusted reusable heavy workflow

GitHub-hosted clean control plane
  - planner and policy validation
  - diff Gitleaks
  - workflow/actionlint/security-control checks
  - CI/release changes
  - untrusted contributions
  - occasional macOS

Development desktop
  └─ isolated Linux VM (primary heavy runner)
       - .NET semantic tests
       - API shards
       - frontend
       - Playwright Chromium
       - containers/Testcontainers
       - nightly/load/mutation on demand

Laptop
  └─ isolated Windows VM or locked runner account
       - Windows launcher/path/process/desktop/MCP contracts
       - targeted Windows API shard
       - full Windows sweep only nightly/release
```

A Linux VM is preferable to a normal WSL distribution with the host drive mounted. The VM should have no shared clipboard, host folder mount, SSH agent forwarding, browser profile, cloud credentials, or personal repository checkout. The runner group should permit only the selected trusted reusable workflow; the workflow itself must revalidate the immutable target SHA before checkout.

### Availability policy

GitHub Actions has no safe automatic “self-hosted unavailable, transparently retry on hosted” primitive. Use an explicit mode:

```text
CI_EXECUTION_MODE = self-hosted | hosted | hybrid
```

Resolution rules:

- T2/T3/R4 always selects hosted.
- `ci:hosted` label or manual dispatch forces hosted.
- trusted heavy jobs select self-hosted while your development machine is on.
- when travelling or the VM is offline, set mode to hosted before qualifying the PR.
- a queued self-hosted job is visible and never silently skipped.

### Isolation stages

**Stage 1: isolated persistent VMs**

- adequate for a private solo repository;
- one CI job at a time per physical machine;
- no secrets;
- reset/rebuild on a schedule or suspected compromise;
- local package caches permitted inside the VM;
- separate unprivileged runner account;
- outbound network restricted as far as practical.

**Stage 2: ephemeral snapshot runners**

Adopt when external collaborators, private forks, high-value secrets, or broad agent autonomy arrive:

- start from a read-only clean snapshot;
- register a one-job ephemeral runner;
- execute one job;
- export only bounded logs/results;
- destroy the VM/disk overlay;
- retain runner diagnostics outside the destroyed instance.

Do not deploy Kubernetes/ARC merely for two machines. It introduces a control plane larger than the workload.

## 8. Test architecture

### Linux semantic baseline

Ordinary code PRs should normally run once on Linux:

- Domain tests;
- Application tests;
- CLI semantic tests;
- impacted API shards;
- frontend lint/typecheck/build/coverage;
- architecture tests;
- selected E2E.

### Windows compatibility contract

Windows pull-request coverage should target behavior that can actually differ:

- `.cmd`, `.ps1`, source launcher, packaged launcher;
- filesystem and path casing/separators;
- process supervision, stdio, shutdown, port allocation;
- named mutex/file locking/SQLite concurrency;
- desktop startup and browser launch;
- MCP HTTP/stdio host startup;
- Windows service/job-object behavior;
- installer/upgrade/uninstall in release qualification.

Until targeted coverage proves equivalence, retain the full Windows API suite in weekly and release qualification.

### API sharding

Introduce stable shards by behavioral ownership, not arbitrary test counts:

1. `api-core` — controllers, serialization, errors, ordinary CRUD.
2. `auth-security` — authentication, authorization, cross-user isolation, rate/abuse controls.
3. `capture-proposal-executor` — capture, triage, provenance, review, apply.
4. `mcp-contract` — in-process MCP tools/resources/contracts.
5. `mcp-process-http` — real startup, stdio/HTTP, process isolation, hostile write auth.
6. `journeys-realtime` — golden journeys, SignalR, SSE, background workers.
7. `persistence-migration` — migration bootstrap, export/import, deletion, SQLite/Postgres compatibility.

Initially map classes explicitly in a checked-in manifest. Then add traits or project separation. Record TRX history and rebalance only when a shard violates its time budget.

### Repair the Windows MCP harness

Investigate and replace:

- fixed sleeps with readiness events/health probes;
- repeated process launches with session fixtures where isolation remains valid;
- long polling intervals with bounded event-driven waits;
- nondeterministic teardown with explicit cancellation and kill-after-grace;
- repeated SDK restore/build with prepared test binaries;
- port-collision retries with reserved-port coordination;
- serial tests that are only accidentally serialized.

Do not merely shard a suite whose time is dominated by duplicated fixed waits.

### Frontend

PR:

- full lint/typecheck/build/unit/coverage on Linux once;
- Windows source-launcher and platform bridge tests only;
- selected Playwright Chromium journey for UI/API-impacting changes;
- visual/cross-browser only when paths/risk select it.

Nightly/release:

- full cross-browser;
- accessibility sweep;
- visual baselines;
- packaged frontend/Desktop smoke.

### Containers

PR container build only when Docker/deploy/runtime/package-lock boundaries change, or for R3 deployment changes. Use single-architecture build and avoid exporting an image artifact unless another job consumes it. Multi-architecture builds belong to release/deep qualification.

## 9. Planner and policy

### Inputs

- event and action;
- actor association and fork status;
- base/head/merge SHA;
- changed paths and statuses;
- generated dependency graph;
- PR labels;
- workflow/control-path changes;
- policy version;
- execution mode;
- recent qualification and flake data.

### Outputs

```json
{
  "risk": "R3",
  "trust": "T1",
  "selected": ["backend-semantic", "api-capture", "windows-process", "e2e-capture"],
  "skipped": [{"lane": "container-build", "reason": "no container boundary changed"}],
  "runnerClass": {"linux": "self-hosted-heavy", "control": "github-hosted"},
  "reasons": ["migration changed", "capture-to-apply boundary changed"],
  "policyVersion": "ci-policy.v1"
}
```

### Fail-closed rules

Full/high-risk plan when:

- planner crashes;
- JSON output does not validate;
- changed file is unmapped;
- dependency graph is stale;
- workflow/release/security control changes;
- base comparison cannot be established;
- policy version differs between planner and gate;
- a required selected job produces no receipt.

### Impact graph

Path groups are only the first layer. Add edges such as:

```text
Domain entity
→ Application services
→ API/MCP serialization
→ export/import/deletion
→ migrations if persistence changed
→ frontend consumers if DTO changed

Capture/proposal/executor
→ core-loop E2E
→ provenance/audit
→ authorization

Worker protocol
→ sidecar conformance
→ Windows process contract
→ container path
```

Every new production module must be mapped by an architecture/governance test.

## 10. Stable required gate

Create one always-running job:

```text
Smart CI / Required Gate
```

It receives:

- planner receipt;
- selected job results;
- required evidence manifests;
- allowed skip reasons;
- policy version;
- exact SHA.

It fails when:

- planning failed or escalated work did not run;
- a selected job failed/cancelled/timed out;
- a job was skipped without policy authorization;
- a receipt references another SHA or policy;
- required hosted clean-control evidence is absent;
- the run exceeded a hard safety budget without an authorized override.

Branch protection/ruleset should require this stable context, require the branch to be current, prevent force pushes/deletion, and apply to administrators unless an explicit audited break-glass procedure exists.

## 11. Caches and artifacts

### Self-hosted caches

Keep inside the isolated VM:

- NuGet package cache;
- npm cache, not `node_modules`;
- Playwright browser cache;
- Docker BuildKit cache with size/age pruning;
- compiled test assets only when keyed by SDK, lockfiles, configuration, and source hash.

Do not restore arbitrary caches into release/signing jobs.

### GitHub cache

Use for hosted jobs only when measured hit savings exceed upload/download overhead. Bound keys and retention. Never use cache as authoritative evidence.

### Artifacts

- success-path PR artifacts: none unless consumed;
- failure diagnostics: 7–14 days;
- qualification receipts/timing summaries: small JSON, 30–90 days or committed aggregated ledger;
- nightly reports: 14–30 days;
- release/SBOM/provenance: release retention policy;
- screenshots/traces: upload on failure, not every successful run.

## 12. Security and supply-chain controls

- Pin every external action to a full commit SHA and annotate the human-readable version.
- Enable the repository policy requiring full-SHA pins once the migration is complete.
- Deny `pull_request_target` workflows that check out or execute untrusted head code.
- Use `persist-credentials: false` where checkout does not need pushes.
- Default `GITHUB_TOKEN` to read-only; grant job-local permissions.
- Self-hosted jobs receive no repository/environment secrets.
- CI-control changes execute on hosted runners and cannot select themselves onto self-hosted runners. In the preferred organization topology this is enforced by runner-group selected-workflow restriction, not merely an `if` statement in PR-controlled YAML.
- The required merge workflow is sourced outside the product PR's write boundary, preferably through an organization ruleset workflow.
- Release credentials live only in protected environments and never in a long-lived normal runner.
- Treat package lifecycle scripts, test binaries, Dockerfiles, and generated scripts as arbitrary code.
- Validate artifact provenance before a downstream privileged job consumes it.
- Protect planner policy and aggregate-gate code as R4.

## 13. CI observability

Every run should emit a bounded `ci-run.json` containing:

- event, SHA, PR, policy version;
- risk/trust class;
- selected/skipped lanes and reasons;
- runner label/class, hosted versus self-hosted;
- queue delay, setup, execution, upload, total duration;
- hosted billable-minute estimate and cost estimate;
- self-hosted CPU/wall time;
- cache hit/miss and transferred bytes;
- tests run, failed, skipped, flaky/rerun status;
- top slow tests;
- artifact size and retention;
- critical-path contribution;
- qualification outcome.

Weekly report:

```text
P50/P95 PR critical path
aggregate runner-minutes per merged PR
hosted minutes and cost
self-hosted wall/CPU time
runner queue delay
selection rate per lane
failure yield per lane
flake and rerun rate
top slow-test regressions
duplicate exact-SHA qualifications
cache utility
artifact/cache storage
```

The important efficiency metric is not “jobs skipped.” It is:

> defects caught or risk retired per unit of critical-path time and compute.

## 14. Provisional service-level objectives

These are starting targets, not claims:

| PR class | Target critical path | Hosted control budget |
|---|---:|---:|
| R0/R1 | ≤5 min | ≤3 hosted min |
| ordinary R2 | ≤10 min | ≤5 hosted min |
| R3 | ≤20 min | ≤10 hosted min plus self-hosted heavy work |
| R4 | correctness first; ≤25 min desired | hosted clean-room required |
| normal main push | ≤5 min | tiny verifier only |

Reliability targets:

- ≥99% planner/gate availability;
- <1% false-green selection escapes discovered by nightly/release;
- <2% rerun/flake rate, then tighten;
- no lane with zero unique failure yield for 60 days without a keep/merge/remove review;
- no unexplained selected job;
- no self-hosted secret exposure.

## 15. Recommended implementation phases

### Phase 0 — measurement and decision

- record exact current run/minute/storage baseline;
- create CI tracker and ADR;
- define risk/trust vocabulary;
- inventory branch protection and Actions settings;
- decide initial VM topology and monthly hosted budget.

### Phase 1 — shadow planner

- add policy/schema/planner tests;
- planner runs but changes no job selection;
- compare plan against the existing full suite for 20–50 representative PRs;
- every nightly failure asks whether the planner would have selected the catching lane;
- missing selection becomes policy-test fixture.

### Phase 2 — stable gate and event repair

- add aggregate gate;
- register it in branch protection;
- require current branch;
- remove full `push main` duplicate, replacing it with landed verifier;
- remove `merge_group` as a private-personal dependency;
- retain manual full-qualification dispatch.

### Phase 3 — platform right-sizing

- Linux semantic baseline;
- targeted Windows compatibility project/filter;
- full Windows weekly/release;
- split API suite and repair MCP process harness;
- reduce frontend duplication;
- path/risk-gate containers and E2E.

### Phase 4 — isolated self-hosted execution

- decide personal fallback versus organization-owned hard boundary;
- in the preferred route, create a runner group restricted to the trusted reusable workflow and a ruleset-required control workflow;
- build Linux VM on desktop;
- build Windows runner on laptop/VM;
- no secrets, one job per host;
- add explicit hosted override;
- validate teardown/cache pruning/offline behavior;
- collect runner health and compute metrics.

### Phase 5 — deep/release consolidation

- one nightly coordinator with change detection;
- weekly full sweep;
- release clean-room qualification;
- artifact retention and cache budgets;
- action SHA-pin enforcement.

### Phase 6 — private cutover

- rehearse low-risk, high-risk, CI-control, and release-dry-run PRs;
- configure spending budget and alerts;
- verify runner fallback/offline behavior;
- verify required gate and direct-push detection;
- manually change visibility;
- repeat the test matrix in private mode;
- retain rollback instructions.

## 16. Recommended decision

Adopt the hybrid Smart CI Fabric.

Do not:

- migrate away from GitHub Actions for quota reasons alone;
- attach a runner directly to the everyday development environment;
- run arbitrary public/fork PRs on persistent self-hosted machines;
- duplicate complete Linux semantics on Windows for every PR;
- rely on merge queue for a private personal repository;
- make path filters the sole correctness mechanism;
- enable selection before a shadow-recall period;
- let the workflow being changed decide that it is safe to run itself on a privileged runner.
