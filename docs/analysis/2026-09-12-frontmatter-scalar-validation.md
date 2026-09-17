# Frontmatter scalar validation: issue #3006

Last Updated: 2026-09-17

Status: PR candidate. Exact-head hosted qualification and human acceptance remain outstanding.

## Boundary and design

The control-path mirror is checked by `scripts/check-docs-governance.mjs` without installing a YAML dependency. The checker therefore implements a deliberately narrow, fail-closed subset rather than claiming general YAML conformance:

- top-level keys;
- single-line scalars;
- flat indented scalar lists;
- JSON-compatible double-quoted strings;
- YAML doubled-single-quote strings;
- comments and blank lines.

Unsupported tags, anchors, aliases, block scalars, flow collections, nested collections, malformed quoting, inconsistent indentation, and trailing content are rejected instead of approximated.

Quoted values are tracked separately from plain values. This distinction is required because a path such as `"null"` is a string, while plain `null` is resolved as a non-string by common YAML schemas. The path consumer rejects whole-scalar plain values that resolve as nulls, booleans, integers, floats, infinities, NaN, dates, or timestamps. Matching follows the resolver's exact spellings and is bounded to the complete scalar, so resolver strings such as `0XFF`, `+.nAn`, and `1e1_0`, plus ordinary paths such as `2026-09-17-notes.md`, `true/guide.md`, and `123/notes.md`, remain valid.

Every decoded internal control character in the C0, DEL, and C1 ranges is rejected. This applies equally to policy JSON strings, literal YAML characters, and escape sequences decoded from quoted YAML values. Leading or trailing Unicode whitespace is rejected separately rather than trimmed into an apparently matching control path.

Decoded unpaired UTF-16 surrogates are also rejected because a JSON escape can otherwise create a JavaScript string that a YAML loader refuses as invalid Unicode. Valid surrogate pairs and ordinary visible Unicode remain valid.

## Recovery audit

The original PR branch accumulated unrelated history and could no longer provide a reviewable three-file change. The clean replacement was created from current `main` and initially copied the three intended blobs. A second audit found that the source branch did not actually contain the final control-range and implicit-scalar changes claimed in its automation summary. The clean branch was corrected directly rather than carrying that overstatement forward.

The current replacement contains:

- `scripts/check-docs-governance.mjs`;
- the original governance regression suite;
- `scripts/check-docs-governance.hardening.test.mjs`, which isolates the missing fail-open cases;
- this evidence note.

No workflow, required context, dependency, deployment setting, policy document, or agent-control rule is changed.

## Verification

The implementation was developed regression-first. On test-only head `de09dcbb0bd9032adc87cf9f2e583ef46f52073b`, the focused suite ran 89 tests and failed four, including the stale whitespace diagnostic and resolver under- and overmatch counterexamples. The implementation was then corrected and the surrogate review finding was added as a separate red/green pair.

Exact-head verification on `b2263bdcdab1b7b01e032c2cfdbd68ba4e9e32ef` produced:

- `node --test scripts/check-docs-governance.test.mjs scripts/check-docs-governance.hardening.test.mjs`: **91 tests, 91 passed, 0 failed, 0 skipped, 0 cancelled, 0 todo**;
- exhaustive C0, DEL, and C1 rejection in policy JSON and decoded quoted YAML;
- rejection of first/last high and low unpaired-surrogate boundaries in both policy and quoted YAML, with valid non-BMP pairs preserved;
- resolver-exact null, boolean, integer, float, non-finite, date, and timestamp coverage, including under- and overmatch counterexamples;
- quoted implicit-scalar equivalents preserved as strings;
- visible Unicode and path-like counterexamples preserved;
- `node --check scripts/check-docs-governance.mjs`: passed;
- `node scripts/check-docs-governance.mjs`: `Docs governance check passed.`;
- `node scripts/check-doc-links.mjs`: `Doc link check passed (711 Markdown files, 0 broken relative links).`;
- `git diff --check`: passed;
- the exact head and working tree remained unchanged and clean before and after verification.

The current hosted Docs Governance job executes the checker against the repository policy/rule pair but does not yet invoke this Node regression suite. Wiring governance regressions into hosted CI remains separately owned by #3005; this PR does not alter a control-plane workflow to conceal that boundary.

Because this evidence note is itself a follow-up commit, the final branch head still requires hosted CI, a fresh exact-head code review, and a final changed-file inventory check before merge. There is no frontend, backend, browser, deployment, or hosted-product claim.
