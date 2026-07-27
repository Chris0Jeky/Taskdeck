# Taskdeck Revival Plan — Free Open Beta → Commercial Horizon

Last Updated: 2026-07-13

**Status:** Active planning spine (maintainer-decided 2026-07-10, **ADR-0044**; supersedes the archive pivot).
**Authority:** work not on this plan's ratified wave list is, by definition, not taken (the finite-work discipline from `COURSE_CORRECTION.md` §1.1 carries over unchanged). New backend surface is allowed only where §7 grants it.
**Evidence base:** `docs/analysis/2026-07-10_revival_assessment.md` (7 code-review dimensions, ~25 adversarially verified claims; market research with live-verified competitor data) plus the 2026-07-10 business research (monetization/licensing/beta-mechanics; sources cited inline below).
**Issue wave:** `REVIVAL-*` issues on GitHub (label `revival`); tracker REVIVAL-00 = `#1311` (amends `#1278`).
**Issue numbers:** REVIVAL-00 `#1311` · 01 `#1297` · 02 `#1298` · 03 `#1299` · 04 `#1300` · 05 `#1301` · 06 `#1302` · 07 `#1303` · 08 `#1304` · 09 `#1305` · 10 `#1306` · 11 `#1307` · 12 `#1308` · 13 `#1309` · 14 `#1310`.
**Phase-4 wave (ADR-0046, 2026-07-13, label `generalist`):** tracker GEN-00 = `#1327`; children GEN-01..12 = `#1315`–`#1326` (see §4 Phase 4).

---

## 1. Vision and positioning

**Taskdeck is the local-first, review-first action-item engine.** Meeting transcripts, voice-note transcripts, and messy notes go in — from any source: a local WhisperX pipeline, Granola/Otter exports, Meetily, plain paste. Evidence-linked, risk-classified proposals come out. Nothing touches your board until you approve it. Everything lives in a single SQLite file you own.

- **What we own:** the *back half* nobody else has made their core product — the market's documented failure mode is "action items get captured but don't move anywhere," and the OSS privacy leader (Meetily, 22.6k★) verifiably stops at transcription + summaries. Taskdeck's proposal machinery (provenance, confidence, side-effects, conflicts, revisions, diff-preview==apply, transactional audited execution) already exists and is the hard part to rebuild.
- **Second act (developer wedge):** the **write-gated MCP server** — "the task store your AI agents can write to but never corrupt." Agents propose; only humans approve; `approve_proposal` structurally does not exist. Incumbent MCP surfaces (Otter, Microsoft Planner) are zero-touch/direct-mutation; enterprise reference patterns (AWS) and the EU AI Act Art. 14 (enforceable 2026-08-02) converge on exactly our model.
- **What we are NOT doing:** competing on the capture front half (bots, live transcription, diarization) — it is crowded, commoditizing, and funded. We integrate with that ecosystem instead of fighting it.
- **The twist that stays:** the Paper UI — a distinctive, calm, keyboard-first aesthetic that reads as a product, not a CRUD demo — once its fonts actually load and its dossier stops fabricating data.

## 2. Business posture: free open beta → additive monetization

The beta is **free and wide open** — its job is adoption, feedback, and exposure while the maintainer develops the commercial side. The posture below adopts the lowest-regret patterns from the 2026-07-10 business research; the follow-up commercial ADR will finalize pricing when the beta earns it.

**Commitments made now (cheap now, impossible later):**

1. **MIT stays MIT — permanently.** Everything shipped in this repo stays MIT; monetization is *additive* (hosted service, future separately-licensed modules), never a relicense or a claw-back of shipped features. Every backlash case studied (Screenpipe 2026-06, Cal.com 2026-04, Budibase 2024, MinIO 2025) came from moving a boundary retroactively; every accepted model (Plausible CE, n8n SUL, Umami, PostHog `ee/`) declared it early. A `LICENSING.md` + license-commitment note ships before launch (REVIVAL-03).
2. **DCO, not CLA.** Inbound=outbound makes a future unilateral relicense of contributed code structurally impossible — which is exactly why the pledge is believable. Commercial code will live in separately-licensed modules (PostHog `ee/` pattern), so the maintainer never needs relicensing rights over the core.
3. **The free boundary** (never gated, ever): the core capture → proposal → review → apply loop; data export/portability; BYO API key and local-LLM use; single-user self-hosting. **Candidate paid surfaces** (not built yet — deliberately absent from the free beta so nothing is later subtracted): managed hosted instance, managed high-accuracy transcription/diarization pipeline, org-level approval policies + audit packs, team workspaces at scale, integrations packs. Features that already ship free (MFA, OIDC, board sharing) stay free.
4. **The hosted control plane stays private from day one.** Multi-tenant/billing/orchestration for a future cloud is never open-sourced — standard open-core practice that requires no pledge-breaking later.
5. **Trademark:** search + document first-use for "Taskdeck" before launch; the code is MIT, the name/logo are not (REVIVAL-03).

