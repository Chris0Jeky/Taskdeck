# Risks, Non-Goals, and Decision Rules

## Primary risks

### Risk 1 — surface sprawl outruns user understanding

Symptom:
- more pages exist, but core value is still unclear

Mitigation:
- no new major surface without a clear golden-path reason
- require a “why would a novice ever use this?” answer

### Risk 2 — agent ambitions outrun trust model

Symptom:
- agents can do a lot, but users do not understand what happened

Mitigation:
- run entities and traces before broad automation
- keep proposal-first as default
- do not expose chain-of-thought as explanation

### Risk 3 — domain churn breaks the working core

Symptom:
- pressure to introduce separate task/project/subproject objects too early

Mitigation:
- keep board/card model stable until real evidence requires change

### Risk 4 — overbuilding knowledge infrastructure too early

Symptom:
- adding embeddings/vector services before the basic note/doc layer is useful

Mitigation:
- start with SQLite FTS and metadata filters

### Risk 5 — docs and product drift apart again

Symptom:
- docs explain workflows the UI does not support clearly

Mitigation:
- tie canonical manual sections to actual top-level navigation
- update docs whenever Home/Today/Review/Agents meaning changes

## Non-goals for the next major cycle

- replacing boards/cards with a new planning model
- shipping silent destructive autonomy
- turning Queue into the main end-user surface
- building a generalized app platform before first-run UX is fixed
- adding external vector infrastructure as a requirement

## Decision rules

### Rule A
If a feature makes demos better but makes the product harder to understand, it is not done.

### Rule B
If a feature needs internal IDs in the happy path, it is not novice-ready.

### Rule C
If a page is empty and offers no next step, it is incomplete.

### Rule D
If an agent action cannot be traced or linked to a proposal/artifact, it is not ready.

### Rule E
If a new concept cannot be explained in one sentence on its own page, it should not be top-level navigation yet.
