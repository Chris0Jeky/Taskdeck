# Taskdeck Smart CI Issue Seeds

These are intentionally structured as issue seeds, not commands to open all work immediately. The in-repository agent must reconcile them with ADR-0013, ADR-0035, ADR-0052, #1173, #1275, #2237, existing labels/milestones, and current queue caps.

## CI-00 — Tracker and architecture decision

**Title:** `CI-00 [TRACKER][decision]: Smart CI Fabric and private-repository readiness`

**Outcome:** one accepted CI direction, bounded issue map, current-state evidence, and explicit human-only actions for visibility, billing, branch settings, and runner registration.

**Acceptance:**

- [ ] Next unused ADR records event topology, trust classes, planner/gate, platform strategy, runner isolation, observability, and release boundary.
- [ ] ADR-0013 and ADR-0052 relationship is explicit: retained/amended/superseded portions named.
- [ ] Every child has one primary outcome and dependency map.
- [ ] No agent changes repository visibility, billing, branch settings, or runner registration without explicit authorization.

---

## CI-01 — CI baseline and evidence ledger

**Title:** `CI-01: Measure Taskdeck CI cost, critical path, duplication, flake rate, and lane yield`

**Scope:** GitHub API report over a stated date range; per-workflow/job duration; OS; billable estimate; duplicate PR/main/merge-group work; artifacts/cache; reruns; slow tests; lane failure yield.

**Acceptance:**

- [ ] One reproducible command emits JSON and Markdown.
- [ ] Method/date/accounting assumptions are recorded.
- [ ] PR #2280 timings are used only as a historical sample, not the new baseline.
- [ ] No secrets or user content enter the ledger.

---

## CI-02 — Versioned planner and policy in shadow mode

**Title:** `CI-02: Deterministic risk/impact planner with fail-closed policy and shadow-recall report`

**Scope:** `ci/policy.v1.json`, schema, path/dependency map, planner, fixtures, `ci-plan.json`, unknown-path escalation, risk R0–R4, trust T0–T4.

**Acceptance:**

- [ ] Planner output is deterministic for exact input.
- [ ] Every production/workflow path is mapped or deliberately full-escalated.
- [ ] Planner failure selects full qualification.
- [ ] Shadow mode changes no existing job selection.
- [ ] Recall report compares plans with full-suite failures for the evaluation window.

**Depends on:** CI-01.

---

## CI-03 — Stable aggregate gate and event topology

**Title:** `CI-03: Smart CI / Required Gate, branch-current contract, and landed-commit verifier`

**Scope:** always-running aggregate job; selected-job receipt verification; exact SHA/policy checks; replace full main push; direct-push fallback; remove private dependence on merge-group.

**Acceptance:**

- [ ] Stable gate fails on missing, skipped-without-reason, cancelled, timed-out, or wrong-SHA evidence.
- [ ] Branch protection requires the gate and branch-current behavior is proven.
- [ ] Compatible sub-minute hosted checks are consolidated where per-job rounding savings exceed lost isolation; permission boundaries remain explicit.
- [ ] Normal PR merge runs only bounded main verification.
- [ ] Direct/bypass push runs full qualification.
- [ ] Manual hosted full qualification remains available.

**Human setting:** branch protection/ruleset changes require maintainer action.

**Depends on:** CI-02.

---

## CI-04 — Trusted workflow boundary and isolated on-demand self-hosted runners

**Title:** `CI-04: Organization runner-group trusted-workflow boundary plus isolated Linux/Windows execution`

**Scope:** decide personal fallback versus organization ownership; preferred runner group restricted to one trusted reusable workflow on `refs/heads/main`; versioned VM/bootstrap/runbook; labels; one-job concurrency; VM start/stop broker; cleanup; health; hosted override; offline behavior; incident revocation.

**Acceptance:**