**Monetization sequencing (for the commercial ADR later, recorded here as intent):** (1) 3–6 months wide-open beta measuring activation/retention; (2) first paid product = a flat-priced hosted instance (~$9–19/mo — the Plausible/Umami pattern; self-hosters convert via convenience, essentially never via donations); (3) then a team tier at $8–12/user/mo on genuinely multi-user features (market-anchored: Meetily PRO $10, Anarlog/Hyprnote Pro ~$8; incumbents charge $8–30); (4) enterprise only if pulled. Supabase precedent: introduce pricing mid-beta once retention is proven, then hold it flat — price stability is itself an adoption asset.

**Telemetry & feedback posture (trust-first, r/selfhosted-grade):** telemetry **off by default**, opt-in via a first-run card with granular toggles (Home Assistant model), instance-UUID-only, never content, `TASKDECK_TELEMETRY=off` escape hatch, every field documented in `docs/TELEMETRY.md`, aggregates published publicly. **No silent third-party keys in release builds** (Hyprnote was confronted on its own Launch HN for exactly this). Crash reports = user-triggered diagnostic bundle, zero network calls. GitHub Discussions is the canonical feedback channel (Outline model — searchable, zero infra, solo-maintainer-sized); Discord only if traction demands it, with a same-day re-file rule. In-app "Send feedback" = prefilled GitHub issue URL, no network call. (REVIVAL-12)

**Data-trust guarantees before asking anyone to bring real meetings:** startup auto-backup of the SQLite file before any migration; one-click full export (already shipped: GDPR JSON + board export — document it as a headline feature); `UPGRADING.md` with per-version notes; v0.x semver honesty (BREAKING section first, release candidates for schema-touching releases — the Immich pattern). Marketing leads with "your entire workspace is a single SQLite file you own." (REVIVAL-07)

## 3. Horizon

| Release | Content | Gate |
|---|---|---|
| **v0.1 "First Light"** | Phase 1 complete: honest surfaces, safe public defaults, exercised release pipeline, welcoming README/onboarding | §6 ship gate |
| **v0.2 "Transcript Engine"** | Phase 2 complete: LLM transcript triage with evidence spans, OpenAI-compatible provider, risk-tiered approvals | a real 45-min transcript → reviewable, evidence-linked, typed action items on the maintainer's own board |
| **v0.3 "Open Beta"** | Phase 3 complete: slimmed surface, MCP packaged, feedback channel live, launched | launch executed; 48h response presence done |
| **v0.4 "Every Artefact"** | Phase 4 complete (ADR-0046): artefact intake (screenshots/PDFs/files), project dossiers, generalist legibility, friends-family channel | a screenshot → reviewable typed proposals on a real board; a non-technical invitee reaches first-approved-proposal unassisted |
| **Checkpoint (~8 weeks from start)** | Traction + dogfooding review | fall back only if both are absent; mixed outcomes require an explicit maintainer plan amendment |

## 4. Phases and waves (the issue map)

Dogfooding (`#1271`) runs through everything from day one — including WhisperX transcripts through the existing transcript capture tab. It is both the acceptance test and the cheapest issue-generator; dogfooding findings are exempt from the intake severity bar.

