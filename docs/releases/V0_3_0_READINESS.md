# v0.3.0 release readiness

Last Updated: 2026-09-21

This is the standing operational view of what separates current `main` from the final `v0.3.0`
release. Live GitHub outranks every count, PR state, branch reference, and CI result in this file.

For the current programme rationale and engineering themes, read the
[2026-09-21 repository direction and v0.3 programme brief](../analysis/2026-09-21-repository-direction-and-v0.3-programme.md).
The [2026-09-17 release assessment](../analysis/2026-09-17-v0.3-release-assessment.md) remains a
historical decision snapshot. The executable human sequence lives in the
[private-repository cutover checklist](../ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md).

## 1. Current verdict

**No-go for the final tag.**

Measured on 2026-09-21 against live GitHub and `main`
`f001dd92149dd3dc807f48691772f2ac2cd3f1f5`:

| Signal | Current state |
| --- | --- |
| v0.3 milestone | 104 closed, 30 open, 134 total: 77.6% closed |
| Open issue split | 16 `ci`, 5 `dogfooding`, 9 other |
| Priority I | 10 open v0.3 issues |
| Distribution | `v0.3.0-rc.1` exists with Windows archive, checksum, and provenance |
| Repository visibility | Public |
| Required `main` contexts | Dependency Security, Semgrep SAST, and Gitleaks Secret Scan |
| Stable Smart CI gate | Not registered in branch protection |
| Final `v0.3.0` tag | Not created |

The RC proves that Taskdeck can be packaged. It does not prove the final private-development,
least-privilege, exact-tag, mirror, runner, storage, or release-publication contract.

Issue closure percentage is a throughput measure, not a release-readiness percentage. One unresolved
trust gate can block the tag; an issue with an explicit residual ruling can remain open without doing
so.

## 2. Formal release gates

| Gate | State | Required outcome |
| --- | --- | --- |
| Exact final candidate green | Pending | Freeze one head, prove the no-publish path, create `v0.3.0`, and qualify that exact tag |
| Milestone closed or explicitly ruled | Not met | Close, move, or explicitly retain every open issue; preserve the recorded `#2315` residual unless re-ruled |
| Launch material | Drafted | Replace provisional/private URLs with verified public mirror and GHCR destinations |
| Frozen `main` head green | Must be re-proved | Historical or superseded green runs do not qualify the eventual release head |
| Private cutover and gate enforcement | Not met | Complete preconditions, flip private, register the gate, pass CI-17, then associate runners |
| Public distribution continuity | Not met | Prove public GHCR and the staged private-Release-to-public-mirror handoff |

## 3. Critical workstreams and release sequence

The numbered subsections group ownership and acceptance workstreams; they are not a linear priority
list. The controlling sequence is:

1. finish admitted exact-identity correctness stacks;
2. finish Smart CI proof and authoritative landed-evidence integration;
3. implement and prove CI-17;
4. close Windows, least-privilege, storage, nightly, runner, and mirror/GHCR prerequisites;
5. reconcile every milestone issue on the resulting evidence, while applying obvious close-on-
   evidence updates continuously;
6. execute the human cutover;
7. freeze, tag, qualify, publish privately, mirror publicly, verify anonymously, then announce.

### 3.1 Release-scope reconciliation

Owners: milestone 4, `#2235`, and the maintainer.

Every open issue needs one current disposition:

- **SHIP**: required acceptance remains in v0.3;
- **CLOSE ON EVIDENCE**: delivered acceptance is reconciled and any real residual is split;
- **EXPLICIT RESIDUAL**: remains open under a recorded non-blocking ruling;
- **DEFER**: moved with a reason and destination milestone;
- **HUMAN GATE**: agent preparation is complete and an explicit maintainer action remains.

Do not infer closure from an old checkbox, title, or merged sibling PR. Do not bulk-close the
milestone.

### 3.2 Smart CI planner, receipts, and landed verification

Owners: `#2326`, `#2327`, `#2508`, `#3227`, and tracker `#2324`.

Landed foundations and current implementation chain:

1. PR `#3156` merged on 2026-09-19 and delivered the merge-base receipt residuals; `#2508` remains
   open for issue reconciliation rather than implementation qualification.
2. PR `#3167` merged on 2026-09-18 and delivered the pure landed-verifier decision core; `#3227`
   records the planner-only shadow-receipt gap.
