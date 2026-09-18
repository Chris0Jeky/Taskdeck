# Documentation Link Parser Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the parser correctness and complexity residuals in GitHub issue #3119 without changing docs-governance workflow ownership.

**Architecture:** Keep `scripts/check-doc-links.mjs` dependency-free and offset-preserving. Replace repeated suffix scans with one-pass indexes, parse HTML attributes outside quoted values, recover from unterminated comments at a blank-line boundary while emitting a diagnostic, and recognise Markdown reference definitions after blockquote/list container prefixes.

**Tech Stack:** Node.js 24 ESM, `node:test`, synchronous fixture filesystem APIs used by the existing checker tests.

**Spec:** GitHub issue #3119.

## Global Constraints

- Preserve case-exact path checking on Windows, macOS, and Linux.
- Preserve public exports and existing diagnostic object shape: `{ line, target, reason }`.
- Do not modify reusable docs-governance workflow wiring; issue #1138 owns that work.
- Do not add runtime dependencies.
- Keep malformed constructs fail-loud while continuing to inspect unrelated later paragraphs.

---

### Task 1: Add public-path regressions

**Files:**
- Create: `scripts/check-doc-links.residuals.test.mjs`

**Interfaces:**
- Consumes: `findBrokenLinks(root)`, `findMaskingDiagnostics(root)`, and `extractLocalTargets(markdown)` from `scripts/check-doc-links.mjs`.
- Produces: causal tests for every #3119 parser residual except workflow wiring.

- [ ] **Step 1: Add fixture tests for HTML recovery and attribute isolation**

Create temporary Markdown fixtures proving that `2 < 3 <a href="./gone.md">` reports `./gone.md`, while `href=` text inside a different quoted attribute does not create a target.

- [ ] **Step 2: Add the unterminated-comment recovery test**

Use `<!-- unfinished\n\n[bad](./gone.md)` and assert both an `unterminated HTML comment` diagnostic on line 1 and a missing-link result on line 3.

- [ ] **Step 3: Add nested-reference tests**

Assert that blockquote, unordered-list, ordered-list, and nested blockquote/list definitions contribute their actual local destinations.

- [ ] **Step 4: Add adversarial complexity guards**

Generate increasing unmatched backtick runs plus a large unmatched-label suffix. Assert extraction completes within 5 seconds on the supported Node runtime and returns no targets.

- [ ] **Step 5: Run the new test file and verify RED**

Run: `node --test scripts/check-doc-links.residuals.test.mjs`

Expected before implementation: HTML recovery, quoted-attribute isolation, comment recovery, nested definitions, or the complexity deadline fails.

### Task 2: Make delimiter discovery linear

**Files:**
- Modify: `scripts/check-doc-links.mjs`
- Test: `scripts/check-doc-links.residuals.test.mjs`

**Interfaces:**
- Produces: `indexBalancedLabels(text): Map<number, number>` and a one-pass backtick-run index carrying start, end, length, line, paragraph, and next equal-length delimiter.

- [ ] **Step 1: Pre-index balanced link labels**

Scan escaped brackets once with a stack and replace repeated `findLabelEnd` suffix scans with constant-time map lookups.

- [ ] **Step 2: Pre-index backtick runs by paragraph and length**

Tokenise each run once, calculate line/paragraph identity during the scan, and reverse-link each token to the next same-length token in its paragraph.

- [ ] **Step 3: Preserve greedy code-span semantics**

Process tokens in source order, mask through the next equal-length delimiter, skip enclosed tokens, and emit one diagnostic for unmatched unescaped openers without rescanning the suffix.

- [ ] **Step 4: Run focused tests**

Run: `node --test scripts/check-doc-links.test.mjs scripts/check-doc-links.residuals.test.mjs`

Expected: existing masking tests and the adversarial deadline pass.

### Task 3: Parse HTML tags and attributes structurally

**Files:**
- Modify: `scripts/check-doc-links.mjs`
- Test: `scripts/check-doc-links.html-quoted-attributes.test.mjs`
- Test: `scripts/check-doc-links.residuals.test.mjs`

**Interfaces:**
- Produces: a small attribute scanner returning `{ value, valueOffset } | null` for `href` or `src`.

- [ ] **Step 1: Reject non-tag `<` characters before scanning for `>`**

Only invoke quote-aware tag-end scanning when the source begins an `<a` or `<img` opening tag with a name boundary; otherwise advance one character.

- [ ] **Step 2: Scan attributes outside quoted values**

Walk names, optional `=`, and quoted/unquoted values from the end of the tag name. Compare attribute names case-insensitively and return the exact value offset.

- [ ] **Step 3: Run HTML-focused tests**

Run: `node --test scripts/check-doc-links.html-quoted-attributes.test.mjs scripts/check-doc-links.residuals.test.mjs`

Expected: all valid attributes are found, embedded `href=`/`src=` prose is ignored, and literal `<` no longer hides a later tag.

### Task 4: Bound malformed comments and recognise container definitions

**Files:**
- Modify: `scripts/check-doc-links.mjs`
- Test: `scripts/check-doc-links.residuals.test.mjs`

**Interfaces:**
- Produces: comment diagnostics through the existing `maskCodeWithDiagnostics` result and container-prefix stripping for reference-definition lines.

- [ ] **Step 1: Bound an unterminated HTML comment**

When `-->` is absent, mask only through the next blank-line boundary (or EOF), emit `{ line, target: '<!--', reason: 'unterminated HTML comment' }`, and continue scanning later paragraphs.

- [ ] **Step 2: Parse reference definitions line by line**

Strip up to three spaces plus repeated blockquote/list markers before matching `[label]: destination`; apply the same container stripping to continuation destinations.

- [ ] **Step 3: Run all link-checker tests**

Run: `node --test scripts/check-doc-links*.test.mjs`

Expected: all prior and new tests pass.

### Task 5: Verify the repository contract

**Files:**
- Modify only if a test exposes a defect: `scripts/check-doc-links.mjs`

**Interfaces:**
- Produces: exact-head verification evidence for review.

- [ ] **Step 1: Run the checker against the repository**

Run: `node scripts/check-doc-links.mjs`

Expected: zero broken repository-relative links; any historical malformed delimiter remains a warning with a bounded location.

- [ ] **Step 2: Check diff hygiene**

Run: `git diff --check`

Expected: no whitespace errors.

- [ ] **Step 3: Request review after exact-head CI**

Keep the pull request draft until focused tests and hosted required suites pass, then mark it ready to trigger Codex review and address every actionable finding.