### Phase 0 — charter (this week)
| Item | Issue | Done when |
|---|---|---|
| Ratify this plan + ship gate; re-scope §D archive issues in place | REVIVAL-00 (amends #1278) | merged + tracker updated |
| Dogfooding starts | #1271 (unchanged) | daily real use; findings filed |

### Phase 1 — truth + safety before strangers (v0.1)
| Item | Issue | Notes |
|---|---|---|
| Registration gating: `Auth:Registration:Mode` = `Open`/`InviteOnly`/`Closed` | REVIVAL-01 | verified blocker: `/api/auth/register` is AllowAnonymous with no flag; environment key `Auth__Registration__Mode` |
| Remove the fake undo timeline + correct side-effect copy | REVIVAL-02 | verified: no revert endpoint exists anywhere |
| Licensing posture pack: LICENSING.md, license commitment, DCO + CI check, `ee/` placeholder, trademark search | REVIVAL-03 | hours now, impossible-cleanly later |
| Self-host Paper fonts + favicon + theme-color | REVIVAL-04 | verified: Fraunces/Inter/JetBrains Mono never load |
| Paper onboarding: guided first-board path + Login/Register in Paper | REVIVAL-05 | verified: setup modal unreachable from any Paper surface |
| README revival rewrite + demo GIF + MCP section | REVIVAL-06 | removes archive messaging ×3, fills the GIF slot |
| v0.1.0 release: dispatch pipeline, fix breakage, publish the GHCR image, fix render.yaml, UPGRADING.md + pre-migration auto-backup | REVIVAL-07 | folds #1123 + #1139; matches ADR-0044's required image channel |
| De-stub Today dossier or honest empty states | #1272 (unchanged) | the flagship screen must not lie |
| Re-point E2E + axe at Paper | #1274 (unchanged) | the default UI is currently the least-tested |
| CI keep/kill/gate pass | #1275 (unchanged) | zero always-red lanes |
| Branch protection on main | #1173 (unchanged) | minutes of settings work |

### Phase 2 — the transcript engine (v0.2; authorized new surface, §7)
| Item | Issue | Notes |
|---|---|---|
| LLM transcript triage (epic): strategy behind `ICaptureTriageService` for transcript sources, worker dispatch branch, chunked map-reduce, cap raise, triage schema v2 (type/assignee/due), deterministic fallback | REVIVAL-08 | the WhisperX payoff; seam verified in the assessment |
| Durable `Transcript` entity + evidence spans (`SourceSpan`/`EvidenceLink`) → every proposed card deep-links to its transcript span | REVIVAL-09 | the trust-gate UX no incumbent has |
| `OpenAICompatible` named provider + true SSE streaming | REVIVAL-10 | formalizes OpenRouter/Groq/DeepSeek; fixes fake streaming |
| Risk-tiered review prioritization + batch-confirm ergonomics + model-derived confidence (replace hardcoded 0.8/0.75) | REVIVAL-11 | reduces rubber-stamping without bypassing ADR-0003: every proposed board write still requires explicit approve, then explicit execute; no standing policy or confidence threshold may auto-apply it |
| Audio upload + local WhisperX sidecar | REVIVAL-08 phase 2b | **gated on transcript-paste proving value in dogfooding** |

### Phase 3 — slim + launch (v0.3)
| Item | Issue | Notes |
|---|---|---|
| Dead-surface amputation (cohorts stub, Integrations shell, voice composable, forecast/knowledge/agents views, fake semantic search, ink-bleed decision) | #1276 (scope expanded) | shrink the first 30 minutes to the honest core |
| Beta feedback + telemetry posture (Discussions, in-app feedback link, opt-in telemetry card + TELEMETRY.md, beta badge) | REVIVAL-12 | §2 posture |
| MCP packaging: README/docs, one-command setup, scoped API keys, wire hash-pinning, replace the stdio first-user identity fallback with explicit multi-user-safe configuration | REVIVAL-13 | folds #1154; identity must fail closed instead of misattributing an MCP action |
| Launch: r/selfhosted + Show HN + awesome-selfhosted PR + hosted static demo + 48h presence plan | REVIVAL-14 | expect probing on phone-home, license permanence, missing features — answers pre-written |

Bounded finish-or-close slices from the archive plan (#1134, #1135, #1128, #1175, #1138, #1222/#1227) continue as capacity allows, unchanged.

### Phase 4 — every artefact, everyone (v0.4; ADR-0046 **Proposed**, tracker `#1327`)

Seeded 2026-07-13 from the maintainer's twin-app evaluation (decision: extend the single app, defer the twin behind the GEN-12 evidence gate). **Ratification pending on GEN-00 `#1327`** — until the maintainer ratifies ADR-0046, only lane **G-A** foundational work is authorized (per maintainer instruction 2026-07-13); lanes G-B/G-C wait for ratification in addition to their technical dependencies. **Strictly subordinate to the v0.1 ship gate** — lane **G-A** is parallel-safe immediately (stays off the transcript-lane files while PR #1312 is open), lane **G-B** waits for REVIVAL-08 M1 (`#1312`) to merge, lane **G-C** follows Phase 1.

| Item | Issue | Lane |
|---|---|---|
| Artefact storage foundation: `SourceArtefact` entity + SQLite blob store + upload endpoint | GEN-01 `#1315` | G-A |
| Local text extraction: `IArtefactTextExtractor`, PDF text layer (PdfPig), extraction records | GEN-02 `#1316` | G-A |
| Consent-gated multimodal image extraction: provider content parts + vision | GEN-03 `#1317` | G-B |
| Artefact-aware triage routing through the transcript lane | GEN-04 `#1318` | G-B |
| Operation vocabulary: apply due dates + labels (closes the `dueDateHint` apply gap) | GEN-05 `#1319` | G-A |
| Paper intake UX: drop-zone wiring, paste-image, artefact previews, source affordance | GEN-06 `#1320` | G-B |
| Project dossier: per-board derived read model + Paper panel (approved state only) | GEN-07 `#1321` | G-C |
| Today enrichment: real attention aggregations (after `#1272`) | GEN-08 `#1322` | G-C |
| Untrusted-artefact threat model + prompt-injection rails | GEN-09 `#1323` | G-A (merges with/before GEN-04) |
| Generalist legibility: mode-scoped nav, plain-language pass, guided-first default | GEN-10 `#1324` | G-A |
| Friends & family beta channel (runbook, invite path, feedback loop, metrics) | GEN-11 `#1325` | G-C |
| Twin-app decision gate (strategy; default **no**) | GEN-12 `#1326` | checkpoint |

## 5. What agents need to know (execution contract)

- Every REVIVAL issue body is a self-contained agent brief: context, acceptance criteria, entry-point files, traps, and verification commands. Do not start work that lacks a tracked issue.
- Review gate: the repository's current declared tier, global `review-and-ship` skill, and global laws 2 and 11; do not copy the tier row here. The 2026-06 two-tier FULL/LIGHT gate with its aging rule is superseded; #1269 tracks retiring the phrase. Changes touching the capture→proposal→apply loop, auth, or migrations remain Taskdeck-specific lens-selection flags for the canonical pipeline.
- **Intake severity bar** (#1269): new issues only for data loss, silent corruption, security exposure real under the deployment model, or dogfooding/beta-user findings. Everything else is fixed in-PR or recorded as dated accepted-risk.
- Wave membership is the admission ticket. Review-seeded follow-ups go through the severity bar, not straight into the wave.
- Worktree protocol (`docs/WORKTREE_AGENT_PROTOCOL.md`) for parallel work; one coordinator synthesizes.

## 6. The v0.1 ship gate (repurposed #1278 exit criteria + revival additions)

- **(a)** Every default-reachable surface shows real data or an honest empty state (#1272; fake undo removed — REVIVAL-02).
- **(b)** Preview diff equals what Apply executes — **done** (#1235/#1280, regression-tested).
- **(c)** Provenance never names an actor that didn't act — **done** (#1273/#1283).
- **(d)** Registration is gateable; public deployment defaults are safe (REVIVAL-01; render.yaml fixed).
- **(e)** One validated, documented run path *per audience*: desktop exe (smoke-run from a real release) and `docker run` from a published image (REVIVAL-07).
- **(f)** Paper core loop has E2E + axe coverage; zero always-red CI lanes; branch protection live (#1274, #1275, #1173).
- **(g)** README/onboarding welcome strangers: no archive messaging, demo GIF present, guided first-board path in Paper (REVIVAL-05, REVIVAL-06).
- **(h)** Licensing posture published (REVIVAL-03).
- **(i)** ≥10 days of organic dogfooding data (#1271).
- A written **beta threat model** (public-facing self-hosted instances with untrusted registrants) replaces the single-user threat model; ACL/TOCTOU-class findings are re-triaged against it once (REVIVAL-00).

## 7. New-surface exceptions (everything else stays under the #1269 rule)

Authorized: REVIVAL-01 (registration gate), REVIVAL-08/-09/-10/-11 (transcript engine), REVIVAL-12 (feedback/telemetry), REVIVAL-13 (key scopes + hash-pin wiring + explicit multi-user-safe stdio identity).

**Phase-4 additions (ADR-0046, Accepted 2026-07-13; tracker GEN-00 `#1327`):** GEN-01/-02/-03/-04/-05 (the artefact intake pipeline — `SourceArtefact` entity + blob store + upload endpoint, extraction abstraction + records, provider multimodal content parts + consent-gated vision, triage routing for artefact sources, due-date/label apply operations), GEN-07 (board dossier read model + Paper panel), GEN-08 (Today attention aggregations), GEN-10 (mode-scoped navigation + guided-first default).

GEN-06 (`#1320`) is wave-authorized as the Paper UX over that approved intake pipeline, not as a separate backend-surface exception: it adds no second mutation path or standalone view. It remains lane G-B and therefore waits on its transcript/artefact-routing dependencies.

Not authorized without a plan amendment: the twin generalist application (GEN-12 `#1326` is the evidence gate), other new views/dashboards, new connector types, real undo (post-beta candidate), Postgres runtime (post-checkpoint candidate if hosted tier happens).

## 8. Metrics and the checkpoint

Tracked without invasive telemetry: GitHub stars + unique Discussion/issue participants per month + issues-to-stars ratio; GitHub Release download counts for the self-contained executable; opt-in ping count as a clearly-labeled lower bound on active installs; local-only activation milestones (first capture, first approved proposal, first board apply) shown to the user as an onboarding checklist and included in the opt-in ping only as an aggregate boolean. GHCR image pulls are not used as a checkpoint metric because the plan does not depend on a public registry counter.

**Checkpoint (~8 weeks from Phase 0):** fall back to the archive plan (`COURSE_CORRECTION.md` §4) only if the beta shows **no organic traction** (real users filing issues/discussions, meaningful downloads, HN/Reddit engagement) **and** the maintainer's own dogfooding has not stuck. Any mixed outcome requires an explicit maintainer assessment and plan amendment rather than an automatic archive decision. Phase 1 leaves the fallback ~90% complete.

The GEN-11 friends-family channel (`#1325`) adds a second signal stream — non-technical activation and retention — reviewed at the same checkpoint against the GEN-12 (`#1326`) twin-app gate criteria.

## 9. Risks (named, accepted)

- **Solo-maintainer support burden** — mitigated by Discussions-only channel, the severity bar, pre-written launch answers, and the checkpoint.
- **Incumbents move into the follow-through lane** (Circleback automations, Otter MCP) — mitigated by the local-first + human-gate combination they structurally can't copy (their business is the cloud and the autonomy).
- **The beta exposes quality gaps fast** (Hyprnote's diarization "dealbreaker" precedent) — mitigated by honest v0.x signaling and shipping the feedback loop before the launch spike.
- **Scope regrowth** — the same failure mode the archive analysis diagnosed; mitigated by §5's admission rules and this plan being the single wave list.

## 10. Related documents

- `docs/decisions/ADR-0044-revival-pivot-open-beta.md` — the decision this plan executes
- `docs/decisions/ADR-0046-generalist-expansion-single-app.md` — the Phase 4 amendment (artefact intake, dossiers, generalist reach; twin-app deferral)
- `docs/analysis/2026-07-10_revival_assessment.md` — evidence base (code + market)
- `docs/COURSE_CORRECTION.md` / `docs/PROJECT_TRAJECTORY.md` — the 2026-07-02 analysis pair; fallback plan + finite-work discipline
- `docs/IMPLEMENTATION_MASTERPLAN.md` — Direction section (points here)
- `OUTSTANDING_TASKS.md` §E/§F — the maintainer-visible checklist mirrors (revival wave / Phase-4 generalist wave)
