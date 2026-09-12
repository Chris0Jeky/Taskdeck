# Frontmatter scalar validation: issue #3006

Last Updated: 2026-09-12

Status: PR candidate, not a claim of merged or hosted-qualified delivery.

## Boundary and design

The control-path mirror is checked by `scripts/check-docs-governance.mjs` without
installing dependencies. The checker must not silently accept unreadable rule
frontmatter, but it is not a general YAML parser. This repair makes that boundary
explicit: top-level keys, single-line scalars and flat indented scalar lists.
Unsupported YAML constructs fail with a diagnostic rather than being approximated.

A quote or bracket inside a plain scalar is text, not the start of a quoted string
or flow collection. Leading quotes are parsed completely, including YAML doubled
single quotes and the JSON subset of double-quoted escapes. Separated trailing
comments are ignored; hashes within plain text remain literal. Other YAML escape
forms, tags, anchors, aliases, block scalars and flow collections are deliberately
unsupported. Consult the [YAML 1.2.2 specification](https://yaml.org/spec/1.2.2/),
sections 7.3 and 8.2, for the broader grammar; this check does not claim conformance
to that entire grammar or to any particular consumer's schema.

Mapping colons require separation. Tabs in indentation, nested/inconsistently
indented lists, inline nested mappings/sequences and trailing text after a quoted
scalar fail closed. Each top-level list can choose its own indentation. Duplicate
keys are rejected without asserting that every loader resolves them the same way.
Unquoted alias-like globs such as `**/.npmrc` must be quoted. Decoded path strings
must be nonempty and free of surrounding whitespace and control characters.
Policy `controlPaths` entries with surrounding whitespace are rejected rather
than trimmed into an apparently matching rule.

The existing policy and rule files are unchanged. No workflow, CI routing, required
context, dependency, deployment setting or maintainer decision changes here.

## Verification and reproducibility

All local executions used Linux and Node v22.16.0, on the uploaded snapshot in an
isolated linked worktree. Before editing, the two changed script blobs were checked
against live main `54e4c0a86fb77eabba73b5d21557d6f8720571bd` and matched exactly.
The worktree snapshot itself is not represented as that complete remote commit.

- Added 39 cases to the existing 26-case governance suite. Against the original
  implementation: **32 passed, 33 failed**. After repair: **65 passed, 0 failed**.
- `node --test scripts/check-*.test.mjs`: **177 passed, 2 failed**. Both failures
  also reproduce against unchanged source: **138 passed, 2 failed** there.
  The link-check case expects `wrong case`, but gets `missing` on the local
  case-sensitive filesystem. An indirectly imported staging-composition test
  requires Docker, which is not installed (`spawn docker ENOENT`). Neither file
  was changed. This is not a broad-green claim.
- `node scripts/check-docs-governance.mjs`, `node scripts/check-doc-links.mjs`,
  `node scripts/check-github-ops-governance.mjs` and `git diff --check` pass.

Re-run the focused suite using `node --test scripts/check-docs-governance.test.mjs`.
Hosted execution on the repository's configured Node version and independent
review remain outstanding. There is no full frontend/backend/browser claim.

## Integration

This is independent of the active estimate, typed-relation and archive-editor
stack. Canonical STATUS/MASTERPLAN documents and human acceptance checkboxes remain
unchanged. Issue #3005 separately owns wiring governance/link-check regression
suites into the hosted docs job; this PR does not pretend that wiring is complete.
