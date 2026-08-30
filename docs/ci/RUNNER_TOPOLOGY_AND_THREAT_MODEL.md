# Self-hosted runner topology and threat model (personal-account mode)

Last Updated: 2026-08-30 · Decision: ADR-0066 §Decisions 7–9 · Issue: CI-04 `#2328` · Upgrade path: CI-14 `#2338`

## Decision summary

Self-hosted runners are an **isolated execution plane for trusted heavy jobs**, not a replacement for
GitHub-hosted clean runners. On a personal GitHub Pro account there is no organization runner group
and therefore no GitHub-native way to restrict a runner to one trusted workflow: **runner labels and
`if:` expressions are routing, not authorization.** The personal-mode boundary is built from
isolation, absence of secrets, owner-only operation, demand-driven availability, and the rule that a
runner is **never registered while the repository is public** (a fork PR could target it).

```text
Development desktop
  └─ Hyper-V VM: td-ci-linux
       - Ubuntu LTS, one GitHub Actions runner, unprivileged runner account
       - labels: self-hosted, taskdeck, trusted, linux, x64, heavy
       - .NET 8, Node 24, Docker/BuildKit, Playwright Chromium
       - no host drives, clipboard, SSH agent, browser profile, cloud tokens
       - NAT outbound only; no inbound ports (the runner connects out to GitHub)
       - one job at a time; started before qualifying a PR, stopped after

Laptop (or a second VM)
  └─ td-ci-windows
       - isolated Windows VM preferred; a stripped-down dedicated local account is the fallback
       - labels: self-hosted, taskdeck, trusted, windows, x64, compatibility
       - the Windows compatibility contract only (CI-07); full Windows sweep weekly/release
       - one job at a time; optional — hosted Windows is the override when away

GitHub-hosted (always)
  - planner, gate, secret/dependency/SAST scans, workflow/control checks
  - T2/T3 changes (control-path edits, forks, Dependabot)
  - release clean-room, macOS, emergency fallback
```

## Assets to protect

Personal files and browser sessions · other repositories on the machines · GitHub credentials, PATs,
SSH agents · LLM provider keys · signing identities (`#2148`/`#2149`) · cloud/deployment credentials ·
Taskdeck user data and development databases · package-registry and Docker credentials · release
provenance and tags · CI cache integrity · the ability to merge or modify workflows.

## Adversaries and failure modes

1. Malicious or mistaken **agent-authored code** executing shell commands during build/test.
2. A **compromised third-party action** executing in a job (mitigated by SHA pinning, CI-11).
3. A **dependency lifecycle script** (npm, NuGet, pip) exfiltrating environment or filesystem state.
4. A **collaborator PR** targeting the persistent runner (private repo: collaborators only; forks off).
5. **Cache poisoning** carrying malicious output into a later job.
6. **Artifact substitution** from an untrusted job into a privileged release job.
7. **Runner persistence** — modified binaries, services, PATH entries, hooks, credentials left behind.
8. **Docker socket access** enabling VM-level compromise inside the runner VM.
9. **Workflow modification** selecting a privileged runner or requesting secrets.
10. **Runner/toolchain drift** leaving an unsupported runner.
11. **Resource exhaustion** harming the development machine or concurrent development work.
12. **Offline-runner ambiguity** leaving required work queued or tempting a gate bypass.

## Controls

### Isolation

VM, never an ordinary host session or a WSL distribution with the host drive mounted; no host
filesystem mounts; no shared clipboard/drag-drop; no forwarded SSH agent; separate virtual disk;
NAT/outbound-only networking; no inbound runner ports; dedicated unprivileged runner account.

### Credentials

No static PAT on the runner; no release/signing/cloud secrets; ordinary self-hosted jobs declare
`permissions: contents: read` only and check out with `persist-credentials: false`; environment
secrets are unavailable to them; a job that needs live provider credentials belongs in a separately
protected environment, not on the ordinary runner.

### Scheduling and trust

