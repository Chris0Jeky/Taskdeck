# Private-repository cutover checklist (personal GitHub Pro account)

Last Updated: 2026-09-18 · Decision: ADR-0066 · Executable issue: CI-13 `#2337` (record evidence there) · Human actions: `OUTSTANDING_TASKS.md` §J

Taskdeck's development repository goes **private for the v0.3.0 release**. The cutover preserves a
public downloadable distribution while moving development, issues, CI, and the control plane behind
the private boundary.

Agents prepare implementation, dry runs, and evidence. The maintainer alone performs billing,
visibility, branch-protection, package-visibility, credential, publication, and runner-association
actions. Never infer a human action from repository state or an issue comment.

## Non-negotiable ordering and budget rules

- GitHub Actions has a **$0 hard ceiling** with stop-on-limit enabled.
- After repository privacy, hosted execution is Linux/control/security only. No private hosted Windows
  or macOS job may be scheduled without an explicit budget re-ruling recorded on `#2337`.
- Release GHCR packages become explicitly public and are anonymously verified **before** repository
  privacy, then verified again afterward.
- `sha_pinning_required: true` is already enabled and verified. It is not a cutover mutation.
- `Smart CI / Required Gate` becomes required **only after** the repository is private. Registering it
  while public leaves a spoofable same-name check interval for forks.
- Branch-current strictness, administrator enforcement, and break-glass are chosen from completed
  observation evidence, not pre-ruled here.
- Runner isolation, cleanup, offline, override, reset, and revocation are proved before any GitHub
  association.
- Immediately after privacy and before runner association, private-mode qualification runs through
  CI-17 `#3170`'s trusted, fail-closed **Linux-only mode across every required, called, and reusable
  workflow**.
- Only after that Linux-only rehearsal succeeds may already-proven isolated runners be associated.
- The final exact-tag Windows archive and Windows-specific qualification use a secret-safe isolated
  release runner. A repository or release credential must never reach an ordinary hosted or
  persistent CI-04 runner.
- The qualified private Release is published before `Chris0Jeky/taskdeck-release` consumes and
  republishes it.

## A. Settled decisions and remaining maintainer choices

- [x] Personal GitHub Pro account confirmed in the existing ruling record.
- [x] Actions posture ruled to a $0 hard ceiling with stop-on-limit enabled.
- [x] Development repository ruled private for v0.3.0.
- [x] Public source/release mirror ruled as `Chris0Jeky/taskdeck-release`.
- [x] GitHub Pages remains public; public install, licensing, launch-kit, support, security, and
  `awesome-selfhosted` references point to the approved public surfaces.
- [x] Release GHCR packages remain public across the repository-visibility change.
- [x] Laptop/isolated Windows execution is the Windows path after privacy; private hosted Windows is
  not the fallback under the current budget.
- [ ] Verify how Codex GitHub App and Copilot review are billed on a private repository; set review
  cadence to after CI stabilises rather than after every micro-push.
- [ ] Choose branch-current strictness, administrator enforcement, and the exact break-glass procedure
  after the Smart CI observation window is accepted.
- [ ] Confirm whether post-rehearsal operation is `hybrid` or hosted-only. Hosted-only requires an
  explicit final-Windows-path or budget re-ruling before the release tag.
- [ ] Keep the release/signing boundary protected and separate from ordinary CI.

## B. Measure and clean before changing visibility (CI-01 `#2325`, CI-09 `#2333`)

- [ ] `docs/ci/CI_BASELINE.md` is current for the accepted measurement window.
- [ ] Unexpired artifact bytes and cache bytes are recorded with retention classes.
- [ ] A fresh identity-bound cleanup dry run names the exact deletion set.
- [ ] Release, provenance, and required audit evidence are explicitly preserved.
- [ ] The maintainer authorizes only the current identity-bound set.
- [ ] The deletion ledger records requested, deleted, skipped, failed, and not-found objects.
- [ ] Post-cleanup artifacts and caches are remeasured below the private allowance under $0.

## C. Planner, required gate, and landed verification (CI-02 `#2326`, CI-03 `#2327`)

- [ ] Versioned policy and schemas are merged; planner fixtures and fail-closed cases are green.
- [ ] Shadow planner runs on every PR.
- [ ] The accepted window contains at least 20 usable merged PRs after the last relevant planner fix.
- [ ] Observation evidence shows zero false reds and complete recall for every enabled lane family.
- [ ] Receipts bind the exact head, merge tree, base, policy/config digest, and selected work.
- [ ] Authoritative landed evidence collection and workflow integration are complete.
- [ ] Normal merge takes the bounded path; direct push, missing/ambiguous evidence, moved base, or
  invalid receipt forces full escalation.
