# Revival Assessment — Can Taskdeck Compete, and As What?

Last Updated: 2026-07-10

**Audience:** the maintainer. This document re-examines the 2026-06-13 archive pivot in light of the maintainer's successful WhisperX + cheap-LLM prototype and asks: could Taskdeck instead be revived as a free-to-deploy, easy-to-adopt product — possibly adjacent to AI meeting-notes tools (Fireflies, Granola, Otter), possibly something leaner with a twist? Minimum bar set by the maintainer: deployable for free, easy adoption, real user value.

**Status:** Proposed analysis. Nothing here overrides the archive plan (#1278) until the maintainer decides. The archive closeout wave and this revival path share most of their near-term work (see §6), so the decision does not need to be made before starting.

**Provenance:** produced 2026-07-10 by a two-track multi-agent analysis. Track 1: seven parallel code-grounded dimension reviews over the repo (product surface, backend architecture, LLM pipeline, frontend/UX, deployment/adoption, quality/risk, integrations/MCP) with ~25 load-bearing claims adversarially verified by independent agents (verdicts inline: CONFIRMED / ADJUSTED / REFUTED). Track 2: market deep-research across five angles (~15 sources); GitHub-traction claims were live-verified against the GitHub API on 2026-07-10; pricing/complaint claims are corroborated across 3+ independent sources but mostly vendor-adjacent blogs — spot-check before quoting publicly. Claims that did not survive verification are flagged rather than dropped.

---

## 1. The verdict in one paragraph

Taskdeck as it stands is **~80% of the *back half* of a meeting-intelligence product with 0% of the front half**. The back half — durable capture queue → triage → evidence-carrying, risk-classified proposals → human-approved board changes, with provenance, revisions, diff-preview-equals-apply, MCP write tools that structurally cannot self-approve — is real, hardened, and adversarially verified. The front half — audio ingestion and any LLM in the capture path — does not exist: capture triage is regex extraction, and a pasted WhisperX transcript today collapses into one junk card (CONFIRMED). The market research shows the front half (transcription, summaries) is crowded and commoditizing — "transcription is a commodity in English; the real differentiation is what happens around it" — while the back half ("action items that actually go somewhere, with a trust gate") is the market's documented failure mode and **nobody's core product**, including the 22.6k-star open-source privacy leader whose feature set verifiably stops at transcription + summarization. That is the lane. The revival premise is stronger than the archive decision assumed, and the marginal cost of attempting it is low because most of the pre-public work is already on the archive exit-criteria list.

---

## 2. What the codebase is (code-grounded, adversarially verified)

### 2.1 Assets

- **MIT license, public repo** — distribution legally unblocked today (CONFIRMED; only nit: package/csproj license metadata absent for registry artifacts).
- **One `git tag v0.1.0` from shippable binaries.** `release-desktop.yml` builds win-x64/linux-x64/osx-x64/osx-arm64 self-contained single-file exes with the SPA embedded, SHA256 checksums, and a post-publish boot smoke test — but it has **never run once**; zero tags, zero releases (ADJUSTED: pipeline complete on paper, unexercised — expect breakage on first dispatch).
- **Zero-config desktop first-run** (CONFIRMED): auto-generated JWT secret + connector key into owner-only `appsettings.local.json`, DB auto-resolved to `%LOCALAPPDATA%\Taskdeck`. Better first-run engineering than most shipped OSS tools.
- **The transcript product was pre-built into the schema**: `CaptureSource.TranscriptPaste/Voice/MeetingIntegration/TranscriptFile` with a raised 51,200-char cap; the CaptureModal already ships a transcript tab (paste + `.txt` upload) flowing end-to-end into the proposal→review→board pipeline (CONFIRMED).
- **Cheap-LLM support is config-only, today** (CONFIRMED): the OpenAI provider's `BaseUrl` is configurable and SSRF-blocklist-validated only — OpenRouter/Groq/DeepSeek/Anthropic-compat endpoints work with `Llm:OpenAi:BaseUrl` + key + model, no code change. Providers carry circuit breakers, per-user quotas (60 req/h, 100k tokens/day), kill switch, SSRF DNS-level guards, degraded-mode honesty.
- **Review-first is structurally enforced, not conventional** (CONFIRMED): only Approved proposals execute, transactionally, with policy/permission revalidation and per-operation audit; preview diff and executor both materialize the latest revision (#1235 fixed); MCP write tools can only create proposals and `approve_proposal` deliberately does not exist (ADJUSTED: 5 of 6 write tools proposal-only; `create_capture` writes the inbox directly — still never a board).
- **Production-grade security fundamentals for a self-hosted tool**: default-deny `FallbackPolicy`, CORS fail-closed in Production, JWT ≥32-char floor with placeholder block, default-on rate limiting (per-IP auth, per-user hot-path/capture, per-API-key MCP), AES-GCM connector credentials, CSP/HSTS, claims-first controllers enforced by architecture tests, MFA, GitHub OAuth with PKCE.
- **Deployment substrate exists at every rung**: one-command dev-up (Windows + POSIX), hardened 3-container compose fitting a $5/1GB VPS (768MB total limits, exactly 2 secrets), single-container production image (API+SPA+SignalR, non-root, PORT-adaptive), Render/Railway blueprints, AWS Terraform.
- **Multi-channel capture works**: web modal, PWA share-target with IndexedDB offline queue, VS Code extension, chat, CLI, MCP. Backend-less demo mode (`VITE_API_BASE_URL=''`) enables a free static hosted demo.
- **A distinctive aesthetic**: the Paper/Graphite "Ember Edition" UI (~11.5k LOC bespoke Vue) — paper texture, ember ink, stamps, ledger hairlines, day-sealing dossier metaphor — a real visual identity vs generic SaaS, plus genuine keyboard-first interaction (palette, global capture hotkey, guarded review keymap).

### 2.2 Liabilities (must-fix before strangers, or must-remove)

- **No LLM anywhere in capture→proposal** (CONFIRMED). Triage is regex (checklist/bullet/numbered/delimiter); prose transcripts → one junk card. The advertised intelligence of the core loop is unbuilt. LLM providers are wired only into chat.
- **The Review "undo window" is a lie** (CONFIRMED, new finding): `PaperUndoTimeline` animates a closing 6h window and `SideEffectAnalyzer` claims mutations "can be undone from the activity log," but **no undo/revert endpoint or service exists anywhere in the backend**. Trust-destroying for a review-first product.
- **The default Today screen fabricates data** (CONFIRMED): `buildStubDossier` hardcodes a fictional agent ledger, decisions, boards, carry-over, "2h 14m focus," a narrative lede — no in-UI disclaimer; only cadence/streak/seal/tomorrow-note are live (#1272 open).
- **First-run in the default theme has no onboarding** (CONFIRMED): the setup modal/onboarding steps render only in the frozen Legacy branch; Paper's empty state is "Nothing waiting. Good." with no CTA. The guided first-board path is unreachable from any Paper surface.
- **The signature fonts never load** (CONFIRMED): Fraunces/Inter/JetBrains Mono have no @font-face/link anywhere; Paper silently degrades to Georgia/system fonts on every fresh machine.
- **Open registration with no disable/invite flag** (CONFIRMED): `/api/auth/register` is `[AllowAnonymous]`; only rate limiting stands between a public deployment and open signup + LLM-spend abuse.
- **`deploy/render.yaml` crashes at first boot as written** (CONFIRMED): missing `Connectors__EncryptionKey` under headless Production hard-fail.
- **Repo supply chain unprotected** (CONFIRMED): zero required status checks, zero required reviews, force-push and deletion allowed on main — untenable for a product strangers deploy (#1173 is minutes of settings work).
- **Streaming is fake in all four providers** (CONFIRMED): buffer-full-completion-then-replay-word-split; OpenAI/Ollama even send `"stream": false`.
- **Provenance confidence is decorative**: hardcoded 0.8/0.75 constants, not model signal — undermines the exact trust story being sold.
- **Long-input machinery absent**: no chunking, no tokenizer (len/4), 2048 default output tokens, 4,000-char board context, 51,200-char transcript cap (~45–60 min of speech).
- **SQLite capacity ceiling is measured, not theoretical**: the project's own nightly k6 shows board-write p95=2.0s at just 20 VUs (the sole recurring cause of the daily-red nightly lane). Fine for personal/small-team; a hard cap beyond that. Single queue-worker host assumption is documented in-code.
- **Hollow surfaces inflate the product**: cohorts dashboard (stub, always empty), Integrations view (connector interface literally cannot fetch data — health-check only, CONFIRMED), voice composable (unwired AND browser-impossible by design), "browser extension" (two enum values, no code), fake hash-based "semantic search," in-memory `Contains` search (no FTS), MCP hash-pinning registered-but-never-called (#1154), CLI mutations bypassing the proposal loop and board authz (#1131 AC2).
- **README actively repels adopters**: archive messaging ×3, empty demo-GIF placeholder, zero mention of the MCP server (the flagship differentiator is undiscoverable).
- **Surface area vs one maintainer**: 43–44 controllers, 45 DbSets, 200+ Application services, dual Paper/Legacy UI (~26 routes still Legacy-inside-Paper-shell), E2E suite pinned to Legacy (Paper — the shipped default — is the least-tested UI; only ~6 of 32 specs touch it; the only axe suite runs against Legacy).
- **CI gate nuance** (ADJUSTED): ci-required is green and real, but of the three security scans only gitleaks blocks; dependency + SAST are advisory (#1175).

---

## 3. What the market is (mid-2026)

Verification level varies: **[V]** = live-verified primary source; **[C]** = corroborated across 3+ sources; **[U]** = single/vendor source, treat as directional.

- **Pricing**: per-seat $8–30/user/mo across incumbents — Fireflies Pro $10 (free: 800 min storage), Otter Pro $8.33–16.99 (free: 300 min/mo; Business $30), Granola $14–18 (25 free lifetime meetings on individual), tl;dv $18, Circleback $25 (no free tier), Fathom **free unlimited tier** with paid teams [C]. Documented AI-credit overage blowups ($95/mo plans becoming $293) [U]. **"Free" alone is not a wedge — Fathom already gives it away.** The wedge must be ownership/trust/workflow.
- **Bot fatigue is real but the botless wedge is closing**: Fathom (Oct 2025), Otter Enterprise (Oct 2025), Fellow 5.0, Granola (always), Fireflies (rolling out) all ship bot-free capture [C]. Google Meet now flags notetaker bots as security risks [U]; Bloomberg (2026-06-29) and AP (2026-07-09) covered the etiquette/consent backlash [V-secondary].
- **Privacy/data-ownership is the wide-open wedge**: Otter faces a federal class action (filed 2025-08-15, N.D. Cal.) for recording without consent [V-secondary]; Granola trains on user data by default (Enterprise to opt out) [C]; vendors resell transcript data and retain metadata after deletion; voiceprints raise BIPA concerns; attorneys warn about privilege waiver [C]. One survey puts privacy as the #1 adoption barrier for 73% of businesses [U — vendor survey]. Market sized $623M (2025) → projected $3.5B (2035) [U].
- **Open-source local-first traction is real and verified — on the capture side only**: Meetily 22,619★ / 2,378 forks / v0.4.0 2026-06-05 [V], Screenpipe 19,742★ / release v2.5.103 dated 2026-07-10 / YC S26 [V], Hyprnote (YC S25) launched well on HN. **But**: Meetily's "production-ready" claim was REFUTED by its own tracker (crash-on-recording #594, Parakeet fails >5 min sessions, Pre-Release badge) [V]; Screenpipe moved MIT → commercial source-available in June 2026 with $25–150/seat pricing [V] — a fresh, verified **gap for a genuinely free, MIT, self-hostable tool**, and the r/selfhosted crowd punishes rug-pulls. Most importantly: **Meetily's verified feature set stops at transcription + summarization — no action-item extraction, no review workflow, no board** [V]. The back-half seam is unoccupied even by the OSS privacy leader.
- **The follow-through gap is the documented failure mode**: "most tools are good at transcription, and almost none are good at what happens after… action items get captured but don't move anywhere" [C]; ~44% of action items never completed, 50–70% improvement when notes connect to a task workflow [U — vendor survey, verify before public use].
- **MCP is now table stakes, but write-gated MCP is not**: Otter, Fireflies, Fellow, MeetGeek expose meeting data via MCP [C]; Otter's positioning is explicitly zero-touch ("without anyone lifting a finger") [V-vendor]; Microsoft's Planner MCP mutates tasks directly under Entra permissions, no per-action approval [V-vendor]; AWS's reference agent pattern is "ask before posting" [V-vendor]. **Enterprise guidance converges on exactly Taskdeck's model; no product owns it.** EU AI Act Article 14 human-oversight obligations enforceable 2026-08-02; NIST agent standards initiative underway [C]. Caveat from the same literature: blanket approve-everything trains rubber-stamping — risk-tiered gating is the recommended pattern (Taskdeck already computes per-proposal risk).
- **Acquisitions**: Atlassian bought Rewatch (Aug 2024) explicitly to turn meeting notes into Jira issues — the exact capture→tracked-work pipeline [V-secondary]. But acquirers buy teams + platform-fitting IP, not solo repos; treat as lottery ticket, not plan.
- **Distribution reality**: HN launch ≈ +121★/24h on average for AI tools; "Show HN" tag confers no measured advantage; timing matters [V-primary, arXiv]. Amurex's Show HN: 90 points — mid-tier is the norm. The self-hosted audience treats privacy defaults as make-or-break (Amurex was roasted for hardcoded telemetry) and rejects browser-extension-only delivery. Durable channel = Docker-first + honest README + r/selfhosted/awesome-selfhosted, not a launch spike.

---

## 4. Positioning options

**A. The review-first action-item engine (recommended headline).** "Transcripts in — from WhisperX, Granola/Otter exports, Meetily, plain paste — evidence-linked proposals out, applied to your board only when you approve. 100% yours, MIT, BYO key, works offline."
*For:* the seam is verified-unoccupied (even Meetily stops at summaries); the follow-through gap is the market's documented failure; every trust artifact (provenance, risk, diff preview, revisions, audit) already exists and is the hard part to rebuild; complements rather than fights the capture-side OSS tools; regulatory tailwind; Rewatch precedent shows this pipeline is what acquirers want.
*Against:* incumbents are moving toward it (Circleback automations, Fellow action items, Otter MCP); requires building the LLM triage engine + evidence spans (the one big build); "action items" can read as feature-not-product — the board must carry its weight.

**B. The genuinely-free MIT self-hosted meeting suite (compete with Meetily head-on).**
*For:* Screenpipe's license rug-pull + Meetily's verified quality gaps + open-core paywalls leave a "truly free, permissive, polished" slot; Docker/compose substrate exists.
*Against:* requires the entire audio front half (capture, diarization, GPU pipelines) a solo .NET+Vue maintainer would build from scratch against funded Rust/Tauri teams; the capture side is where all the OSS competition already is. **Not recommended as the product goal — adopt only its distribution tactics.**

**C. The human-approval task gateway for AI agents (MCP-first developer wedge).** "The task store your agents can write to but never corrupt."
*For:* the write-gated MCP architecture is already enforced in code and verified; write-with-approval is unoccupied while read-mostly MCP became table stakes; Planner MCP's direct-mutation contrast writes the marketing copy itself.
*Against:* MCP server is currently unpackaged, undocumented in the README, keys are unscoped, hash-pinning unwired (#1154); a developer-niche audience today.
*Verdict:* ship it as the developer-facing second act — it shares ~90% of its work with A.

---

## 5. The plan (if reviving)

Lesson from `COURSE_CORRECTION.md` that applies doubly here: unbounded goals never finish. A revival needs a ratified, finite ship-gate exactly as the archive needed exit criteria. Phases below have completion conditions; most Phase-1 items are already archive exit criteria, so this work is not wasted if the revival is later abandoned.

**Phase 0 — decide + re-point (this week).**
Amend #1278: the archive exit criteria become the **v0.1 public ship gate** (same honesty/coverage/run-path items, new purpose). Start dogfooding now (#1271) — including WhisperX transcripts of real meetings pasted through the existing transcript tab; it is simultaneously the pivot's acceptance test and the cheapest issue-generator. *Done when:* amended charter committed; dogfooding started.

**Phase 1 — truth + safety before strangers (~1–2 weeks of sessions).**
1. De-stub the Today dossier or honest empty states (#1272).
2. Remove the fake undo timeline + correct SideEffectAnalyzer copy (or scope a real revert — recommend remove now, build later).
3. `Auth:Registration` disable/invite flag (new, small).
4. Branch protection with existing ci-required contexts (#1173); CI keep/kill pass (#1275); fix render.yaml key.
5. Dispatch `v0.1.0` → fix whatever the never-run release pipeline reveals; publish a GHCR image (`docker run` one-liner).
6. README revival rewrite + demo GIF + MCP section; self-host the Paper fonts; port onboarding/Login to Paper (guided first-board path); real favicon.
7. Re-point E2E default at Paper + Paper axe (#1274).
*Done when:* a stranger can go from README → running app with honest screens in ≤15 min on Windows/macOS/Linux/VPS.

**Phase 2 — the transcript engine (the WhisperX payoff, ~2–4 weeks of sessions).**
1. `LlmCaptureTriageService` behind the existing `ICaptureTriageService` seam, selected for transcript sources (`IsTranscriptSource`), deterministic extractor as degraded fallback. New worker dispatch branch + queue predicates for a transcript RequestType (verified necessary — reuse is pattern-level, not drop-in).
2. Chunked map-reduce for long transcripts; raise the 51,200-char cap; dedupe via the existing idempotency-key scheme.
3. Triage schema v2: item type (action/decision/question), assigneeHint, dueDateHint; real per-item model-reported confidence replacing the hardcoded 0.8/0.75.
4. Durable `Transcript` entity + implement the declared-but-empty `SourceSpan`/`EvidenceLink` vocabulary → **every proposed card deep-links to the transcript span that justified it** (the trust-gate UX no incumbent has).
5. `OpenAICompatible` named provider (formalizes OpenRouter/Groq/DeepSeek; one true-SSE streaming implementation covers all of them).
6. Risk-tiered approvals: opt-in auto-apply for low-risk proposals using the existing risk classification (answers the over-gating/rubber-stamping critique from the HITL literature).
7. Audio upload + local WhisperX sidecar worker = Phase 2b, **only after** transcript-paste proves value in dogfooding (the multipart-upload pattern exists via `ExportController`; audio infra is otherwise net-new — verified).
*Done when:* a real 45-min meeting transcript produces reviewable, evidence-linked, typed action items you actually approve onto your own board.

**Phase 3 — distribute + slim (parallel/after).**
1. Amputate hollow surfaces (#1276 + cohorts stub, Integrations shell, forecast/knowledge/agents views, fake semantic search) — shrink the first-30-minutes to the honest core.
2. Package the MCP story: docs, one-command setup, scoped API keys, wire hash-pinning (#1154).
3. Launch: r/selfhosted + Show HN + awesome-selfhosted PR + hosted static demo (demo mode exists). Copy leads with ownership + follow-through, not "free."
*Done when:* launched; feedback loop running.

**Kill criteria (decide now, not later):** if after Phase 2 + launch (~8 weeks) there is no organic traction signal (users/issues/stars/discussion) **and** your own dogfooding isn't sticking, fall back to the archive plan — which remains ~90% intact because Phase 1 was its exit criteria.

---

## 6. Relationship to the archive closeout wave

| Archive item | Revival role |
|---|---|
| #1278 exit criteria | becomes the v0.1 ship gate (amend, don't discard) |
| #1271 dogfooding | unchanged — the acceptance test for either path |
| #1272 dossier truth, #1274 Paper coverage, #1275 CI right-sizing, #1276 dead surface, #1173 branch protection | unchanged — prerequisites for strangers |
| #1139/#1123 run path | resolved by Phase 1 item 5 (tag + GHCR) |
| #1269 two-tier gate / severity bar / no-new-backend-surface | keep the tiering; **amend** the no-new-backend-surface rule with a scoped exception for the Phase-2 transcript engine |
| #1270 backlog triage | still worth the hour; de-scoped GTM trackers (#544/#546/#550…) become *relevant again* — retriage rather than close |
| #219 (voice capture) | superseded by Phase 2b (WhisperX sidecar, not browser speech API) |

## 7. Not verified / residual risk

- Market pricing figures and the 44%/73% statistics come from vendor-adjacent blogs and surveys; corroborated but not primary-verified. The deep-research synthesis/vote phase did not complete (workflow stopped early to save usage); GitHub-traction and license claims WERE live-verified.
- The completeness-critic pass of the code review did not run; the seven dimensions completed and ~25 claims were verified, but no independent agent audited this synthesis for gaps.
- Effort estimates are session-count analogies to the Paper-flip arc, not measured.
- The biggest unverified assumption is personal: **whether the maintainer wants to run a public project** (support burden, issue triage, security response). No analysis can answer that; ≥10 days of dogfooding plus the Phase-1 sprint is the cheapest way to find out.
