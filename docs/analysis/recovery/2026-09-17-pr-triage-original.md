# Taskdeck pull-request recovery report

**Repository:** `Chris0Jeky/Taskdeck`  
**Observed main:** `b32ec2dbe5e5e7100e1dedd459cc542beca782dd`  
**Open pull requests reviewed:** 15  
**Repository mutations made:** none. The connected GitHub surface exposed read/inspection operations but no branch, comment, workflow-rerun, review-resolution, or merge mutation operation; the local runtime also had no authenticated GitHub checkout.

## Immediate order of work

1. Finish **#3128**'s exact-head required matrix. If it is terminal green, it is the nearest clean merge candidate.
2. Complete **#3132**'s visual-baseline bootstrap: inspect the fresh 22-image Ubuntu artifact, promote all reviewed images together, rerun Visual Regression, and obtain a fresh review. The supplied five-image contact sheet is not the required artifact.
3. Apply the prepared **#3134** patch, resolve its two review findings, and run fresh exact-head CI.
4. Apply the prepared **#3098** documentation patch, resolve its five current review findings, and rerun docs checks/required CI.
5. Combine or stack **#3131** with **#3111**, rebase onto current main, and run one fresh control-plane qualification. Do not merge #3111 while its invalid numeric-prerelease review finding remains unfixed.
6. Rebase **#3102** after #3128; retain #3128's path/case corrections while replaying the parser/masking changes.
7. After #3132 lands, rebase and rerun **#3100**, **#3090**, and **#3103**. Their old Extended failures include inherited k6 and visual-baseline debt; #3090 and #3103 also require WebKit reproduction on the refreshed head.
8. Rebase and fully requalify **#2838** against the final workflow inventory.
9. Reconcile conflicts on the explicit human-gated PRs **#3130**, **#2931**, and **#3113**, then return them to the maintainer for the required human decision/review.
10. Close **#3067** after #3134 replaces it.

## Pull-request classification

| PR | Current classification | Evidence and required next action |
|---|---|---|
| #3128 | Closest merge candidate | Mergeable; Smart CI and Extended CI green; all review threads resolved; required CI was still running. Merge only after that exact-head run is terminal green. Land before #3102. |
| #3132 | Human visual-acceptance gate | Smart CI green; required and Extended runs still in progress. Branch intentionally removes all baselines to generate 22 images. Inspect all 22, commit the complete reviewed set, rerun, and obtain fresh review. Do not promote the older five-image contact sheet. |
| #3134 | Fix prepared; not merge-ready | Mergeable; Smart/Extended green; required run cancelled. Two valid review findings remain: YAML implicit-scalar resolver mismatch and stale control-character assertions. Apply `Taskdeck-PR-3134-review-fixes.patch`, resolve threads, and rerun exact-head CI. |
| #3098 | Fix prepared; not merge-ready | Mergeable and required/Smart CI green, but five unresolved documentation findings remain. Apply `Taskdeck-PR-3098-review-fixes.patch`, resolve threads, run docs governance/link checks, and rerun required CI. |
| #3131 | Green but incorrectly isolated from parent | All three CI workflows green and its review thread is resolved. Its stricter SemVer grammar is the fix required by #3111. Prefer folding/stacking it into #3111 and requalifying once, rather than merging #3111 with a known review blocker. |
| #3111 | Green CI; blocking review remains | All three CI workflows green, but the recorded review demonstrates that `v1.2.3-01` reaches container metadata. Incorporate #3131, rebase, rerun, and obtain a fresh review. |
| #3102 | Conflicted dependency | Currently unmergeable. It touches the same link checker as #3128. Land #3128 first, then rebase and preserve its path/case behavior while replaying parser/masking hardening. |
| #3067 | Superseded and contaminated | Currently unmergeable, 138 commits and 117 changed files. #3134 is the clean replacement. Close after #3134 is qualified and merged. |
| #3100 | Rebase after shared CI repairs | Required and Smart CI green; Extended failed only in inherited k6/visual lanes. Review thread is resolved with warning-lifecycle follow-up tracked separately. Rebase after #3132; restore draft status if repository policy still follows the PR body's stated gate; rerun focused telemetry tests plus full CI. |
| #3090 | Rebase and reproduce | Required and Smart CI green; Extended failed in inherited k6/visual lanes plus WebKit. Review threads are resolved, with colocated-spec coverage explicitly left as a nonblocking residual. Rebase after #3132 and reproduce WebKit before changing product code. |
| #3103 | Rebase and reproduce | Required and Smart CI green; no review threads. Extended failed in inherited k6/visual lanes plus WebKit. Rebase after #3132 and rerun; do not mix unrelated visual/browser repairs into the formatter change unless they reproduce. |
| #2838 | Sound design, stale inventory proof | Mergeable; all three old CI workflows green; no review threads. Security review explicitly requires a current-main refresh because the contract inventories 29 workflow files. Rebase and rerun credential persistence, permission-shape, action-pin, governance, and required suites. |
| #3130 | Conflicted, human-owned | Currently unmergeable; 24 files and 1,564 additions. Body explicitly says human review and human merge. Reconcile current main, rerun frontend/Pages/static-demo proof, then return to the maintainer. |
| #2931 | Conflicted, parked human decision | Currently unmergeable. Control-plane PR is explicitly parked until ADR-0066/J.3(b) is answered; no agent merge. Reconcile only after that decision. |
| #3113 | Conflicted, explicit maintainer gate | Currently unmergeable. Prior exact-head evidence was green, but main has advanced materially. Reconcile and rerun Bash/PowerShell/worktree contract matrices, then require maintainer review. |

