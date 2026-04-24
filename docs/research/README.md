# Taskdeck Research Dossier

This folder bundles everything you need to walk into a deep-research session about how to make Taskdeck "really what it should be".

It's structured for two audiences:

1. **You (the maintainer)**, to brief yourself before you sit down to research.
2. **A deep-research LLM** (Claude / GPT / Perplexity / Gemini deep-research mode), to be pasted in as context.

The intent is *not* to prescribe the next roadmap. It's to make sure your research has the strongest possible starting position: an honest read of the project, its thesis, its delivered substrate, and its real gaps.

---

## Files

| File | Purpose |
|---|---|
| [`RESEARCH_BRIEF.md`](RESEARCH_BRIEF.md) | **The main artefact.** A self-contained prompt you paste into a deep-research tool. Frames the question, includes constraints, lists 7 research axes, specifies the deliverable. |
| [`AUDIT.md`](AUDIT.md) | Reconciliation of "what Taskdeck is supposed to be" vs "what it actually is" vs "where it's going". One-page-ish synthesis of the strategic picture. |
| [`LIMITATIONS.md`](LIMITATIONS.md) | Concrete, severity-tagged gap inventory across capture / NLP / proposals / automation / memory / agent / UX / collab / integrations / observability. ~50 entries with sources and user-facing impact lines. |
| [`IDEAS_SEED.md`](IDEAS_SEED.md) | Candidate-pool of techniques and technologies (LLM structured output, embedding stacks, voice intake, agent frameworks, command-first UI patterns, local inference). Unfiltered — meant to widen the research, not constrain it. |
| [`_scratch/backend-map.md`](_scratch/backend-map.md) | What the backend actually does today: capture, intent, planner, executor, tool-calling, MCP, agent substrate. Source-grounded. |
| [`_scratch/frontend-map.md`](_scratch/frontend-map.md) | What the frontend actually shows today: tabs, modes, capture flow, review surface, intelligent surfaces (and where they aren't surfaced). |
| [`_scratch/limitations.md`](_scratch/limitations.md) | Raw agent-generated limitations summary (input to the synthesised LIMITATIONS.md). |

The `_scratch/` files are working notes — primarily input to the synthesis docs. The four top-level files (BRIEF / AUDIT / LIMITATIONS / IDEAS) are the deliverables.

---

## How to use this in a deep-research session

### Minimal flow (10 minutes)

1. Skim `AUDIT.md` to internalise the thesis ↔ reality picture.
2. Skim `LIMITATIONS.md` so you know the gap shape.
3. Paste the `## Prompt to the research agent` block from `RESEARCH_BRIEF.md` into your deep-research tool.
4. If your tool supports attachments, also include `AUDIT.md`, `LIMITATIONS.md`, and `IDEAS_SEED.md`.
5. Critique the response: does it respect GP-06 (review-first) and the local-first invariant? Does it include concrete tech with licences? Does it propose a phased plan?

### Heavier flow (a few hours)

1. Read `AUDIT.md`, `LIMITATIONS.md`, `IDEAS_SEED.md` end-to-end.
2. Read also: `docs/STATUS.md` (Project Summary + Current Implementation Snapshot), `docs/IMPLEMENTATION_MASTERPLAN.md` (Roadmap by Horizon + Chat-to-Proposal NLP Gap), `docs/GOLDEN_PRINCIPLES.md`, `docs/InReview/HUMAN/01_PRODUCT_THESIS.md`, `docs/InReview/HUMAN/02_MARKET_AND_VALUE.md`.
3. Customise `RESEARCH_BRIEF.md` — narrow or broaden the 7 axes to match what you most want to investigate.
4. Run the brief through *two* tools (e.g. one Claude deep-research and one Perplexity deep-research) and compare. Bias and gaps in the literature differ.
5. Iterate — pick one axis, deepen it, propose 1–2 spike experiments, then come back to the others.

### Don't do these

- Don't let the research output bypass `GP-06` (review-first). Anything that proposes silent automation should be flagged.
- Don't accept tech recommendations without licence/maturity check.
- Don't conflate engineering investments (CI, infra) with product investments (capture quality, intent layer). The user complaint is about product, not engineering.
- Don't treat "we built it" as "users use it" — there are still zero external users.

---

## Key insight to keep in mind

The maintainer's complaint that *"it's just a mediocre Trello board with way too many tabs and things happening"* is a precise diagnosis. Taskdeck has shipped:

- the **substrate** for an intelligent execution workspace (Clean Architecture, proposal lifecycle, tool-calling orchestrator, MCP server, agent registry, knowledge entities, capture pipeline, premium UI primitives);
- but **not the intelligence layer that makes it feel different from a Trello clone** (intent extraction is regex, knowledge isn't wired into capture, no semantic memory, no personalisation, no streaming, agent runtime is partial).

Closing that gap is what the deep research session is for. Everything in this folder exists to make the gap diagnosable in 10 minutes and addressable with concrete, trust-respecting techniques in the next 3–12 months.
