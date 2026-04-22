# ADR-0031: SAST Scanning with Semgrep

**Status:** Accepted  
**Date:** 2026-04-22

## Context

Security audit finding (issue #870): No static application security testing (SAST) was running in CI. Security issues were found manually rather than automatically. The codebase spans C# (.NET 8) and TypeScript/Vue, requiring a polyglot SAST tool.

## Decision

Adopt **Semgrep** as the SAST engine for Taskdeck CI, with both registry rules and custom project-specific rules.

### Configuration

- **Registry rulesets**: `p/csharp`, `p/typescript`, `p/jwt` -- maintained by the Semgrep community, covering common vulnerability patterns (SQL injection, XSS, insecure crypto, JWT misuse).
- **Custom rules** (`.semgrep/`): Taskdeck-specific patterns that enforce project conventions and architecture invariants:
  - Missing `[Authorize]` on controllers (CWE-862)
  - Raw SQL string interpolation in EF Core (CWE-89)
  - Logging sensitive data (CWE-532, ADR-0016)
  - Domain-layer referencing Infrastructure (ADR-0001)
  - Hardcoded connection strings (CWE-798)
  - innerHTML/document.write usage (CWE-79)
  - Raw localStorage token storage outside sessionStore (ADR-0009)
  - Disabled security-relevant ESLint rules (CWE-95)

### CI Integration

- Runs in the **ci-extended** lane (triggered by `security` label) and **ci-nightly** (unconditionally).
- **Non-blocking initially** (advisory mode): findings are reported as CI step summaries and artifacts but do not fail the build.
- **Path to enforcement**: The `enforce-findings` input can be set to `true` to gate on ERROR-level findings. Plan to enable after baseline triage.
- Uses the `semgrep/semgrep:latest` container image for reproducible scanning.
- Results summarized by `scripts/ci/summarize-sast-findings.mjs` which produces both Markdown (GitHub step summary) and JSON (machine-readable) output.

### Workflow Topology

Follows the reusable workflow pattern established in ADR-0013:
- `reusable-sast-scanning.yml` -- self-contained reusable workflow
- Called from `ci-extended.yml` and `ci-nightly.yml`

## Alternatives

| Alternative | Why Not |
|---|---|
| **CodeQL** | GitHub-native but slower (~15 min for C#), requires compilation step, limited TypeScript rule library for custom patterns. |
| **SonarQube/SonarCloud** | Heavier infrastructure (server-based), more suited to large enterprises. Overkill for current scale. |
| **Roslyn Analyzers only** | C#-only. Does not cover TypeScript/Vue. Good complement but not sufficient alone. |
| **ESLint security plugins only** | TypeScript-only. Does not cover C#. Already used for lint but not SAST-grade. |

Semgrep was chosen for: polyglot support (C# + TypeScript in one tool), fast scanning (<5 min typical), no compilation required, easy custom rule authoring (YAML), strong community ruleset, and free for CI usage.

## Consequences

- **Positive**: Automated detection of common vulnerability patterns before merge. Custom rules enforce Taskdeck-specific architectural invariants that would otherwise require manual review. Clear path to enforcement gating.
- **Negative**: False positives are possible, especially for pattern-matching rules. The team must triage initial findings and tune rules. Semgrep container image adds ~1 min to job startup.
- **Operational**: `.semgrep/` directory becomes the home for custom rules. `.semgrepignore` controls scan scope. New rules should be added as security patterns are identified.

## References

- Issue: #870 (CI-01: Add SAST scanning to CI)
- ADR-0013: CI Topology - Reusable Workflow Decomposition
- ADR-0001: Clean Architecture Layering
- ADR-0009: Session Token Storage
- ADR-0016: Security Logging Redaction
- Semgrep documentation: https://semgrep.dev/docs/
