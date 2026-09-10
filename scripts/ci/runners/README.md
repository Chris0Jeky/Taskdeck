# Isolated self-hosted runner tooling

Last Updated: 2026-09-10 · Issue: CI-04 `#2328` · Decision: ADR-0066 sections 7–9 · Threat model: [RUNNER_TOPOLOGY_AND_THREAT_MODEL.md](../../../docs/ci/RUNNER_TOPOLOGY_AND_THREAT_MODEL.md)

Nothing in this directory associates a runner with GitHub. The scripts contain no association
credential, package downloader, repository-setting operation, or GitHub API call. They prepare and
verify only the fixed guest-local account, filesystem, policy, cleanup-hook, architecture, and
preinstalled-toolchain contract.

## Shipped preparation contract

| File | Contract |
| --- | --- |
| `bootstrap-linux.sh` | Defaults to non-mutating `Check`; explicit root-only `Apply` creates or validates the locked `taskdeck-runner` account, fixed directories, immutable policy and hook. Checks x64, Node 24.13.1, .NET SDK 8.0.415, Git, rootless Docker, and an available BuildKit builder. |
| `bootstrap-windows.ps1` | Defaults to non-mutating `Check`; explicit Administrator-only `Apply` validates an existing enabled local `taskdeck-runner` account has only the Builtin Users membership and the fixed account profile, then creates fixed cleanup directories and protected ACLs. Checks x64, Node 24.13.1, .NET SDK 8.0.415, and x64 Git. It never creates the account or accepts an account credential. |
| `cleanup-linux.sh` | Installed as an immutable hook. Clears only children of validated service-home, work, temp, npm, NuGet, Playwright, and Docker-client cache roots, removes rootless job containers/volumes/networks, and applies both age and size limits to BuildKit. |
| `cleanup-windows.ps1` | Installed as an immutable hook. Clears only children of the validated fixed tool profile-state, work, temp, npm, NuGet, and Playwright cache roots. It does not clear the active Windows account profile or HKCU. |
| `Invoke-TaskdeckCiRunnerVm.ps1` | Starts, stops, or inspects already-created Hyper-V guests. It does not create or associate a VM. |
| `runner-bootstrap-contract.test.mjs` | Pins the verify-first, containment, output, account, and no-association contract. The Smart CI test directory imports it so the existing planner self-test discovers it. |

Both cleanup hooks:

- load only the immutable, fixed policy file installed outside the runner application and writable
  work/cache trees;
- reject filesystem roots, conventional host user-profile paths, UNC/network paths, shared/mounted host
  filesystems, and any symlink, junction, or reparse point in a governed root or ancestor;
- ignore job-controlled workspace, temp, home, and cache variables;
- run as the dedicated unprivileged service account and fail for any other identity;
- emit stable action codes and counts, never paths, user names, environment values, or child names;
- return nonzero on validation, cleanup, or prune failure; and
- impose a 120-second internal limit because the runner does not supply a hook timeout.

### Canonical guest roots