- [ ] The live decision records whether Taskdeck remains personal or moves to an organization.
- [ ] In organization mode, a product PR cannot acquire the runner except through the selected trusted reusable workflow; the workflow revalidates target SHA and control paths.
- [ ] In personal fallback, limitations are explicit and external contributors remain ineligible for persistent self-hosted execution.
- [ ] Linux VM has no host mounts/credentials and runs heavy semantic/E2E/container fixture.
- [ ] Windows runner runs the compatibility fixture.
- [ ] T2/T3/R4 fixtures cannot select self-hosted runners.
- [ ] Ordinary self-hosted jobs receive no secrets and read-only token permissions.
- [ ] Runner offline never creates a false-green gate.

**Human actions:** registration tokens and GitHub runner association are not performed autonomously.

**Depends on:** CI-02; can develop scripts before CI-03.

---

## CI-05 — Test ownership and impact graph

**Title:** `CI-05: Map production boundaries to semantic, integration, platform, journey, and release tests`

**Scope:** checked-in test ownership manifest; architecture check for unmapped modules; risk edges for auth, persistence, capture/proposal/executor, MCP, Context Fabric, frontend, desktop, container, release.

**Acceptance:**

- [ ] New production module without test ownership fails governance.
- [ ] Shared DTO/domain changes conservatively fan out.
- [ ] Planner consumes the generated map.
- [ ] Exact full-qualification escape hatch exists.

**Depends on:** CI-02.

---

## CI-06 — API sharding and MCP/process harness performance

**Title:** `CI-06: Split API integration by behavioral ownership and remove Windows MCP fixed-overhead hotspots`

**Scope:** stable API shards; TRX timing history; event-driven readiness; bounded teardown; prepared binaries; port coordination; rebalance policy.

**Acceptance:**

- [ ] Shards have explicit class/trait ownership and no test silently disappears.
- [ ] Full union equals the current API assembly test inventory.
- [ ] Windows MCP/process fixture P95 materially improves against recorded baseline.
- [ ] No timeout increase is accepted as the primary fix.
- [ ] Weekly/release full API union remains.

**Depends on:** CI-01, CI-05.

---

## CI-07 — Windows compatibility contract

**Title:** `CI-07: Replace duplicate full PR matrices with a targeted Windows compatibility contract`

**Scope:** launchers, PowerShell, filesystem/path, process/stdio, desktop, SQLite locking, MCP host, Job Object/worker containment; ordinary frontend/backend semantic duplication removed only after parity evidence.

**Acceptance:**

- [ ] Every historically Windows-only regression is represented or documented.
- [ ] Ordinary PR Windows duration is bounded by a stated target.
- [ ] Full Windows regression runs weekly and release-qualified.
- [ ] Planner selects expanded Windows coverage for platform-sensitive changes.

**Depends on:** CI-05, CI-06.

---

## CI-08 — Frontend, E2E, and container right-sizing

**Title:** `CI-08: One frontend semantic lane, journey-aware E2E, and risk-gated container builds`

**Scope:** Linux full frontend; Windows launcher subset; E2E journey map; upload traces on failure; containers only for affected boundaries; reusable build/cache evidence.

**Acceptance:**

- [ ] Lint/type/build/coverage run once per ordinary PR.
- [ ] UI/API journey change selects the relevant E2E smoke.
- [ ] Cross-browser remains nightly/release or explicitly selected.
- [ ] Container build runs on Docker/runtime/deploy risk and release.
- [ ] No successful artifact is uploaded without a consumer or retention reason.

**Depends on:** CI-05.

---

## CI-09 — Cache, artifact, and storage budget

**Title:** `CI-09: Bounded CI caches and artifact retention with measured utility`

**Scope:** cache inventory; hit/transfer timings; VM-local cache policy; BuildKit pruning; artifact-on-failure; retention classes; release isolation.

**Acceptance:**

- [ ] Each cache records owner, key inputs, max size, prune rule, and measured saving.
- [ ] Release jobs restore no untrusted build-output cache.
- [ ] PR artifacts default to failure-only/short retention.
- [ ] Storage report and budget warning exist.

**Depends on:** CI-01.

---

## CI-10 — Change-driven nightly and clean release qualification

**Title:** `CI-10: Consolidate deep CI ownership, skip no-change nightlies, and rebuild releases cleanly`

