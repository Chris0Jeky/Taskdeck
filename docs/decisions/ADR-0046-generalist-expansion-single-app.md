# ADR-0046: Generalist Expansion — Artefact Intake and Dossiers in the Single App (No Twin Fork)

- Status: Proposed
- Date: 2026-07-13
- Deciders: Maintainer (Chris)
- Related: ADR-0044 (revival pivot — the plan this amends), ADR-0045 (LLM transcript triage engine, PR #1312), ADR-0008 (novice-first product legibility before breadth — the standing precedent this extends), `docs/REVIVAL_PLAN.md` §Phase 4, tracker `#1327` (GEN-00), decision gate `#1326` (GEN-12)

## Context

On 2026-07-13 the maintainer evaluated two related proposals:

1. An external research handoff ("Artefact Intake and Project Dossiers", derived from a Reddit personal work-OS build) recommending Taskdeck gain a constrained **artefact intake → structured extraction → reviewable proposal → project dossier** workflow rather than recreating a general-purpose project-management application.
2. The maintainer's own idea to go further: **abstract a chunk of Taskdeck and build a twin application** — a simpler, generalist "big brother" targeting non-technical users (friends and family as the first dogfooding audience), on the reasoning that even engineers may prefer a generalist framing.

A same-day capability recon (11 questions, file-level verification) established the load-bearing facts:

- **No artefact/attachment/blob model exists anywhere** — the intake foundation is greenfield, but `PaperCaptureComposer.vue` already ships a deliberately inert drag-drop zone waiting for exactly this pipeline.
- **The LLM extraction seam already exists or is in flight**: REVIVAL-08 M1 (PR #1312) builds transcript triage with a strict output schema and deterministic fallback; REVIVAL-09 (#1305) builds evidence spans. Artefact intake is a *generalization* of that lane, not a new pipeline.
- **The proposal apply vocabulary cannot apply what extraction will propose**: `UpdateCardAsync` maps only title/description — no operation sets a due date, so REVIVAL-08 M3's `dueDateHint` would be extracted but unappliable (closed by GEN-05 `#1319`).
- **Prompt injection from untrusted content is essentially undefended** beyond the strict output schema and the human review gate (closed by GEN-09 `#1323`).
- **The generalist scaffold already exists**: workspace modes (`guided | workbench | agent`), a first-run onboarding model, and the Paper shell's calm language. What reads as "developer product" is the default navigation (Ops CLI, Endpoints, Logs, Dev Tools, API Keys, Agents, Metrics) and jargon — a **visibility + language problem, not an architecture problem**.
- The review-first proposal machinery, provenance chain, import dry-run/apply pattern, egress disclosure, and quota/kill-switch controls are all reusable as-is.

The governing constraints: ADR-0044's finite-work discipline (a ratified wave list is the admission ticket; scope regrowth is the named failure mode), solo-maintainer capacity, the MIT/local-first commitments, and the ~8-week revival checkpoint.

## Decision

1. **Single app.** Taskdeck extends into generalist reach as one product. The "twin" generalist application is **not built now**. The idea is preserved behind an explicit decision gate (GEN-12 `#1326`) with evidence criteria (non-technical retention, structural divergence of demands, legibility ceiling) reviewed at the revival checkpoint. If the gate ever opens, the options ladder is: edition/profile flag → separately-branded distribution of the same codebase → true fork (last resort, own ADR).
2. **A Phase 4 wave ("Every Artefact, Everyone", v0.4) is appended to `docs/REVIVAL_PLAN.md`** — tracker GEN-00 `#1327`, children GEN-01..12 `#1315`–`#1326` — strictly subordinate to the v0.1 ship gate: GEN work interleaves only where parallel-safe (lane G-A), waits for the transcript engine where it shares the seam (lane G-B), and follows Phase 1 for the rest (lane G-C).
3. **Architecture boundaries (binding for the wave):**
   - **Artefact** — immutable stored source (screenshot/PDF/file/pasted text) with provenance; never edited.
   - **Extraction** — a recorded interpretation of an artefact (extractor/model, version, warnings). *Extraction is not mutation*: no extraction result touches domain state.
   - **Proposal** — extraction output flows into the existing review-first proposal machinery. No new privileged mutation path; no second task representation.
   - **Domain state** — approved cards/boards remain the only execution truth.
   - **Dossier** — a derived read model over approved state + linked artefacts; pending interpretations are visibly provisional or absent. No new persisted dossier entity without evidence.
4. **Storage:** artefact blobs live **in SQLite** (separate blob table, per-artefact cap default 10 MB, per-user quota default 200 MB) to preserve the "your entire workspace is a single SQLite file you own" guarantee end-to-end (backup, export, GDPR delete). Revisit only on demonstrated bloat evidence from dogfooding.
5. **Image extraction at MVP is consent-gated cloud multimodal** (OpenAI/Gemini vision through the existing provider policy, quotas, circuit breakers, kill-switch, and egress disclosure), with an honest not-extracted state when consent is absent and a deterministic fallback discipline identical to REVIVAL-08 M1. Local OCR (Tesseract-class native dependencies) is rejected at MVP; Ollama vision is an optional follow-up. Image bytes never leave the device without explicit consent naming the destination.
6. **Generalist reach is delivered inside the existing scaffold:** mode-scoped navigation (guided mode hides developer surfaces behind one "Advanced" reveal), a plain-language pass with maintainer sign-off on contested renames, and guided-first defaults for new users (existing users untouched). Visibility and copy only — no feature removal.
7. **Friends & family become the generalist acceptance test** (GEN-11 `#1325`): an InviteOnly or desktop-exe channel with a non-GitHub feedback path, feeding the checkpoint the same way #1271 dogfooding does.

## Alternatives Considered

- **Build the twin generalist app now (the maintainer's initial framing).** Rejected: it duplicates the entire hard core (capture queue, proposal pipeline, provenance, review UX, Today, MCP, persistence — the recon confirmed these are the bulk of the product), splits solo-maintainer capacity before either product has traction, re-enters the unbounded-work failure mode the 2026-07-02 analysis diagnosed, and competes in the crowded generalist category (Notion/ClickUp/AppFlowy) while leaving behind the one differentiated asset — the review-first artefact-to-action pipeline. The maintainer's own observation ("even engineers could benefit from a more generalist approach") argues for one generalist-legible product, not two products.
- **Abstract-then-fork preemptively ("make the core reusable now, fork later").** Rejected as speculative scaffolding: the abstraction dividend only exists once a second consumer exists. Clean Architecture seams already keep a later extraction cheap; GEN-12 records when that evidence bar is met.
- **Recreate the Reddit-style general-purpose work OS inside Taskdeck.** Rejected per the handoff's own analysis (autonomous continuous management, background reinterpretation agents, a parallel task representation all contradict the review-first thesis). The MVP exclusions in the handoff are adopted as-is.
- **Local-OCR-first image intake.** Rejected at MVP: native dependency weight and packaging pain across the desktop/container run paths for a capability cloud vision provides behind consent gates already built for exactly this trust model. Revisit with Ollama vision as local-first maturity improves.
- **Filesystem blob store.** Rejected: breaks the single-file ownership story that REVIVAL_PLAN §2 leads marketing with; export/backup/GDPR paths would all need parallel handling.

## Consequences

- `docs/REVIVAL_PLAN.md` gains Phase 4, a v0.4 horizon row, §7 new-surface authorizations for GEN-01/02/03/04/05/07/08/10, and a friends-family signal at the checkpoint. The twin app, real undo, Postgres, and new connector types remain unauthorized.
- Prompt-injection rails (GEN-09) become a **merge prerequisite** for wiring artefact text into LLM prompts (GEN-04) — the review gate stops being the only defense layer before hostile documents flow in.
- The scope-regrowth risk named in REVIVAL_PLAN §9 grows: mitigated by lane subordination to the v0.1 gate, the unchanged ~8-week checkpoint, the GEN-12 gate, and this wave being seeded complete (admission ticket discipline — new GEN work requires a plan amendment, same as REVIVAL).
- The `.codex`/`.claude` active-gate snapshots are refreshed to the revival + generalist direction so overnight autonomous agents do not execute against the superseded archive framing.
- Ratification: this ADR ships as **Proposed**; the maintainer ratifies via the GEN-00 (`#1327`) checklist. Foundational lane G-A execution was authorized by maintainer instruction on 2026-07-13 pending ratification.

## References

- `docs/REVIVAL_PLAN.md` — the plan this amends (Phase 4)
- GEN-00 tracker `#1327`; children `#1315`–`#1326`
- 2026-07-13 capability recon (session record; load-bearing findings restated in Context above)
- External research handoff "Taskdeck Proposal: Artefact Intake and Project Dossiers" (2026-07-13 conversation) — adopted in narrowed form; its §5 MVP exclusions adopted verbatim
- `docs/COURSE_CORRECTION.md` §1.1 — the unbounded-work failure mode governing the twin-app deferral