3. Open parent PR `#3295`: enforce-mode, selected-lane, and repository-bound receipt proof.
4. Open child PR `#3296`: stacked on `#3295`; CLI and output-denial hardening.
5. Still required: authoritative PR association, repository-scoped collection, artifact retrieval,
   workflow integration, and the bounded-versus-full main routing contract.

Acceptance remains:

- at least 20 usable merged-PR observations after the last relevant planner fix;
- zero false reds in the accepted window;
- full recall for every lane family proposed for selection;
- bounded normal-merge verification;
- full hosted escalation for direct/bypass push, missing/expired/ambiguous evidence, moved base,
  invalid receipt, collector failure, or policy mismatch.

Do not register `Smart CI / Required Gate` while the development repository is public.

### 3.3 CI-17 private-cutover rehearsal

Owner: `#3170`; prerequisite work includes PR `#3297`; human execution owner is `#2337`.

PR `#3297` inventories runner jobs and reusable-workflow call edges. It is a non-activating
prerequisite, not the rehearsal mechanism or security boundary.

The final CI-17 implementation must:

- derive rehearsal mode from trusted protected-base code;
- cover required, called, and reusable workflows transitively;
- suppress all private hosted Windows work before runner association;
- preserve non-vacuous Linux, security, governance, receipt, and control-plane evidence;
- detect unsupported, missing, ambiguous, or drifted coverage and fail closed;
- emit an exact-identity receipt;
- prove R0, R2, R4, normal merge, nightly, and no-publish release scenarios.

The cutover stops if any hosted Windows job is scheduled, any expected evidence is absent, or the
mode can be selected or weakened by untrusted head code.

### 3.4 Windows qualification reliability

Owners: `#2378` and `#2588`; delivered timeout evidence: closed `#3158` and merged PR `#3162`.

- PR `#3162` merged on 2026-09-19 with the calibrated outer timeout, per-test hang detection,
  process-tree termination, mini-dump support, and partial-results upload.
- Post-merge 45-minute ceiling occurrences under runner contention remain evidence to classify, not
  proof that the timeout contract is missing.
- Existing launcher/runner timing issues close only on causal repair, a proven superseding contract,
  or an explicit residual decision. Same-head reruns cannot erase the original failure.

### 3.5 Least privilege and hosted control proof

Owner: `#2335`; delivered implementation: merged PR `#2838`.

`sha_pinning_required: true` is already enabled. PR `#2838` merged on 2026-09-19 with checkout
credential-persistence coverage and Pages permission scoping. Remaining work is to reconcile the
issue's acceptance, retain hosted-only control-path proof, record the maintainer's control-plane
ruling, and choose an explicit CodeQL posture.

### 3.6 Storage under the settled `$0` posture

Owner: `#2333`.

1. Refresh the identity-bound inventory and cleanup dry run.
2. Preserve release, provenance, and required audit evidence.
3. Obtain authorization for the exact current deletion set.
4. Execute only that set.
5. Record requested, deleted, skipped, failed, and not-found outcomes.
6. Remeasure unexpired artifacts and caches.
7. Prove the private posture is sustainable without paid overage.

### 3.7 Nightly, weekly, and exact-tag qualification

Owner: `#2334`.

- Accept the change-driven nightly observation window.
- Prove the weekly full Linux, Windows, browser, security, container, and performance sweep.
- Keep mutation manual unless ADR-0052 changes.
- Prove the frozen head through the CI-17 Linux-only no-publish path.
- Prove a draft-only publication hold before tag creation.
- Create the real tag, then qualify hosted Linux/control work and isolated Windows release work
  against one immutable identity.

### 3.8 Public mirror and package continuity

Owner: `#2439`.

The accepted mirror is `Chris0Jeky/taskdeck-release` with mirror Actions disabled. Required work:

- create the repository and narrowly scoped credential;
- implement the private-side fail-closed exporter/publisher;
- exclude private control-plane and agent-instruction material;
- make release GHCR packages explicitly public before privacy;
- verify anonymous GHCR access before and after the flip;
- publish the private Release first;
- stage and re-download/verify the mirror source and byte-identical assets before making them public;
- move public install, support, security, licensing, telemetry, and launch links to verified public
  destinations.

### 3.9 Runner isolation and association

Owner: `#2328`.

Prove isolated VMs, no host mounts or personal credentials, one job per host, read-only ordinary
tokens, cleanup, offline behavior, hosted override, reset, detachment, revocation, and incident
response before GitHub association.

