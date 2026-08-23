# Repository Realignment — 2026-08-23

**Executed by:** agent session under the maintainer's explicit realignment authorization
("modify anything that needs to be modified"), from the 2026-08-23 master brief
(`TASKDECK_REPOSITORY_REALIGNMENT_MASTER_BRIEF_2026-08-23.md`, archived in this directory).
**Baseline:** `main` = `a43df082c` (identical to the brief's checkpoint; no drift). Zero open PRs
at start. Worktree `.worktrees/realign-0823`, branch `chore/realignment-2026-08-23`.

## 1. Inventory (Phase 1, read-only — snapshots in this directory)

- `open-issues.tsv` — 172 open issues with labels/milestones at baseline.
- `labels.tsv` — the 40 pre-realignment labels.
- `project-status-snapshot.tsv` — ProjectV2 Status for every open issue at baseline:
  Now 0 · Next 6 · Blocked 19 · Review 5 · Pending 142 (plus 24 closed issues stuck in
  Review/Pending).
- Releases: v0.1.0 (2026-08-19, 4 platforms) · v0.1.1 (2026-08-21, win-x64, Latest). **No v0.1.2
  tag or release existed** — the "v0.1.2 published" statement in the decision-studio export was a
  misstatement; v0.1.2 is the open milestone, not a shipped release.
- Branch protection (read-only): required contexts = the three security scans (ADR-0035);
  DCO advisory; force-push/deletion blocked; enforce_admins off.

## 2. Contradiction matrix — dispositions

| Contradiction | Resolution applied |
|---|---|
| Review-first invariant vs user-selectable autonomy | Shipped review-first (ADR-0003/GP-06/ADR-0056) stays operative; future direction recorded as **ADR-0057 (Proposed)** — separation of duties, policy engine approves under user grants, no agent self-approval. Ratification is an open maintainer decision. |
| "No approval tool" vs policy-authorised execution | ADR-0057 §2: MCP still exposes no agent-callable approve/apply; delegated execution is represented as delegated authority, never fake approval. |
| README "transcript extraction planned for v0.2, not shipped" vs shipped code | README corrected: transcript-source LLM triage + evidence spans + deep links ARE shipped (PRs #1312/#1571/#1582/#1556/#1835); the honest provider-egress hedge retained. |
| v0.1.2 release truth | No tag exists; docs updated to "in progress, milestone open, q-3 scope"; UPGRADING.md was already accurate. Windows incident (2026-08-22 inherited-Gemini startup failure) now named on the download path (README + WINDOWS_QUICK_START troubleshooting). |
| GPL core vs proprietary intention | No licence artifact touched. Commercial/licensing decision surface seeded as a `decision`+`human-action` issue; LICENSING.md/ADR-0050 remain the only policy. |
| Name-kept ruling vs open legal language | q-6 recorded on #1482 (stays open as the commercial-gate tracker); spent "decide before v0.1.0" line annotated. |
| Revival direction vs archive-era labels/trackers | `archive-closeout` label re-described as historical provenance; live work items relabelled (revival/bug/docs/tech-debt); #1278/#1277 kept as the fallback records, demoted Priority I→III, status Blocked-on-checkpoint. |
| Telemetry: local-only vs heavy-beta vs opt-in policy | Trust posture NOT reversed. The opt-in REVIVAL posture stands; an explicit consent-based Beta Observability Mode was proposed inside #1308 (decision label). |
| Guided/Workbench/Agent mode truth | #1972 confirmed (Agent mode is a false affordance); mode disposition + five-destination IA seeded as one `decision` issue rather than silently re-scoping findings. |
| Provider set vs Gemini removal | ADR-0055/PR #1887-#1888 truth propagated (STATUS Gemini line, README provider claims); #1879 re-scoped to the BYOK decision residual. |
| Queue caps 4/8 vs decision-studio 30/100 | 4 Now / 8 Next preserved (brief's own instruction); 30/100 interpreted as unbounded *evidence* archive, recorded in PRODUCT_DIRECTION.md §6. |
| Checkpoint clock (REVIVAL_PLAN vs ADR-0044) | Resolved per q-8 in dogfooding/README.md, REVIVAL_PLAN, masterplan: floor 2026-09-01; ADR-0044's conditions control. |

## 3. Documentation changes (this branch)

1. **`docs/strategy/PRODUCT_DIRECTION.md` (new)** — canonical strategy spine: three-layer identity,
   trust-model reconciliation, release-theme ladder, guardrails, open decision surfaces. All claims
   carry interpretation labels (COMMITTED/LEANING/PROVISIONAL/SHIPPED/POLICY).
2. **`docs/decisions/ADR-0057-user-sovereign-delegated-authority.md` (new, Proposed)** + INDEX row
   + GP-06 direction note (GP-06 text unchanged) + archive-pivot footnote on old parked ADR rows.
3. **`docs/REVIVAL_PLAN.md`** — demoted to execution plan under the spine; horizon table updated to
   shipped truth + q-3/q-8 rulings.
4. **`docs/ops/GITHUB_LABEL_TAXONOMY.md`** — rewritten (was 2026-04-01/archive-era): current
   45-label set, new semantic labels, current Priority semantics.
5. **`docs/ISSUE_EXECUTION_GUIDE.md`** — 495 → ~120 lines; delivered Stage-0..8 listings moved
   verbatim to `docs/archive/planning-history/ISSUE_EXECUTION_STAGES_2026-02_to_2026-05.md`;
   current issue-state model (Priority × Status × milestone) documented.
6. **Public truth:** README (shipped-transcript truth, live roadmap block, v0.1.1 known limitation,
   dead #1296/#1173 pointers removed), WINDOWS_QUICK_START (v0.1.1 example + real failure-mode
   troubleshooting), START_HERE (packaged Windows path first), USER_MANUAL (ADR-0056 scoping),
   dogfooding/README (q-8 resolution), GITHUB_PROJECT_AUTOMATION (defers to taxonomy),
   GOLDEN_PRINCIPLES (stamp + companions), CLAUDE.md + docs/INDEX.md pointers.
7. **`docs/STATUS.md`** head-leaned with history rotated to `docs/archive/status-history/` and
   **`docs/IMPLEMENTATION_MASTERPLAN.md`** truth pass (separate commits; executes #1138's rotation
   scope).

## 4. GitHub metadata changes (Phase 4 — full log: `metadata-log.tsv` in this directory)

- **Labels:** +5 (`decision`, `human-action`, `dogfooding`, `product-truth`, `historical`);
  descriptions synced for ~25 previously empty/stale labels; `archive-closeout` re-described as
  historical; Priority I–V descriptions modernised (I now cites q-3).
- **Milestones:** "v0.1.0 — First Light" closed (shipped, 0 issues); "v0.2.0 — Open Doors" renamed
  **"v0.2 — Coherent Context-to-Action Loop"**; **"v0.3 — Open Beta + Accountable Agents"** created;
  v0.1.2 description set to the q-3 scope.
- **Milestone membership:** v0.1.2 ← #1876 (pre-existing) + the product-facing Priority I tranche
  (#2005 #2004 #1997 #1992 #1973 #1967 #1966 #1949 #1940 #1938 #1947 #1242).
  **Interpretation call (flagged for the maintainer):** q-3 says "the open Priority I tranche";
  repo-tooling/CI Priority I items (#1512 #1521 #1553 #1590 #1651 #1710), the GEN-gating security
  boundary #1429, decision-gated #1653, and trackers/epics/human items (#1311 #1278 #1277 #1271
  #1304) were **deliberately left off the milestone** as non-release content — they keep Priority I
  (except #1278/#1277 → III) and are one `gh issue edit --milestone` away if the maintainer reads
  q-3 more literally. v0.2 ← #1304 #1305 #1306 #1307 #1308 #1628 #1879. v0.3 ← #1309 #1310 #1772.
- **ProjectV2 Status:** Now = {#1876, #1271}; Next = {#2005 #1966 #1973 #1949 #1940 #1938 #1997
  #1512} (8/8 cap); the six stale Next residuals demoted to Pending; 8 stale Blocked → Pending;
  5 stale Review → Pending/Blocked; #1879/#1936/#1278/#1277 → Blocked; 24 closed issues stuck in
  Review/Pending → Done. Priority field mirrored for all label changes.
- **Priority labels:** added to 6 unlabelled issues (#2003 #1853 #1854 → III, #1821 → III,
  #1812 #1819 → IV); #1278/#1277 I → III.
- **Semantic labels applied:** `dogfooding` on the 31 sweep/horizon findings; `product-truth` on 11
  truth defects; `decision` on 10 decision-gated issues; `human-action` on 12 human-gated issues.

## 5. Issue reconciliation (Phase 5 — executed; 74 issues touched)

- **Closed with evidence (4):** #996 (PAPER-00 — all 12 children verified delivered; residuals →
  #1276/#1266), #219 (voice capture — superseded by REVIVAL-08 M4 per its own re-scope; the
  SpeechRecognition path verified dead by construction), #1553 (guard shipped in PR #1551;
  `Test-New-CodexIssueWorktree.ps1 -Case git-add-failure` re-run PASS on 2026-08-23), #1710
  (shipped in PR #1675/#1862; `check-codex-path-trust.ps1` re-run exit 0 on 2026-08-23). The
  #1553/#1710 closures rest on today's local runs plus the merged PRs' recorded CI — stated on
  both threads.
- **Body updates:** #1311 (Phase-1 boxes ticked with PR citations; MIT line annotated to ADR-0050;
  Phase-2 status), #1327 (ratification boxes + delivery truth), #1947 (8 closed children ticked,
  #1948/#1949 added), #1949 (5 fixed rows), #1142 (11-closed/8-open child truth), #1322 (delivered
  aggregations struck), #1967 (refuted finding (c) struck → #1987), #1984 (finding 1 superseded by
  #2005), #1938 (title priority corrected to match label), #1607 (stale 64/415 baseline → 40/391),
  #1879 (retitled to the BYOK decision residual).
- **Dated evidence/state comments** on ~60 further issues (every one prefixed
  "Realignment 2026-08-23"). Executor corrections worth noting: #1983 is OPEN (an analysis-pass
  claim that PR #1993 closed it was refuted); #1935 has seven hardcoded ⌘ sites, not six; the
  #1628 "degradedReason lost" claim was corrected on-thread (the *classification* is lost, the
  reason is persisted); #1276's delete list was corrected (`PaperEmptyState.vue` is live).
- **Post-executor milestone truth:** v0.1.2 open:13 · v0.2 open:7 · v0.3 open:3 ·
  "v0.1.0 — First Light" closed. Open issues: 172 → 171 (−4 closures, +3 seeded).
- **Work preservation:** the uncommitted #1876 v0.1.2 draft (10 modified files + 1 new) was
  committed and pushed to `origin/issue-1876/desktop-retired-provider-diagnostics` (base
  `0f38c692c`, needs rebase before review).

## 6. New issues seeded (3 — everything else reused existing issues)

1. **#2011** — Ratify or reject **ADR-0057** (delegated-authority model) — `decision`, `strategy`,
   `automation`, `human-action`.
2. **#2012** — **Commercial/licensing decision surface** — copyright/contribution audit +
   business-model choice before any proprietary transition — `decision`, `human-action`, `strategy`.
3. **#2013** — **Guided five-destination IA + Guided/Workbench/Agent mode disposition** —
   `decision`, `strategy`, `ux`, `frontend` (links #1936 #1940 #1946 #1972).

## 7. Open maintainer decisions (register — none silently ratified)

| # | Decision | Where it lives |
|---|---|---|
| 1 | Ratify/reject ADR-0057 (autonomy invariant, presets, classification, audit schema) | new decision issue; ADR-0057 |
| 2 | Commercial/licensing model + copyright audit before proprietary transition | new decision issue; #1482 for name/legal residuals |
| 3 | Beta Observability Mode vs current opt-in-only telemetry (Option A/B) | #1308 |
| 4 | Guided IA (five destinations) + Agent-mode disposition | new decision issue; #1972 #1936 |
| 5 | Victory/progress dossier relation to GEN-07 | #1321 |
| 6 | #1653 TOTP encryption: v0.1.2 tranche membership + the four 2026-08-12 questions | #1653 |
| 7 | #2004 chat-workflow redesign ADR + v0.1.2 honesty-slice split | #2004 |
| 8 | q-3 tranche interpretation (see §4 milestone-membership call) | this log; v0.1.2 milestone description |
| 9 | GEN wave (v0.4) vs v0.2 Context-to-Action theme overlap (#1318/#1320/#1322/#1323 sequencing) | #1327 |
| 10 | #1131 CLI authorization: local-admin surface or board-access routed | #1131 |
| 11 | CodeQL re-enable criteria (#1819), code-signing (#1167) — human settings/purchases | those issues |
| 12 | Dogfooding sprint, it/es translation review, real-phone #1821 check, second-machine key, OneDrive hydration | OUTSTANDING_TASKS.md §H |

## 8. Deliberately unchanged

`LICENSE`, `LICENSING.md`, ADR-0050, contribution terms; all Git tags and releases; branch
protection and every repository/environment setting; production environment; all secrets; all
`.worktrees/` checkouts (nothing pruned); GP-06's invariant text; every human-owned checkbox in
`OUTSTANDING_TASKS.md`; the 38 advanced decision-studio questions (recorded as open, not converted
into issues).

## 9. Verification record

Filled at PR time: governance checks (`check-docs-governance`, `check-golden-principles`,
`check-github-ops-governance`) green on this branch; exact-head `ci-required.yml` + the
review-and-ship gate on the PR(s); post-mutation GitHub re-query (labels/milestones/status counts)
recorded in `metadata-log.tsv` and the PR body.
