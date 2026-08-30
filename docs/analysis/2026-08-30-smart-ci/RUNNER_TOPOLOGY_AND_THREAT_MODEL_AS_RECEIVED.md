# Taskdeck Self-Hosted Runner Topology and Threat Model

## Decision summary

Use self-hosted runners as an **isolated execution plane for trusted heavy jobs**, not as a replacement for GitHub-hosted clean runners.

The strongest practical control is to place Taskdeck in a GitHub organization and restrict the runner group to a selected reusable workflow on `refs/heads/main`. Labels route work; the runner-group workflow restriction authorizes it. A personal repository lacks that hard workflow boundary and must use a more conservative owner-only/ephemeral fallback.

Recommended initial topology:

```text
Desktop host
  └─ Hyper-V/VM: td-ci-linux
       - Ubuntu LTS
       - one GitHub Actions runner
       - labels: self-hosted, taskdeck, trusted, linux, x64, heavy
       - Docker/BuildKit, .NET, Node, Playwright
       - no host drives, secrets, SSH agent, browser profile, or cloud tokens

Laptop or second VM
  └─ td-ci-windows
       - isolated Windows user/VM
       - labels: self-hosted, taskdeck, trusted, windows, x64, compatibility
       - targeted Windows contracts
       - one job at a time

Organization runner group
  - private repositories only
  - selected trusted workflow only
  - one-job concurrency per physical host

Organization ruleset-required workflow / protected CI-policy repository
  - product PR cannot edit its own required gate

GitHub-hosted
  - control planner and gate
  - workflow/release/security-control changes
  - untrusted/fork/dependency-bot changes
  - diff secret scan
  - release clean-room and macOS
```

## Assets to protect

- personal files and browser sessions;
- source repositories beyond Taskdeck;
- GitHub credentials/PATs/SSH agents;
- LLM/provider keys;
- signing identities and certificates;
- cloud/deployment credentials;
- Taskdeck user data and development databases;
- package registries and Docker credentials;
- release provenance and tags;
- CI cache integrity;
- the ability to merge or modify workflows.

## Adversaries and failure modes

1. **Malicious or mistaken agent-authored code** executes shell commands during build/test.
2. **Compromised third-party action** executes in a job.
3. **Dependency lifecycle script** exfiltrates environment/filesystem state.
4. **Private collaborator or fork PR** targets a persistent runner.
5. **Cache poisoning** carries malicious output into a later job.
6. **Artifact substitution** crosses from an untrusted job into a privileged release job.
7. **Runner persistence** leaves modified binaries, services, PATH entries, hooks, or credentials after a job.
8. **Docker socket access** enables host-level compromise inside a runner VM.
9. **Workflow modification** selects a privileged runner or requests secrets.
10. **Runner update drift** leaves an unsupported runner or toolchain.
11. **Resource exhaustion** harms the development machine or corrupts concurrent development work.
12. **Offline runner ambiguity** silently leaves required work queued or encourages bypassing the gate.

## Core controls

### Isolation

- VM, not ordinary host session.
- No host filesystem mounts.
- No shared clipboard or drag/drop where practical.
- No forwarded SSH agent.
- Separate virtual disk.
- NAT/outbound-only networking.
- Do not expose inbound runner ports; runner connects outward to GitHub.
- Dedicated unprivileged runner account.

### Credentials

- No static PAT inside the runner.
- No release/signing/cloud secrets.
- Read-only default `GITHUB_TOKEN` permissions.
- Environment secrets unavailable to ordinary self-hosted jobs.
- Repository checkout uses `persist-credentials: false` unless explicitly required.

### Scheduling

- One concurrent heavy job per physical machine.
- Trust classifier decides eligibility.
- The selected trusted reusable workflow is pinned to `refs/heads/main` and revalidates the target SHA before checkout.
- Runner-group workflow restriction prevents a product PR from acquiring the runner through a new arbitrary workflow.
- Workflow/control changes cannot run their own modified heavy path on self-hosted.
- Explicit hosted override for travel/offline periods.
- Required gate blocks while a selected runner job is pending.

### Cleanup and reset

Persistent VM stage:

- clean workspace before and after each job;
- remove untracked files and worktrees;
- prune Docker images/build cache to bounded limits;
- clear temp/spool directories;
- scan for unexpected services/processes/startup entries;
- rotate/rebuild VM monthly or after any suspicious job;
- keep the golden image/versioned bootstrap scripts.

Ephemeral stage:

- clone clean snapshot;
- one-job registration;
- execute;
- export bounded logs/receipts;
- destroy overlay;
- preserve runner diagnostic logs externally.

### Network

Allow only required outbound destinations where feasible:

- GitHub Actions endpoints;
- package registries used by .NET/npm/Playwright/Docker;
- no access to the host/LAN management plane;
- no access to local Taskdeck databases or personal services.

A job that needs live external-provider credentials belongs in a separately protected environment, not on the ordinary runner.

## Trust matrix

| Scenario | Self-hosted? | Secrets? | Notes |
|---|---:|---:|---|
| Maintainer product PR, no CI-control change | Yes, isolated | No | heavy execution permitted |
| Agent-authored product PR | Yes after planner classifies T1 | No | ownership does not waive isolation |
| Workflow/planner/action change | No | No | hosted clean-control only |
| Private fork/collaborator PR | No by default | No | hosted read-only |
| Dependabot/dependency update | Hosted by default | No | package scripts are code |
| Nightly on protected `main` | Yes | No | appropriate for heavy/regression work |
| Tag release qualification | Dedicated clean runner | Protected only | no reuse of ordinary VM state |
| Signing | Dedicated protected context | Yes, narrowly | never on ordinary self-hosted runner |

## Initial machine use

### Desktop

Use the desktop for Linux-heavy work because it likely has the better CPU, RAM, storage, and thermal capacity. The runner VM can be started while developing and stopped after PR qualification. This avoids an always-on CI server.

Suggested allocation, adjusted to actual hardware:

- 6–12 vCPU;
- 12–24 GB RAM;
- 100–200 GB expandable disk;
- one concurrent job;
- Docker cache capped by size and age.

Do not allocate so aggressively that the IDE, browser, local agent, and runner compete and make both development and CI slower.

### Laptop

Use for bounded Windows compatibility. Keep it optional:

- run when plugged in and available;
- ordinary PRs target only the Windows contract;
- full Windows matrix runs weekly/release or via `ci:windows-full`;
- hosted Windows override when away.

## Operational runbook

Before starting:

1. update VM image/toolchain;
2. start VM;
3. verify runner labels and one-job concurrency;
4. ensure no host shares/credentials are present;
5. set `CI_EXECUTION_MODE=self-hosted` or `hybrid` through the agreed manual control;
6. push/mark PR ready.

After qualification:

1. inspect runner health and abnormal processes;
2. stop runner service/VM;
3. optionally prune caches according to bounded policy;
4. leave GitHub runner registered but offline, or use one-job ephemeral registration in the later model.

Incident response:

1. stop VM immediately;
2. revoke runner registration;
3. invalidate any token that could have been exposed;
4. discard VM disk/overlay;
5. inspect workflow/action/dependency changes on a clean host;
6. rebuild from golden image;
7. record incident and affected CI receipts.

## Revisit triggers

Move to ephemeral runners or dedicated hardware when any occurs:

- external collaborators/forks begin contributing;
- ordinary CI needs secrets;
- concurrent queues routinely exceed one job per host;
- runner compromise or persistence anomaly occurs;
- releases/signing become frequent;
- personal development performance is materially affected;
- hosted beta operations require an always-available build plane.