- [ ] Exact commands for post-privacy required-check registration and rollback are prepared.
- [ ] The maintainer's strict/admin/break-glass choice is recorded, but the gate remains unregistered
  until section K changes repository visibility to private.

## D. Event topology

- [ ] PR qualification tests the intended merge identity and superseded runs cancel safely.
- [ ] Drafts run only the light plan unless risk classification requires R3/R4.
- [ ] Main uses landed verification rather than an unconditional duplicate full suite.
- [ ] `merge_group` is either deliberately supported or inert and non-required.
- [ ] Auto-merge remains disabled until the final gate and branch-current policy are proven.

## E. Test right-sizing (CI-05 `#2329`, CI-07 `#2331`, CI-08 `#2332`)

- [ ] Linux semantic baseline and ownership mapping are complete.
- [ ] Windows compatibility contract covers platform-sensitive behaviour and historical Windows-only
  regressions.
- [ ] Weekly and release full-Windows coverage exists before ordinary PR Windows work is narrowed.
- [ ] Frontend semantic work runs once per ordinary PR on Linux; Windows retains only the approved
  platform contract outside full qualification.
- [ ] E2E is journey-aware; containers are risk-gated; manual full qualification remains available.
- [ ] Private-repository-incompatible jobs such as dependency-review/CodeQL are either moved to an
  approved hosted control lane or have an explicit recorded v0.3 posture.

## F. Runners (CI-04 `#2328`) - prove before, associate after

- [ ] Linux runner is an isolated VM; Windows runner is an isolated VM or dedicated low-privilege
  account. The release-secret runner boundary is separately identified.
- [ ] No host mounts, clipboard, SSH agent, browser profile, or personal credentials are present.
- [ ] Ordinary runners receive no repository, environment, or release secrets; one job runs per host;
  labels match policy.
- [ ] Hosted override and offline-runner behaviour are tested. Offline means pending/fail-closed,
  never false green.
- [ ] Workspace, temp, Docker, and cache cleanup are tested.
- [ ] VM reset/rebuild, detachment, token revocation, and incident response are documented and proven.
- [ ] All proof above is complete while runners remain unassociated with the repository.

## G. Supply chain and least privilege (CI-11 `#2335`)

- [x] Every external `uses:` is pinned to a full commit SHA and the pin inventory guard is green.
- [x] `sha_pinning_required: true` is enabled and read back from GitHub.
- [ ] Default workflow tokens are read-only; every elevated job is justified and scoped.
- [ ] `persist-credentials: false` is present wherever a checkout does not need push credentials.
- [ ] No `pull_request_target` path checks out or executes untrusted head code.
- [ ] CI-control changes have a hosted-only trust fixture.
- [ ] PR `#2838` is current-head qualified and passes the ADR-0066/J.3 maintainer gate.
- [ ] CodeQL is re-enabled in an approved lane or the current scanner posture and residual are recorded.

## H. Nightly and release qualification (CI-10 `#2334`)

- [ ] One coordinator owns nightly/quality and emits an honest no-change receipt.
- [ ] The weekly full sweep covers Linux, Windows, browsers, security, containers, and performance.
- [ ] Mutation remains manual unless ADR-0052 is explicitly amended.
- [ ] Final-head no-publish rehearsal uses CI-17's trusted Linux-only mode while self-hosted runners are
  offline. It schedules no private hosted Windows job.
- [ ] A draft-only publication hold prevents a real-tag workflow from publishing before post-tag
  evidence is accepted.
- [ ] The real release tag is created only after final-head evidence and the hold are proven.
- [ ] Exact-tag Linux/control qualification runs on the approved hosted lanes.
- [ ] Exact-tag Windows archive and Windows-specific qualification run on the approved isolated,
  secret-safe release runner after association.
- [ ] Linux/control and Windows evidence bind the same tag, commit, policy/config, checksums,
  provenance, and release contract.
- [ ] SBOM, provenance, digest verification, migration/upgrade, install, backup/restore, MCP proposal
  flow, and consumer smoke evidence are release-owned.

## I. Public-mode rehearsal before cutover (hosted-only, no runner associated)

