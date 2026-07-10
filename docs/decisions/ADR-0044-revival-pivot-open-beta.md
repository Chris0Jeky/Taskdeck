# ADR-0044: Revival Pivot — Open-Beta Distribution with a Commercial Horizon (Supersedes the Archive Pivot)

- Status: Accepted
- Date: 2026-07-10
- Deciders: Maintainer (Chris)
- Related: ADR-0038 (Paper canonical), ADR-0041 (desktop key auto-generation), `docs/REVIVAL_PLAN.md`, `docs/analysis/2026-07-10_revival_assessment.md`, tracker `#1278` (archive exit criteria — repurposed), REVIVAL-00 tracker

## Context

On 2026-06-13 the maintainer decided to finish Taskdeck for personal use and archive it (masterplan Direction section; closeout wave #1269–#1278). On 2026-07-10 the maintainer reopened that decision after successfully prototyping a WhisperX transcription + cheap-LLM pipeline outside the repo, and commissioned a two-track analysis (seven code-grounded dimension reviews with ~25 adversarially verified claims, plus market deep-research with live-verified competitor traction data — see `docs/analysis/2026-07-10_revival_assessment.md`).

The analysis found:

1. Taskdeck is **~80% of the back half of a meeting-intelligence product** (durable capture queue → risk-classified, evidence-carrying proposals → human-approved board apply, MCP write tools that structurally cannot self-approve) **with 0% of the front half** (no LLM anywhere in the capture path — triage is regex; no audio ingestion).
2. The front half (transcription, summaries) is commoditizing and crowded (Fireflies/Otter/Granola per-seat SaaS; Meetily 22.6k★, Screenpipe 19.7k★, Hyprnote on the OSS side). The back half — "action items that actually go somewhere, behind a human trust gate" — is the market's documented failure mode and verifiably **nobody's core product** (the OSS privacy leader Meetily stops at transcription + summarization).
3. Human-in-the-loop approval of agent actions has a regulatory tailwind (EU AI Act Art. 14 enforceable 2026-08-02) and is where enterprise reference patterns converge, while incumbent MCP surfaces are zero-touch.
4. Most pre-public work (honesty de-stubs, coverage re-point, CI right-sizing, run-path validation) is **already on the archive exit-criteria list** — the marginal cost of a revival attempt over a clean archive is small.

The maintainer's clarified distribution intent: **"free" means a wide-open free beta** for genuine use, adoption, feedback, and exposure — while the maintainer develops the commercial side. It does not mean free-forever-everything.

## Decision

1. **The archive pivot is superseded.** Taskdeck's direction is now: **revive and ship as a free open beta**, positioned as *the local-first, review-first action-item engine* — transcripts/notes in (from any source, including local WhisperX pipelines), evidence-linked proposals out, applied to a board only on human approval — with the write-gated MCP server as the developer-facing second act.
2. **Distribution posture: open beta first, monetization later.** The beta is free and public (self-hosted Docker/GHCR image + self-contained desktop exe + hosted static demo). Its purpose is adoption, feedback, and exposure. Commercial packaging (hosted tier, team/Pro features, support) is developed in parallel by the maintainer and introduced only after the beta demonstrates value; specifics will be decided in a follow-up ADR informed by `docs/REVIVAL_PLAN.md` §Business Posture.
3. **No license rug-pull.** Everything shipped in this repository under MIT **stays MIT**. Future commercial value is added *around* the core (hosted service, optional premium modules, support), never by retroactively relicensing what beta users adopted. This is a trust commitment made *before* launch, informed directly by the June 2026 Screenpipe MIT→commercial backlash precedent.
4. **The archive exit criteria become the v0.1 ship gate.** Tracker #1278's criteria (honest surfaces, coverage re-point, one validated run path, zero always-red CI lanes, branch protection) are repurposed as the bar for the first public artifact, with revival-specific additions (registration gating, fake-undo removal) tracked in the REVIVAL wave. The finite-work discipline from `COURSE_CORRECTION.md` carries over: **a revival without a ratified, finite ship gate would repeat the unbounded-work failure mode the archive analysis diagnosed.**
5. **Scoped exception to the no-new-backend-surface rule (#1269):** the Phase-2 transcript engine (LLM-backed triage strategy, Transcript entity, evidence spans, OpenAI-compatible provider, risk-tiered auto-apply) and the Phase-1 registration gate and beta feedback channel are authorized new surface. Everything else stays under the rule.
6. **Checkpoint (not kill) criteria:** after Phase 2 ships and the beta launches (~8 weeks at demonstrated velocity), the maintainer re-evaluates against adoption/feedback signals. If the beta shows no traction and personal dogfooding has not stuck, fall back to the archive plan — which remains ~90% intact because Phase 1 *is* its exit criteria.

## Alternatives Considered

- **Proceed with the archive (status quo).** Rejected by the maintainer after the analysis showed an unoccupied, on-thesis market seam and a low marginal cost to attempt the revival.
- **Compete head-on as an open-source meeting notetaker** (audio capture, diarization, live transcription — the Meetily/Hyprnote lane). Rejected: it requires building the crowded, commoditizing front half from scratch, solo, against funded teams; and the analysis verified the incumbent OSS capture tools are weakest exactly where Taskdeck is strongest (the reviewed follow-through loop).
- **Free-forever, donations-only OSS.** Rejected: the maintainer intends a commercial side; deciding the posture *now* (open beta + core-stays-MIT + commercial around the core) is the lowest-regret path — cheap before adoption, expensive to change after.
- **Immediate commercial launch (paid from day one).** Rejected: adoption/feedback/exposure are the scarce resources for a solo-maintained unknown product; a paid gate before product-market signal would suppress all three.

## Consequences

- `docs/REVIVAL_PLAN.md` becomes the active planning spine (waves, ship gate, issue map); the masterplan Direction section is amended to point at it.
- The de-scoped distribution-era trackers (GTM `#544`/`#546`/`#550`, packaging `#532`, cloud `#537`/`#548`, code-signing `#1167`) are **re-triaged rather than closed** — some become relevant again under the beta framing.
- The archive-closeout issues (#1269–#1278) are re-scoped in place: same work, new purpose (ship gate instead of archive gate). Dogfooding (#1271) remains the acceptance test for either path.
- New public-exposure obligations attach before launch: registration gating, branch protection, removal of dishonest surfaces (fake undo window, fabricated Today dossier), and a privacy-respecting feedback channel — all tracked in the REVIVAL wave.
- Risk accepted: solo-maintainer support burden of a public beta; mitigated by the checkpoint criteria and by keeping the archive plan intact as the fallback.

## References

- `docs/analysis/2026-07-10_revival_assessment.md` — full evidence base (code + market, with verification verdicts)
- `docs/REVIVAL_PLAN.md` — the plan this ADR authorizes
- `docs/COURSE_CORRECTION.md` §1.1 — the unbounded-work failure mode the ship gate guards against
- Screenpipe license change (June 2026) and community reaction — the rug-pull precedent behind Decision 3
