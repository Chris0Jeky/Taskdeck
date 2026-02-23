# 04 — Guardrails and Quality Gates Plan (CI + Repo Discipline)

Date: 2026-02-23  
Goal: add **mechanical enforcement** only when it reduces real cost or risk.

---

## 1) Coverage: do it in two phases

### Phase 1 — collect & publish coverage (no gates)
**Backend**
- Add coverage collection to CI for:
  - `Taskdeck.Domain.Tests`
  - `Taskdeck.Application.Tests`
- Upload artifacts: cobertura + html report.

**Frontend**
- Add a separate workflow (schedule or manual dispatch):
  - `npm run test -- --coverage` (or `npx vitest --run --coverage`)
- Upload coverage artifacts.

This gives you visibility without blocking velocity.

### Phase 2 — minimal thresholds (soft gates)
Start with:
- global thresholds (low-ish)
- file-pattern thresholds for the riskiest areas:
  - validation & authz
  - manifest parsing
  - import/export guards

Use “ratchet” strategy:
- thresholds only go up over time (never down).

---

## 2) OpenAPI drift detection (contract guardrail)

### Option A (lightweight): generate & validate only
- Generate OpenAPI JSON from the API in CI.
- Validate it parses and includes expected endpoints.

### Option B (stronger): snapshot + diff
- Commit `docs/generated/openapi.json`
- CI regenerates and diffs.
- Breaking changes become explicit PR diffs.

**Note:** keep snapshots stable (ordering, formatting) to reduce noise.

---

## 3) E2E stability guardrails

### Guardrail: “No flake without a ticket”
- Any flaky E2E test must:
  - be quarantined OR fixed immediately
  - have an issue link in the test file header comment

### Guardrail: central timeouts
- Don’t sprinkle custom timeouts.
- Prefer `expect.poll` / `toBeVisible` with consistent timeouts.

### Guardrail: stable selectors policy
- Every interactive element that matters must have:
  - role + label, or
  - `data-action` / `data-testid`

---

## 4) Architecture guardrails upgrades (recommended)

### Backend
Current architecture test checks project references only.
Upgrade options:
- add namespace-based rules (e.g., `Taskdeck.Domain` cannot reference `Taskdeck.Infrastructure`)
- enforce controller rules:
  - controllers may not directly use DbContext
  - controllers should depend on Application services only

Libraries you can consider:
- NetArchTest (simple)
- ArchUnitNET (more expressive)

### Frontend
Add a simple check script (Node) that fails CI if:
- `fetch(` or `axios` is used outside `src/api/`
- raw absolute API URLs appear outside config

This is easy to implement via ripgrep + allowlist.

---

## 5) Security-adjacent guardrails that are cheap
(Not “testing” per se, but practical.)

- `dotnet list package --vulnerable` in a scheduled workflow
- `npm audit` in scheduled workflow
- enable Dependabot/Renovate (if not already)
- secret scanning (GitHub defaults are decent, but add gitleaks if you want local)

Keep these as non-blocking at first.

---

## 6) Documentation drift guardrail upgrades
You already have docs governance scripts. Extend them gradually.

Good next checks:
- require `Last Updated:` line in additional key docs
- require that `docs/TESTING_GUIDE.md` references all test projects
- require that `docs/INDEX.md` links to new analysis docs when added

---

## 7) Suggested new workflows
- `ci-required.yml` stays as PR gate
- add:
  - `nightly-quality.yml` (schedule): coverage + security audits + mutation sample + openapi snapshot refresh suggestion
  - `harness-gc.yml` (schedule): doc gardening checks + “golden principles” lint + opens issues with findings

(Opening issues from Actions requires a token; decide later.)