## Prepared patch: #3134

File: `Taskdeck-PR-3134-review-fixes.patch`

Scope:

- aligns implicit null/boolean/integer/float/timestamp matching with the loader's actual resolver boundaries;
- adds the reviewer's under-match and over-match counterexamples;
- separates Unicode/control-character diagnostics from ordinary whitespace-policy assertions.

Fresh focused verification:

- current matcher regression probe: **2 failures**, reproducing `0xF__F` under-match and `0XFF` over-match;
- proposed matcher regression probe: **3/3 passed**;
- deterministic resolver parity: **curated cases plus 250,000 generated scalars passed**;
- patch parser/application against the reconstructed changed fragments: passed;
- full repository test suite: **not claimed**. The connector-fetched local fixture was truncated and was not an executable full checkout; hosted exact-head CI remains required.

Suggested branch sequence:

```bash
git switch fix/3006-frontmatter-scalars-clean
git apply --check /path/to/Taskdeck-PR-3134-review-fixes.patch
git apply /path/to/Taskdeck-PR-3134-review-fixes.patch
node --test scripts/check-docs-governance.test.mjs scripts/check-docs-governance.hardening.test.mjs
node --check scripts/check-docs-governance.mjs
git diff --check
```

## Prepared patch: #3098

File: `Taskdeck-PR-3098-review-fixes.patch`

Scope:

- makes the recurring daily/weekly backup schedule a pre-invite gate;
- adds an authenticated same-origin egress-disclosure procedure;
- selects the local board before Windows capture/triage;
- adds a sole-owner preflight before account deletion;
- probes registration closure locally so Cloudflare Access cannot intercept the application-level 403.

Fresh focused verification:

- unified patch parses successfully: 2 files, 46 insertions, 12 deletions;
- added egress JavaScript snippet passes `node --check`;
- revised closure-probe command passes `bash -n`;
- full documentation governance/link checks: **not claimed** without a full checkout; run them on the branch after applying the patch.

Suggested branch sequence:

```bash
git switch docs/1325-friends-family-beta
git apply --check /path/to/Taskdeck-PR-3098-review-fixes.patch
git apply /path/to/Taskdeck-PR-3098-review-fixes.patch
node scripts/check-docs-governance.mjs
node scripts/check-doc-links.mjs
git diff --check
```

## Patch integrity

```text
99990659664381713f5baab4df4fb96c0e0912486c08f832d2744278c09f2df8  Taskdeck-PR-3134-review-fixes.patch
3469208b85c41253c70ac2eae3652abc2eb4a6b38df043e22c726d64bf2f9f92  Taskdeck-PR-3098-review-fixes.patch
```
