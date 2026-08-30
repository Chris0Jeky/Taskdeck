# Taskdeck Private-Repository CI Cutover Checklist

## A. Decisions

- [ ] Confirm GitHub account plan and current Education/Pro status.
- [ ] Set a monthly GitHub Actions spend ceiling and alerts.
- [ ] Verify how the repository's automated code-review integrations consume private-repository Actions minutes/AI credits; do not assume their public-repository cost model carries over.
- [ ] Decide repository ownership: remain personal with the weaker owner-only/ephemeral runner fallback, or transfer to an organization for runner-group selected-workflow restriction and organization required-workflow rules.
- [ ] Select `hosted`, `hybrid`, or `self-hosted-heavy` as the initial mode.
- [ ] Decide whether the laptop is a real Windows runner or hosted Windows remains the initial fallback.
- [ ] Confirm the release/signing runner boundary separately from ordinary CI.
- [ ] Confirm whether public documentation/demo/website should remain in a separate public repository.

## B. Measure before changing

- [ ] Export 30-day workflow run/job data.
- [ ] Record hosted runner-minutes by operating system.
- [ ] Record P50/P95 PR critical path and aggregate runner-minutes.
- [ ] Record artifact and cache storage.
- [ ] Record rerun/flake rate.
- [ ] Record lane failure yield and top slow tests.
- [ ] Record duplicate PR/main/merge-group exact-SHA work.

## C. Planner and gate

- [ ] Merge versioned CI policy/schema.
- [ ] Add deterministic planner with fixture tests.
- [ ] Run planner in shadow mode without skipping existing jobs.
- [ ] Collect at least 20–50 representative plans or an equivalent risk corpus.
- [ ] Confirm every failure caught by full CI would have selected the relevant lane.
- [ ] Add fail-closed unknown-path and planner-error behavior.
- [ ] Add `Smart CI / Required Gate` with exact-SHA and policy-version checks.
- [ ] Prefer an organization ruleset-required workflow sourced outside the Taskdeck PR write boundary; otherwise document the weaker same-repository check-name boundary.
- [ ] Register the stable gate in branch protection/ruleset.
- [ ] Require branch to be current before merge.
- [ ] Apply protections to administrators or document a break-glass procedure.

## D. Event topology

- [ ] PR workflow tests the merge ref and carries substantive qualification.
- [ ] Superseded PR runs cancel.
- [ ] Drafts run only the authorized light plan unless high risk.
- [ ] Full `push main` duplication is replaced by landed verifier.
- [ ] Direct/bypass push with no valid qualification receipt escalates to full CI.
- [ ] `merge_group` is not required for private-personal operation.
- [ ] Auto-merge is enabled only after required checks and current-branch policy are proven.

## E. Test right-sizing

- [ ] Linux semantic baseline defined.
- [ ] Windows compatibility contract defined and passing.
- [ ] Full Windows suite retained weekly/release during the evidence period.
- [ ] API suite partitioned into stable ownership shards.
- [ ] MCP/process test fixed waits and teardown measured/repaired.
- [ ] Frontend full semantic lane runs once per PR.
- [ ] Windows frontend lane is narrowed to launcher/platform contracts.
- [ ] E2E selection maps to changed journeys.
- [ ] Container build selection maps to container/runtime risk.
- [ ] Full manual qualification remains available.

## F. Runner setup

- [ ] Linux runner is an isolated VM, not ordinary WSL/host session.
- [ ] Windows runner is isolated or uses a dedicated low-privilege account/VM.
- [ ] No host drive mounts, clipboard, SSH agent, browser profile, or personal credentials.
- [ ] No repository/environment/release secrets on ordinary self-hosted runners.
- [ ] One concurrent heavy job per physical host.
- [ ] Runner labels match policy exactly.
- [ ] If organization-owned, runner group accepts only the selected trusted reusable workflow at `refs/heads/main`.
- [ ] The trusted workflow rejects stale/mismatched PR/head/merge SHAs and control-path changes before checkout.
- [ ] Hosted override is documented and tested.
- [ ] Offline self-hosted job behavior is tested.
- [ ] Workspace/temp/Docker/cache cleanup is tested.
- [ ] VM reset/rebuild process is documented.
- [ ] Runner incident/revocation process is documented.

## G. Supply chain

- [ ] Inventory every external `uses:` reference.
- [ ] Pin actions to full commit SHAs with version comments.
- [ ] Enable full-SHA action policy after migration.
- [ ] Set default workflow token permissions to read-only.
- [ ] Review every elevated permission job.
- [ ] Ensure CI-control changes force hosted execution.
- [ ] Ensure no `pull_request_target` path executes untrusted head code.
- [ ] Validate artifact provenance before privileged consumption.
- [ ] Keep signing/cloud keys in protected release environments only.

## H. Nightly and release

- [ ] Merge nightly/quality ownership into one coordinator policy.
- [ ] Nightly skips heavy work when `main` has no relevant change.
- [ ] Weekly full Linux/Windows/browser/security/container sweep exists.
- [ ] Mutation remains manual unless a new evidence-based decision changes it.
- [ ] Release rebuilds from exact tag in clean context.
- [ ] SBOM/provenance and artifact verification are release-owned.
- [ ] Release dry-run succeeds with ordinary self-hosted runners offline.

## I. Visibility-change rehearsal

Create and prove these cases while still public, without attaching a public-untrusted runner:

- [ ] R0 docs-only PR.
- [ ] R2 ordinary backend or frontend PR.
- [ ] R3 migration/auth/executor/MCP PR.
- [ ] R4 workflow/policy PR, hosted-only.
- [ ] self-hosted runner offline + hosted override.
- [ ] cancelled/superseded PR.
- [ ] normal PR merge → tiny main verifier.
- [ ] direct push simulation → full escalation.
- [ ] nightly no-change skip.
- [ ] weekly/deep full run.
- [ ] release dry-run.

## J. Manual private cutover

- [ ] Pause merges briefly.
- [ ] Capture current required-check and Actions settings evidence.
- [ ] Verify public assets that must remain public have a separate home.
- [ ] Change repository visibility manually.
- [ ] Re-check Actions permissions, fork policy, Dependabot behavior, Pages/package/release visibility, and runner association.
- [ ] Re-apply/verify branch protection or ruleset.
- [ ] Run R0, R2, R4, main-verifier, nightly-dispatch, and release-dry-run tests in private mode.
- [ ] Verify hosted usage and budget accounting.
- [ ] Verify self-hosted jobs consume no hosted minutes and expose no secrets.
- [ ] Resume merges.

## K. Rollback

- [ ] Keep previous workflow files available by tag/commit.
- [ ] Maintain a manual hosted full-qualification workflow.
- [ ] Document how to force all jobs hosted.
- [ ] Document how to detach/revoke self-hosted runners.
- [ ] Document how to restore previous branch-protection contexts.
- [ ] Do not change visibility back merely to obtain free CI before understanding the failure; use the hosted override or rollback workflow first.
