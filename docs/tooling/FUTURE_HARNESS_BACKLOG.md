# Taskdeck - Future Harness Backlog (Deferred Enhancements)

**Purpose:** A do later list of optional tools, settings, harness ideas, and workflows we discussed but are *not required right now* (to keep agility high).  
**Use:** Keep this file in `docs/` and periodically cherry-pick items into your Execution Board.
Last Updated: 2026-02-23

---

## 0) Guiding principle for when to pull items forward
Promote an item from this backlog **only if** it:
- removes repeated friction (you keep running into the same failure mode), or
- reduces high-cost risk (security, data loss, privacy, release confidence), or
- enables faster iteration (cuts debug time, reduces CI time, improves signal).

## Status Legend
- `Delivered`: implemented and merged.
- `Seeded`: issue exists and is currently open.
- `Deferred`: not yet seeded as an issue.

## Recent Promotion (2026-02-23 testing-harness pack)
- New testing-harness wave tracker and child issues seeded: `#254` to `#260`.
- Existing issue seeds updated with extracted knowledge instead of duplicating:
  - `#89` (property/fuzz target surfaces)
  - `#90` (mutation scheduling posture)
  - `#106` (dependency/security signal commands and cadence)
  - `#168` (CI topology routing for OpenAPI/nightly-quality lanes)

---

# A) Optional MCP servers / tool integrations

## A1) Chrome DevTools MCP (or CDP integration)
**Why:** faster UI debugging (console/network/perf/layout) than E2E alone.  
**Pull forward when:** UI bugs are hard to understand with Playwright screenshots alone.

## A2) Observability MCP (logs/metrics/traces queryable by the agent)
**Why:** agent can reason from real runtime signals, not guesses.  
**Pull forward when:** you spend time chasing why is this slow/broken without good telemetry.

**Paths:**
- Lite: structured logs + local query scripts
- Full: Loki/Prometheus/Tempo (LogQL/PromQL/TraceQL)

## A3) Error tracking MCP (e.g., Sentry)
**Why:** automatic grouping + stack traces + regressions; agent can triage and open issues.  
**Pull forward when:** real users / long sessions produce bugs you can't reproduce quickly.

## A4) Dependency/security maintenance automation
**Why:** routine upgrades and security alerts become mechanical.  
**Options:** Dependabot + Renovate + SBOM generation; optionally MCP-assisted triage.  
**Pull forward when:** packages start drifting or security alerts become noisy.

## A5) DB introspection MCP (read-only)
**Why:** agent can inspect schema/fixtures quickly.  
**Pull forward when:** you frequently debug schema/migration issues.

**Safety:** read-only credentials, tool allowlist, no destructive commands.

---

# B) MCP configuration hardening (recommended later)

## B1) Split GitHub tokens: readonly vs write
**Goal:** day-to-day agent runs use readonly; elevated token only used intentionally.

## B2) Tool allowlists
**Goal:** reduce blast radius by restricting which tools are callable per MCP server.

## B3) Write is explicit policy
**Goal:** agent only performs GitHub write operations when the prompt explicitly authorizes it.

---

# C) Harness engineering improvements (repo-local, compounding)

## C1) Structural architecture checks
**Goal:** make Clean Architecture boundaries mechanical (CI failure on violations).

## C2) Docs governance CI job
**Goal:** doc drift becomes an observable failure (missing cross-links, missing last-updated).

## C3) Golden principles doc + lint
**Goal:** 5-10 rules that never change (naming, layering, auth posture, error contract) + checks.
**Status:** Seeded via `#259`.

## C4) Benchmark guardrails
**Goal:** simple performance checks for hot paths (log query, automation execution).  
**Pull forward when:** performance regressions become a time sink.

## C5) Flake quarantine policy (E2E)
**Goal:** separate flaky test maintenance from product bugs.  
**Includes:** quarantine label, rerun strategy, stabilize selectors/timeouts.

## C6) Property-based / fuzz testing for risky parsers/inputs
**Pull forward when:** you introduce manifest/import formats or complex parsing.

---

# D) Delivery workflow upgrades

## D1) Required CI checks as branch protections
**Goal:** merges require green CI (backend/unit + integration + frontend build/typecheck + E2E smoke).

## D2) Release discipline upgrades
**Includes:**
- changelog conventions
- tag/version rules
- RC checklist enforced (manual checklist + green CI + docs updated)

## D3) Automated Execution Board sync
**Goal:** turn Masterplan items into issues automatically; keep a project board tidy.

## D4) Non-blocking nightly quality workflow
**Goal:** run coverage artifact collection + dependency/security signals on schedule/manual without blocking PR-required CI.  
**Status:** Seeded via `#260`.

---

# E) Ways of thinking you'll reuse (keep sharp)

## E1) Correctness Map (zones)
- Zone A: security/identity + access control
- Zone B: data invariants + migrations + recovery
- Zone C: automation safety + deterministic executor
- Zone D: operability (logs/diagnostics) + release safety

## E2) Contract-first boundaries
UI -> API -> Domain -> DB as contracts; enforce with types, validation, tests.

## E3) Mechanical invariants over prose
If it matters, encode it as:
- tests
- CI checks
- linters/structural assertions
not guidelines someone might forget.

## E4) Promote friction to tooling
Repeated confusion -> add a doc pointer, script, template, or check.

---

# F) Optional nice-to-have additions

- Accessibility audit pass (keyboard + contrast + screen-reader labels)
- Threat model doc (assets, threats, controls, detection) updated quarterly
- Backup/restore posture doc + recovery drills
- SBOM generation for releases
- One command dev bootstrap (`./dev.ps1` / `./dev.sh`) for consistency