The planner decides eligibility: only **T1** plans (same-repo, no control-path change, maintainer or
approved agent) with risk ≤ R3 may resolve to a self-hosted class, and only when
`CI_EXECUTION_MODE` is `hybrid`/`self-hosted`. T2/T3/R4 always resolve hosted. The `ci:hosted`
label forces hosted for one PR. The maintainer starts the VM only after reviewing the PR's
control-path diff (the shadow planner prints it). A queued self-hosted job is visible in the gate as
pending — never skipped, never green.

### Cleanup and reset (persistent-VM stage)

Clean workspace before and after each job (untracked files, worktrees, temp/spool); prune Docker
images and BuildKit cache to bounded limits; scan for unexpected services/processes/startup entries
after each qualification day; rotate/rebuild the VM monthly or after any suspicious job; keep the
golden image and the versioned bootstrap scripts in `scripts/ci/runners/`.

### Ephemeral stage (later)

Clone a clean snapshot → register a one-job `--ephemeral` runner → execute → export bounded
logs/receipts → destroy the overlay → keep runner diagnostics outside the destroyed instance. Adopt
when any revisit trigger below fires. No Kubernetes / Actions Runner Controller for two machines.

### Network

Allow only what the jobs need where feasible: GitHub Actions endpoints; the .NET/npm/Playwright/Docker
registries; no host or LAN management plane; no local Taskdeck databases or personal services.

## Trust matrix

| Scenario | Self-hosted? | Secrets? | Notes |
| --- | --- | --- | --- |
| Maintainer product PR, no CI-control change | yes, isolated | no | heavy execution permitted |
| Agent-authored product PR | yes after the planner classifies T1 | no | ownership does not waive isolation |
| Workflow / planner / policy / action change | no | no | hosted clean-control only |
| Collaborator or fork PR | no by default | no | hosted read-only |
| Dependabot update | hosted by default | no | package scripts are code |
| Nightly on protected `main` | yes | no | heavy/regression work |
| Tag release qualification | dedicated clean hosted runner | protected only | never reuses ordinary VM state |
| Signing | protected context only (`#2149`) | narrowly | never on an ordinary runner |

## Machine use

**Desktop (Linux VM).** Best CPU/RAM/thermals; start while developing, stop after qualification.
Suggested allocation to be measured on the hardware, never guessed: 6–12 vCPU, 12–24 GB RAM,
100–200 GB expandable disk, one job, Docker cache capped by size and age. Do not starve the IDE,
browser and local agent.

**Laptop (Windows).** Plugged in and available; ordinary PRs target only the compatibility contract;
the full Windows matrix runs weekly/release or via the `ci:windows-full` label; hosted Windows when
away.

## Operational runbook

Before qualifying: update VM image/toolchain → start the VM (`Invoke-TaskdeckCiRunnerVm.ps1 -Action
Start`) → verify labels and one-job concurrency → confirm no host shares/credentials → set
`CI_EXECUTION_MODE` → push/mark the PR ready. After: inspect runner health and abnormal processes →
stop the VM → prune caches per policy → leave the runner registered but offline (or, later, use
one-job ephemeral registration).

Incident: stop the VM → revoke the runner registration → invalidate any token that could have been
exposed → discard the VM disk/overlay → inspect workflow/action/dependency changes on a clean host →
rebuild from the golden image → record the incident and the affected CI receipts on CI-00.

## Revisit triggers (move to ephemeral runners, dedicated hardware, or the CI-14 organization boundary)

External collaborators or forks begin contributing · ordinary CI needs a secret · queues routinely
exceed one job per host · a compromise or persistence anomaly · frequent releases/signing · personal
development performance materially affected · hosted beta operations need an always-available build
plane.

## Human actions

Runner registration tokens, `gh`/UI runner registration, VM creation on personal hardware, and the
`CI_EXECUTION_MODE` flip to `hybrid` are maintainer actions recorded in `OUTSTANDING_TASKS.md` §J;
agents prepare the scripts and the checklist and never register a runner.