**Scope:** nightly coordinator; last-deep SHA; weekly full sweep; coverage/security/performance ownership; release from tag; SBOM/provenance; eliminate duplicate tag/release events where proven.

**Acceptance:**

- [ ] No-change nightly exits through an honest green coordinator receipt.
- [ ] Weekly full suite covers Linux, Windows, browser, dependency/SAST, container, and performance contracts.
- [ ] Release rebuilds from tag and verifies artifact digests/provenance.
- [ ] Mutation remains manual unless a separate decision changes ADR-0052.

**Depends on:** CI-01, CI-03, CI-05.

---

## CI-11 — Workflow supply-chain hardening

**Title:** `CI-11: Full-SHA action pinning, least privilege, and hosted-only CI-control qualification`

**Scope:** external action inventory; SHA pins; Renovate/Dependabot update process; read-only default token; `persist-credentials`; `pull_request_target` audit; artifact provenance.

**Acceptance:**

- [ ] Every external action is pinned to a full SHA with version comment.
- [ ] Repository full-SHA enforcement is ready for maintainer enablement.
- [ ] Workflow/planner/release/security changes run hosted-only.
- [ ] No untrusted PR code receives secrets or reaches ordinary self-hosted runners.

**Depends on:** CI-02.

---

## CI-12 — CI observability and budgets

**Title:** `CI-12: CI receipts, weekly dashboard, budget regression, flake and slow-test ownership`

**Scope:** `ci-run.json`; critical path; runner minutes/cost; queue; cache; test counts; slow tests; lane yield; flakes; exact-SHA duplication; weekly report/dashboard.

**Acceptance:**

- [ ] Every gate run emits validated receipt.
- [ ] Weekly report is reproducible and content-free.
- [ ] Repeated flaky tests create/update owned findings.
- [ ] Budget warnings identify the lane and reason, not only the total.

**Depends on:** CI-01, CI-02.

---

## CI-13 — Private-repository cutover acceptance

**Title:** `CI-13 [HUMAN GATE]: Prove private-mode CI, budgets, runner trust, and visibility cutover`

**Scope:** execute the cutover checklist; low/high/CI-control PRs; main; nightly; release dry-run; settings evidence; visibility change.

**Acceptance:**

- [ ] CI-02/03/04/07/11 prerequisites are proven or explicitly waived with reason.
- [ ] Hosted budget/alerts configured.
- [ ] Required gate and branch-current behavior verified.
- [ ] Runner offline/hosted override verified.
- [ ] Public assets/package/docs implications reviewed.
- [ ] Maintainer manually changes visibility and records post-cutover evidence.

**Human gate:** no agent changes visibility or billing.

---

## CI-14 — Protected required-workflow control plane

**Title:** `CI-14: Organization ruleset-required CI workflow outside the Taskdeck PR write boundary`

**Scope:** small private CI-policy repository or protected organization workflow; required workflow rule; policy ceiling; trusted reusable heavy workflow; source visibility/access; evaluation mode; rollback.

**Acceptance:**

- [ ] Taskdeck PRs cannot modify the workflow that supplies the required merge decision.
- [ ] The required workflow always runs and ignores PR path filters for enforcement.
- [ ] Product-repository policy input may request more checks but cannot reduce the trusted ceiling.
- [ ] Runner group is restricted to the selected trusted workflow.
- [ ] Evaluate mode proves behavior before active enforcement.

**Human actions:** organization/repository creation or transfer, ruleset activation, runner-group settings.

**Depends on:** CI-00, CI-02; recommended before CI-04 is enabled.

---

## CI-15 — Flake quarantine and test reliability contract

**Title:** `CI-14: Fail-visible flake classification, quarantine expiry, and retry governance`

**Scope:** classify infrastructure versus product failure; one diagnostic rerun max; quarantine file with owner/expiry/issue; required gate semantics; no silent retry-to-green.

**Acceptance:**

- [ ] A rerun is reported as flaky and does not erase the first failure.
- [ ] Quarantine entry requires issue, owner, reason, expiry, and compensating nightly coverage.
- [ ] Expired quarantine fails governance.
- [ ] Flake rate appears in CI-12 reports.

**Depends on:** CI-12.