PR `#3261` is a stale recovery branch with a blocking nested reparse-point gap. Port a corrected,
minimal current-main slice; do not merge the branch wholesale.

### 3.10 Human cutover and release

Owner: `#2337` and the maintainer.

Canonical order:

1. pause merges and capture settings plus rollback values;
2. complete sections A-J of the cutover checklist;
3. make GHCR public and verify anonymously;
4. change the development repository to private;
5. register `Smart CI / Required Gate` and retained security contexts;
6. apply the evidence-based strict/admin/break-glass policy;
7. run the CI-17 Linux-only rehearsal with all self-hosted runners unassociated;
8. associate only already-proven runners and prove the separate Windows/self-hosted contract;
9. freeze one final head and repeat the no-publish rehearsal;
10. activate the publication hold and create `v0.3.0`;
11. qualify the exact tag on approved hosted Linux/control and isolated Windows release lanes;
12. publish the private Release;
13. stage, verify, and publish the public mirror;
14. verify source, assets, checksums, provenance, links, GHCR, and downloads anonymously;
15. announce only after every public identity and access check matches.

## 4. Open milestone routing

This is a routing aid, not a substitute for live issue bodies or final maintainer dispositions.

### CI and release-control issues

`#2324`, `#2326`, `#2327`, `#2328`, `#2329`, `#2331`, `#2332`, `#2333`, `#2334`, `#2335`,
`#2337`, `#2378`, `#2439`, `#2508`, `#2588`, `#3170`.

### Product, trust, acceptance, and residual issues

`#1131`, `#1307`, `#1309`, `#1772`, `#1940`, `#1949`, `#1999`, `#2004`, `#2009`, `#2214`,
`#2315`, `#2499`.

### Final documentation and launch closeout

`#2235`, `#2391`.

`#2315` retains its explicit non-blocking residual ruling. Every other issue must close, move, or
receive its own recorded ruling before final release.

## 5. Human decisions and actions

Do not infer completion of any item in this section.

- Final per-issue milestone disposition.
- CLI local-admin versus claims-first posture for `#1131`.
- MCP runtime hash-approval posture for `#1309`.
- Branch-current strictness, administrator enforcement, and break-glass after Smart CI evidence.
- CodeQL posture for `#2335`.
- Remaining private-instance choices and execution for `#1772`.
- Exact storage deletion authorization for `#2333`.
- Mirror repository and credential creation for `#2439`.
- Repository privacy, required-check, branch-policy, and runner-association actions for `#2337`.
- Frozen release commit, final tag, private Release, mirror publication, and announcement.

## 6. Definition of done

`v0.3.0` is ready only when:

- every milestone issue is closed, moved, or covered by an explicit residual ruling;
- no release-critical PR is parked behind an unrecorded decision;
- the exact frozen head is green under the final required-check configuration;
- landed verification and full escalation are proven with authoritative evidence;
- Smart CI observation and recall thresholds are accepted;
- artifact/cache posture fits the settled private budget;
- runner isolation and recovery are proven before association;
- CI-17 schedules zero private hosted Windows work and preserves all expected evidence;
- GHCR is anonymously accessible before and after privacy;
- the real tag is rebuilt and qualified after it exists;
- hosted Linux/control and isolated-Windows evidence bind one tag, commit, policy, checksums,
  provenance, and release contract;
- the release workflow cannot publish before the hold is released;
- the private Release contains the exact qualified body and assets;
- the public mirror republishes corresponding source and byte-identical assets through staged
  verification;
- anonymous verification passes;
- archive, container, MCP proposal flow, install, update, upgrade, and supported backup claims are
  consumer-smoked;
- documentation and known limitations match shipped reality;
- announcement happens last.

## 7. Keeping this current

When release state changes:

1. Re-read the live milestone, open issue bodies, open PR bases, branch protection, releases, and
   exact-head checks.
2. Update this file only for programme state, gates, or sequencing.
3. Update `docs/STATUS.md` only for merged shipped behavior.
4. Update `docs/IMPLEMENTATION_MASTERPLAN.md` for durable delivery history and execution changes.
5. Update the cutover checklist and `#2337` together if the human sequence changes.
6. Preserve dated assessments as historical evidence rather than rewriting their snapshots.
7. Record destructive or settings actions only after exact read-back evidence exists.
