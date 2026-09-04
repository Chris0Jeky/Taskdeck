# Private-repository cutover checklist (personal GitHub Pro account)

Last Updated: 2026-09-03 · Decision: ADR-0066 · Executable copy: CI-13 `#2337` (record evidence there) · Human actions: `OUTSTANDING_TASKS.md` §J

The repository goes **private for the v0.3.0 release**. Everything below is proven while the
repository is still public and hosted-only; **no self-hosted runner is attached before the
visibility change**. Agents prepare, rehearse and verify; the maintainer performs the settings,
billing, visibility and runner-registration actions.

## A. Decisions (maintainer)

- [x] Confirm the GitHub plan is Pro and record the allowance in force (3,000 minutes/month, 1 GB storage as of 2026-08-30). *Confirmed by the maintainer 2026-09-03 (SC-3 re-ruling): the account is GitHub Pro and the included 3,000 minutes/month are the CI budget.*
- [ ] Verify at step J.7 that the Actions spending limit (Billing → Spending limits) reads **$0** and record it on `#2337`. *This box replaced "set a spend ceiling and alert" on 2026-09-03 when SC-3 was re-ruled (superseding the $10/month packet value): **deferred — no paid overage, no alert threshold.** Hosted Actions spend only the included 3,000 minutes/month and only on **Linux** jobs; Windows (x2) and any macOS (x10) legs run locally (the laptop runner via CI-04, or agent-run proving checks) or carry a local fallback, never hosted overage. GitHub's default $0 spending limit is the effective hard ceiling, which is what the J.7 read-back confirms.*
- [ ] Verify how the Codex GitHub App and Copilot code review are billed on a private repository (Copilot review consumes Actions minutes; do not assume the public-repo model); set the review cadence to after-CI-stabilises.
- [ ] Ownership: stay personal (ADR-0066 ruling 1) — the organization boundary is CI-14 with its triggers. *Confirmed 2026-09-03 with the private-Pro approval-boundary amendment (ADR-0066).*
- [ ] Initial execution mode: `hosted` (ruling 4); `hybrid` only after CI-04 is registered and proven.
- [x] Laptop as a real Windows runner, or hosted Windows as the initial fallback. *Ruled 2026-09-03 (SC-3 re-ruling): the laptop is the Windows runner and the fallback is local execution, not hosted Windows. Hosted Windows legs run only while the repository is still public and free; from the visibility change onward Windows evidence is local only — the laptop runner once CI-04 `#2328` registers it (SC-7), agent-run proving checks on this box until then. CI-07 `#2331` sizes the Windows contract for local execution, not for a hosted allowance.*
- [ ] Release/signing boundary stays `#2149`'s protected context, separate from ordinary CI.
- [ ] Public documentation/demo/site: GitHub Pages (`pages-frontend.yml`) keeps publishing from a private repo on Pro and the site stays public — keep, move, or retire; the launch-kit links (`#2242`) and any `awesome-selfhosted` reference get the same decision. *Ruled 2026-09-03 (SC-8): **private development repository + public release/source mirror** — Pages keeps publishing; Releases, checksums/provenance and the GPL source publish through the mirror (CI-16 `#2439`); launch-kit and `awesome-selfhosted` wording point at the mirror.*

## B. Measure before changing (CI-01 `#2325`, CI-09 `#2333`)

- [ ] `docs/ci/CI_BASELINE.md` committed with the 30-day window (runs, critical path, allowance minutes per run, storage).
- [ ] Unexpired artifact bytes and cache bytes recorded; retention classes applied; the one-time cleanup dry-run prepared (deletion itself is a maintainer-authorized action).

## C. Planner and gate (CI-02 `#2326`, CI-03 `#2327`)

- [ ] Versioned policy + schemas merged; planner fixtures green; fail-closed behaviour proven (unmapped path, planner error, control-path change).
- [ ] Shadow planner running on every PR; recall report over ≥20 PRs shows the plan would have selected every lane that actually failed.
- [ ] `Smart CI / Required Gate` in observation mode with zero false reds over ≥20 PRs; receipts bound to the exact SHA, merge tree and policy digest.
- [ ] Landed verifier proven: normal merge → bounded path; direct-push simulation → full escalation; base moved → re-qualification.
- [ ] **Maintainer:** register the gate as required; keep the three security contexts; decide on the observation evidence — not in advance (SC-4 ruling 2026-09-03) — whether to set `strict: true` and `enforce_admins` or to document break-glass. (CI-03 supplies the exact `gh api` commands.)

## D. Event topology (CI-03)