- [ ] R0 docs-only PR.
- [ ] R2 ordinary backend/frontend PR.
- [ ] R3 migration/auth/executor/MCP PR.
- [ ] R4 workflow/policy PR through hosted control lanes.
- [ ] Cancelled and superseded PR behaviour.
- [ ] Normal merge to bounded landed verifier.
- [ ] Direct-push simulation to full escalation.
- [ ] Nightly no-change and weekly/deep run.
- [ ] Release dry run with no publication.
- [ ] Mirror dry run with no public mutation and byte-identity verification.
- [ ] CI-17 public-mode proof shows its trusted Linux-only selection cannot be set or bypassed by
  untrusted PR-head code.

## J. Public distribution preparation (CI-16 `#2439`)

- [ ] Create `Chris0Jeky/taskdeck-release` and disable Actions in the mirror.
- [ ] Create a fine-grained publishing credential scoped only to the mirror contents boundary. Keep it
  out of issues, logs, and repository files.
- [ ] The private-side mirror workflow uses an allowlisted snapshot, denies nested agent instruction
  files, fails closed on secret/token shapes, and verifies every copied asset.
- [ ] Public README, licensing, install, support, security, telemetry, launch-kit, and site links target
  their post-cutover homes.
- [ ] Set every release GHCR package explicitly public.
- [ ] Verify anonymous GHCR pull/read access before the repository visibility change.

## K. Manual private cutover (maintainer, exact order)

1. Pause merges and select a short frozen cutover window.
2. Capture current branch protection/rulesets, required checks, Actions permissions, package
   visibility, Pages, Releases, collaborators, forks, external links, and rollback values.
3. Review and authorize only the fresh identity-bound storage deletion set from section B; execute it
   and verify the post-cleanup measurement.
4. Verify the mirror repository, disabled mirror Actions, and narrowly scoped private-side credential.
5. Re-check that release GHCR packages are public and anonymously readable.
6. Confirm GitHub Pro and the settled $0 stop-on-limit posture.
7. **Change repository visibility to private.** Agents never perform this action.
8. Only now register `Smart CI / Required Gate`, retain the security contexts, and apply the recorded
   strict/admin/break-glass policy.
9. Re-check Actions permissions, fork approval, Dependabot, Pages, packages, Releases, mirror links,
   collaborators, forks, and anonymous GHCR access.
10. Keep every self-hosted runner unassociated. Run private-mode R0/R2/R4 PRs, a normal merge, a
    nightly dispatch, and a no-publish release rehearsal through CI-17's trusted Linux-only mode.
    Abort if any private hosted Windows job is scheduled, the mode is absent/bypassable, or expected
    Linux/control/security evidence is missing.
11. Only after step 10 succeeds, associate already-proven runners if hybrid mode remains desired.
    Verify labels, read-only token posture, no secret exposure, selected self-hosted workload evidence,
    and full Windows evidence on the approved runner classes.
12. If hybrid mode is not retained, record hosted-only operation and explicitly re-rule the final
    Windows build path or budget before creating the release tag.
13. Record the complete evidence ledger on CI-00 `#2324`; resume merges only after every assertion is
    reconciled.

## L. Final tag, private Release, mirror, and announcement

1. Freeze one exact `main` commit and stop feature merges.
2. Update release notes, upgrade notes, limitations, checksums/provenance expectations, and public
   links.
3. Run final-head checks and the CI-17 Linux-only no-publish rehearsal with self-hosted runners
   offline. Do not create the tag if any private hosted Windows job is scheduled.
4. Activate and verify the draft-only publication hold.
5. Create the real `v0.3.0` tag on the proven commit.
6. Qualify that exact tag on hosted Linux/control lanes and the secret-safe isolated Windows release
   runner.
7. Reconcile both runner-class evidence sets against the same immutable identity.
8. Consumer-smoke the produced archive/container and claimed install, update, MCP, and backup paths.
9. Publish the private `v0.3.0` Release with the exact qualified assets, checksums, provenance, and
   body.
10. Let the private-side workflow stage and verify the mirror source and byte-identical assets.
11. Publish the mirror commit, tag, and public Release only after staged verification succeeds.
12. Verify source identity, downloads, checksums, provenance, links, and GHCR access anonymously.
13. Announce only after all public identities and access checks match.

## M. Rollback and abort

- Previous workflow/config files remain reachable by immutable commit; all setting values are
  captured before mutation.
- The hosted control override remains available; associated runners can be detached and revoked in
  one step.
- Do not flip public merely to regain free minutes. Diagnose against captured state and use the
  documented override or rollback path.
- A failed, ambiguous, missing, or bypassable assertion stops the cutover.
- Do not create the release tag while the publication hold, Linux-only rehearsal, runner boundary,
  public GHCR continuity, mirror handoff, storage posture, or exact-head evidence is unproved.