| Purpose | Linux | Windows |
| --- | --- | --- |
| Immutable policy and hooks | `/etc/taskdeck-runner/` | `C:\ProgramData\TaskdeckRunner\Policy\` |
| Account home/profile | `/var/lib/taskdeck-runner/home` (cleared) | `C:\TaskdeckRunner\Profile` (validated, not cleared) |
| Cleared tool profile-state | Same as account home | `C:\TaskdeckRunner\ProfileState` |
| Work | `/var/lib/taskdeck-runner/work` | `C:\TaskdeckRunner\Work` |
| Temp | `/var/lib/taskdeck-runner/temp` | `C:\TaskdeckRunner\Temp` |
| Bounded caches | `/var/cache/taskdeck-runner/` | `C:\TaskdeckRunner\Cache\` |
| Linux rootless container state | `/var/cache/taskdeck-runner/buildkit` | Not used by the Windows compatibility runner |

The policy/hook and runner-state parent roots are owned by root or the local Administrators group.
Within the roots managed by these scripts, the service account has read-and-execute access to those
parents and write access only to the fixed Linux home or Windows profile-state, work, temp, and cache
child roots. On Windows, writable roots use inherit-only child permissions and do not grant the
account permission to delete or replace the roots themselves. The active Windows account profile is
validated but is not ACL-managed or cleared by these scripts. The bootstraps reject a different root
instead of accepting a path parameter.

## Image verification

Run these only inside the isolated guest whose host-sharing controls have already been inspected.
`Apply` does not install the toolchain. It is idempotent preparation for a human-built, checksum-pinned
image and fails closed when that image does not match the contract.

Linux:

```bash
sudo scripts/ci/runners/bootstrap-linux.sh
sudo scripts/ci/runners/bootstrap-linux.sh --action Apply --dry-run
sudo scripts/ci/runners/bootstrap-linux.sh --action Apply
```

Windows, from an elevated PowerShell session for `Apply` or `-WhatIf`:

```powershell
& scripts/ci/runners/bootstrap-windows.ps1
& scripts/ci/runners/bootstrap-windows.ps1 -Action Apply -WhatIf
& scripts/ci/runners/bootstrap-windows.ps1 -Action Apply
```

The existing Windows account must have exactly one direct local-group membership, Builtin Users, and
its `ProfileImagePath` must already be `C:\TaskdeckRunner\Profile`. Linux permits only the account's
dedicated primary group and no supplementary group. These allowlists fail closed instead of trying to
enumerate every root-equivalent group name.

During the later Windows service setup, configure the runner work directory as
`C:\TaskdeckRunner\Work`; set `HOME` and `DOTNET_CLI_HOME` to
`C:\TaskdeckRunner\ProfileState`; set `TEMP` and `TMP` to `C:\TaskdeckRunner\Temp`; and set
`npm_config_cache`, `NUGET_PACKAGES`, and `PLAYWRIGHT_BROWSERS_PATH` to their fixed cache roots in
the table above. Keep the OS-managed `USERPROFILE` at `C:\TaskdeckRunner\Profile`. These mappings
bound cooperative tool state only. The hook clears only the enumerated fixed roots, so arbitrary code
can still persist elsewhere in the active account profile or HKCU. A persistent Windows VM is not a
security reset; unexpected state makes the guest suspect and requires discard and rebuild.

Linux `Check` must run as root or as the service account so it can prove both the locked-account and
rootless-daemon boundary. A first Linux `Apply` may create the account and fixed roots, then stop at
the rootless-Docker check; configure the pinned rootless daemon as that account and rerun `Apply`.
That bounded partial state contains no GitHub association and is safe to reconcile idempotently.

There is deliberately no global Playwright-package check. Playwright is a repository dependency,
so the honest runtime proof is a repository job that installs the lockfile-pinned dependencies and
launches the bundled browser through the repository's Playwright test command. Golden-image OS and
browser dependencies are installed separately from checksummed inputs; the first real browser launch
remains part of the post-cutover workload rehearsal.

## Hook pinning after the private cutover

After the later maintainer-only association step, configure the runner service with these exact
absolute hook paths:

| Guest | `ACTIONS_RUNNER_HOOK_JOB_STARTED` | `ACTIONS_RUNNER_HOOK_JOB_COMPLETED` |
| --- | --- | --- |
| Linux | `/etc/taskdeck-runner/hooks/cleanup-linux.sh` | `/etc/taskdeck-runner/hooks/cleanup-linux.sh` |
| Windows | `C:\ProgramData\TaskdeckRunner\Policy\Hooks\cleanup-windows.ps1` | `C:\ProgramData\TaskdeckRunner\Policy\Hooks\cleanup-windows.ps1` |

The runner invokes each hook synchronously as its service account. A pre-job nonzero exit prevents the
job from running and marks it failed. Both post-job hooks also return nonzero on failure, but whether
that changes an already-completed job conclusion is **not yet proven** for this runner version. Until
the real-run rehearsal establishes that behavior, any post-job failure makes the guest suspect: stop
it, remove its association, and rebuild rather than treating the completed result as clean evidence.

The hook variables are pinned only after association. This repository does not write the runner
service environment or application directory.

## Remaining human and runtime gates

The static/check contract above does not prove or perform any of these steps. VM creation and all
host-level changes remain human operations:

1. Create the Linux and Windows VMs; disable host drives, shares, clipboard, agent forwarding, browser
   profiles, inbound ports, and other host integration; allocate bounded guest resources.
2. Install the exact toolchain and guest dependencies from checksum-verified inputs. Configure Linux
   rootless Docker to keep its state at the canonical root and make one BuildKit builder available.
   Cleanup removes job containers, volumes, and custom networks, prunes images and BuildKit entries
   older than seven days, caps retained BuildKit data at 20 GB, and rejects total container state above
   30 GiB rather than silently accepting a full disk.
3. Complete the private-repository cutover. No self-hosted runner may exist while the repository is
   public.
4. Perform runner registration and service setup as the maintainer, outside the repository; pin the
   absolute hooks above and verify exact labels:
   - Linux: `self-hosted, taskdeck, trusted, linux, x64, heavy`
   - Windows: `self-hosted, taskdeck, trusted, windows, x64, compatibility`
5. Run the no-credential real workload and Playwright browser-launch rehearsal. Prove normal, failed,
   cancelled, disk-exhausted, pre-hook-failed, post-hook-failed, fixed profile-state cleanup, inspection
   for residue outside the bounded roots (including the active Windows account profile and HKCU), and
   host-containment cases. Do not treat bounded cleanup as proof of a globally clean machine.
6. Prove an offline runner leaves the required gate queued or failed, never green. Only then may the
   maintainer consider moving `CI_EXECUTION_MODE` away from `hosted`.

The human gates remain in `OUTSTANDING_TASKS.md` as SC-6 and SC-7. An anomaly follows the incident
path in the threat model: stop, disassociate, rotate any potentially exposed external material, discard
the guest disk, and rebuild from the reviewed image inputs.