- [ ] PR workflow tests the merge ref and carries the substantive qualification; superseded runs cancel.
- [ ] Drafts run only the light plan unless R3/R4.
- [ ] Full `push: main` re-qualification replaced by the landed verifier.
- [ ] `merge_group` is not required for anything (inert trigger is fine).
- [ ] Auto-merge enabled only after the gate and branch-current are proven (maintainer setting).

## E. Test right-sizing (CI-05 `#2329`, CI-07 `#2331`, CI-08 `#2332`)

- [ ] Linux semantic baseline defined in the ownership map.
- [ ] Windows compatibility contract defined and green; the full Windows suite retained weekly/release during the evidence period.
- [ ] Frontend lint/type/build/coverage once per PR on Linux; the Windows leg narrowed to launcher/platform.
- [ ] E2E selection by journey; container build by container/runtime/deploy risk; a manual full hosted qualification remains available.
- [ ] `ci-extended`'s `dependency-review` job removed or gated — `actions/dependency-review-action` and CodeQL need GitHub Advanced Security on a private repository; the in-repo dependency signals and Semgrep are the gates (CI-11).

## F. Runners (CI-04 `#2328`) — prepared before, registered after

- [ ] Linux runner is an isolated VM; Windows runner is an isolated VM or a dedicated low-privilege account.
- [ ] No host mounts, clipboard, SSH agent, browser profile, personal credentials; no repository/environment/release secrets on ordinary runners; one job per host; labels match the policy.
- [ ] Hosted override (`ci:hosted`, `CI_EXECUTION_MODE=hosted`) documented and tested; offline-runner behaviour tested (gate pending, never green).
- [ ] Workspace/temp/Docker/cache cleanup tested; VM reset/rebuild and incident revocation documented.

## G. Supply chain (CI-11 `#2335`)

- [ ] Every external `uses:` pinned to a full commit SHA with a version comment; the inventory guard is green.
- [ ] Default workflow token read-only (already); every elevated-permission job reviewed; `persist-credentials: false` where no push is needed.
- [ ] No `pull_request_target` path checks out or executes head code (contract test).
- [ ] CI-control changes run hosted-only (fixture).
- [ ] **Maintainer:** flip `sha_pinning_required` on after the migration.

## H. Nightly and release (CI-10 `#2334` — v0.3 since 2026-09-03: Q1 on `#2337` ruled **A**, the whole section is a cutover prerequisite and `#2334` moved from v0.4)

- [ ] One coordinator owns nightly/quality; a no-change night exits through the honest green receipt; the weekly full sweep exists.
- [ ] Mutation remains manual (ADR-0052).
- [ ] Release rebuilds from the exact tag in a clean hosted context; SBOM/provenance and digest verification release-owned; a release dry-run succeeds with ordinary self-hosted runners offline.

## I. Rehearsal while still public (hosted-only, no runner attached)

- [ ] R0 docs-only PR · R2 ordinary backend/frontend PR · R3 migration/auth/executor/MCP PR · R4 workflow/policy PR (hosted-only) · cancelled/superseded PR · normal merge → tiny main verifier · direct-push simulation → full escalation · nightly no-change skip · weekly/deep full run · release dry-run (no publish).

## J. Manual private cutover (maintainer, in order)

1. Pause merges briefly.
2. Capture current required-check and Actions settings (`gh api repos/Chris0Jeky/Taskdeck/branches/main/protection`, `…/actions/permissions`, `…/actions/permissions/workflow`).
3. Register the gate and apply the SC-4 evidence-based `strict` / `enforce_admins` decision (C); flip `sha_pinning_required` (G).
4. Verify public assets that must remain public have a separate home (A).
5. **Change repository visibility to private.**
6. Re-check Actions permissions, fork-PR approval policy, Dependabot, Pages/package/release visibility, runner association, collaborator list.
7. Run R0, R2, R4, a merge (main verifier), a nightly dispatch, and a no-publish release rehearsal in private mode; verify hosted-minute accounting against the 3,000-minute Linux-only allowance; read Billing → Spending limits back, confirm it shows **$0**, and record it on `#2337` (SC-3, checklist A).
8. Register the isolated runners (F); set `CI_EXECUTION_MODE=hybrid`; verify self-hosted jobs consume no hosted minutes and expose no secrets.
9. Resume merges; record post-cutover evidence on CI-00 `#2324`.

## K. Rollback

- Previous workflow files remain reachable by tag/commit; the manual hosted full-qualification workflow stays available; runners can be detached in one step; the pre-cutover branch-protection contexts are in the step-2 capture.
- Do not flip visibility back merely to regain free minutes before understanding a failure — use the hosted override or the rollback workflow first.
