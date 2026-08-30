# Taskdeck Smart CI and Private-Repository Readiness Pack

**Prepared:** 2026-08-30  
**Repository audit target:** `Chris0Jeky/Taskdeck`  
**Purpose:** redesign Taskdeck's CI as a measured, policy-driven verification system that remains rigorous after the repository becomes private, while avoiding unnecessary hosted minutes, local compute, duplicated validation, and unsafe self-hosted execution.

This pack is a planning and implementation seed. It does **not** register runners, modify GitHub billing, change repository visibility, alter branch protection, create secrets, or claim that the example workflows can be copied without reconciling them against the live repository.

## Recommended decision

Keep **GitHub Actions as the control plane**. Replace the current “run almost everything on every important event” topology with a **Smart CI Fabric**:

1. A small GitHub-hosted planner classifies trust, changed surfaces, and risk.
2. A versioned policy maps the plan to tests, operating systems, and runner classes.
3. Linux is the primary semantic platform; Windows runs a deliberately bounded compatibility contract on ordinary pull requests.
4. Heavy trusted jobs run on isolated, on-demand self-hosted machines; clean-room/security/control jobs stay GitHub-hosted.
5. **Preferred hard boundary:** place the private repository in a GitHub organization and restrict a self-hosted runner group to one trusted reusable workflow pinned to `refs/heads/main`. Product PRs cannot author arbitrary jobs that acquire the runner.
6. Use an organization ruleset-required workflow, ideally stored in a separately protected private CI-policy repository, so a Taskdeck PR cannot rewrite or spoof its own merge gate. A personal-account fallback is documented, but it has a weaker control-plane boundary.
7. One stable aggregate check, `Smart CI / Required Gate`, is the branch-protection contract.
6. Pull requests receive the substantive qualification; `main` receives a short landed-commit verifier instead of the same full suite again.
7. Nightly work is change-driven, with a weekly full entropy/platform sweep and release-only clean qualification.
8. Every job emits cost, duration, selection reason, failure yield, and flake evidence.

## Files

| File | Purpose |
|---|---|
| `TASKDECK_SMART_CI_BLUEPRINT.md` | Master architecture and rollout plan |
| `REPOSITORY_CI_AUDIT_2026-08-30.md` | Evidence-based audit of the current Taskdeck estate |
| `DRAFT_ADR_SMART_CI_AND_PRIVATE_RUNNER_TRUST.md` | Draft ADR for reconciliation into the repository |
| `CI_ISSUE_SEEDS.md` | CI-00 through CI-14 issue/epic seeds |
| `AGENT_EXECUTION_PROMPT.md` | Prompt to give an in-repository agent |
| `PRIVATE_REPO_CUTOVER_CHECKLIST.md` | Human and technical cutover checklist |
| `RUNNER_TOPOLOGY_AND_THREAT_MODEL.md` | Self-hosted runner design and threat model |
| `CURRENT_CI_MEASUREMENTS.json` | Snapshot of measured/current facts used by the design |
| `ci-policy.example.json` | Example versioned CI policy |
| `ci-policy.schema.json` | JSON schema for that policy |
| `ci-plan.example.json` | Example planner output and audit receipt |
| `ci-metrics.schema.json` | Suggested per-run metrics schema |
| `smart-ci-pr.example.yml` | Reference hosted/required pull-request orchestration pattern |
| `smart-ci-heavy-trusted.example.yml` | Trusted reusable workflow eligible for the restricted runner group |
| `smart-ci-main.example.yml` | Reference post-merge topology |
| `smart-ci-nightly.example.yml` | Reference change-driven nightly topology |
| `runner-broker.example.ps1` | Safe on-demand Hyper-V runner broker skeleton |
| `architecture.svg` | Editable architecture diagram |
| `ci-dashboard.html` | Interactive hosted-minutes and topology simulator |

## Implementation order

The safe order is:

```text
measure current estate
→ introduce planner in shadow mode
→ validate planner recall against existing full CI
→ create stable aggregate gate
→ split Linux semantic / Windows compatibility contracts
→ eliminate duplicate main-push qualification
→ introduce isolated self-hosted execution
→ consolidate nightly/release policy
→ perform private-repository cutover rehearsal
→ change repository visibility manually
```

The repository should remain public until the new gate, runner trust boundary, and hosted fallback have been exercised. A self-hosted runner should not be attached to the public repository while arbitrary public pull requests can target it.
