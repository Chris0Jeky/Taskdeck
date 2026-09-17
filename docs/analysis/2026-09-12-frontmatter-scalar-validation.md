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

Quoted values are tracked separately from plain values. This distinction is required because a path such as `"null"` is a string, while plain `null` is resolved as a non-string by common YAML schemas. The path consumer rejects whole-scalar plain values that resolve as nulls, booleans, integers, floats, infinities, NaN, dates, or timestamps. Matching is bounded to the complete scalar so ordinary paths such as `2026-09-17-notes.md`, `true/guide.md`, and `123/notes.md` remain valid.

Every decoded internal control character in the C0, DEL, and C1 ranges is rejected. This applies equally to policy JSON strings, literal YAML characters, and escape sequences decoded from quoted YAML values. Ordinary visible Unicode remains valid. Leading or trailing Unicode whitespace is rejected separately rather than trimmed into an apparently matching control path.

## Recovery audit

The original PR branch accumulated unrelated history and could no longer provide a reviewable three-file change. The clean replacement was created from current `main` and initially copied the three intended blobs. A second audit found that the source branch did not actually contain the final control-range and implicit-scalar changes claimed in its automation summary. The clean branch was corrected directly rather than carrying that overstatement forward.

The current replacement contains:

- `scripts/check-docs-governance.mjs`;
- the original governance regression suite;
- `scripts/check-docs-governance.hardening.test.mjs`, which isolates the missing fail-open cases;
- this evidence note.

No workflow, required context, dependency, deployment setting, policy document, or agent-control rule is changed.

## Verification

Focused red/green evidence on Node v22.16.0:

- against the extracted source implementation, the new suite failed three of six groups:
  - policy controls accepted U+0009;
  - decoded quoted-YAML controls accepted U+0009;
  - plain YAML `~` was accepted as a string path;
- against the corrected implementation, all six groups pass;
- the suite exhaustively checks C0, DEL, and C1 code points in policy JSON and decoded quoted YAML;
- it checks representative null, boolean, numeric, non-finite, date, and timestamp forms;
- it proves quoted equivalents remain strings;
- it proves visible Unicode and path-like counterexamples are not overmatched;
- `node --check scripts/check-docs-governance.mjs` passes.

The current hosted Docs Governance job executes the checker against the repository policy/rule pair but does not yet invoke this Node regression suite. Wiring governance regressions into hosted CI remains separately owned by #3005; this PR does not alter a control-plane workflow to conceal that boundary.

Exact-head hosted CI, a fresh code review, and review of the final changed-file inventory are required before merge. There is no frontend, backend, browser, deployment, or hosted-product claim.
