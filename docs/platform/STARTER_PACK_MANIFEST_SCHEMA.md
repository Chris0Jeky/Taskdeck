# Starter Pack Manifest Schema (PACK-01)

Last Updated: 2026-02-25

This document defines the `v1` starter-pack manifest contract used by Taskdeck package foundations.

## Goals

- Provide a deterministic, versioned manifest format for starter packs.
- Support pack composition for labels, columns, card templates, and seed cards.
- Define compatibility and validation constraints ahead of backend apply endpoints (`PACK-02`).

## Schema Version

- Current supported version: `1.0`
- Field: `schemaVersion`
- Validation rule: values other than `1.0` are rejected.

## Manifest Shape (v1)

```json
{
  "schemaVersion": "1.0",
  "packId": "engineering-onboarding",
  "displayName": "Engineering Onboarding",
  "description": "Baseline board setup for engineering teams",
  "compatibility": {
    "minTaskdeckVersion": "1.0.0",
    "maxTaskdeckVersion": "2.0.0",
    "requiredFeatures": ["boards", "labels"]
  },
  "tags": ["starter", "engineering"],
  "labels": [
    { "name": "priority-high", "color": "#E85D5D", "description": "High urgency" },
    { "name": "blocked", "color": "#4A5568", "description": "Waiting on dependency" }
  ],
  "columns": [
    { "name": "Backlog", "position": 0 },
    { "name": "In Progress", "position": 1, "wipLimit": 5 },
    { "name": "Done", "position": 2 }
  ],
  "templates": [
    {
      "templateId": "bug-report",
      "title": "Bug Report",
      "description": "Template for bug triage",
      "checklist": ["Reproduction steps", "Expected behavior", "Actual behavior"]
    }
  ],
  "seedCards": [
    {
      "title": "Set up sprint board",
      "description": "Create initial sprint lanes",
      "columnName": "Backlog",
      "templateId": "bug-report",
      "labels": ["priority-high"]
    }
  ]
}
```

## Validation Rules

General:
- `packId`, `tags[*]`, `requiredFeatures[*]`, and `templateId` must be kebab-case.
- `displayName` is required.
- Duplicate `tags` and duplicate `requiredFeatures` are rejected (case-insensitive).

Compatibility:
- `minTaskdeckVersion` is required and must be strict semver (`major.minor.patch`).
- `maxTaskdeckVersion` is optional; when provided it must be strict semver.
- `maxTaskdeckVersion` must be greater than or equal to `minTaskdeckVersion`.

Labels:
- `labels[*].name` is required and unique (case-insensitive).
- `labels[*].color` must be hex RGB format (`#RRGGBB`).

Columns:
- At least one column is required.
- `columns[*].name` is required and unique (case-insensitive).
- `columns[*].position` must be non-negative.
- Column positions must be contiguous starting at `0`.
- `wipLimit`, when provided, must be greater than `0`.

Templates:
- `templates[*].templateId` must be kebab-case and unique (case-insensitive).
- `templates[*].title` is required.
- `templates[*].checklist[*]` items cannot be blank.

Seed cards:
- `seedCards[*].title` and `seedCards[*].columnName` are required.
- `seedCards[*].columnName` must reference an existing column name.
- `seedCards[*].templateId`, when provided, must reference an existing template ID.
- `seedCards[*].labels[*]` must reference existing label names.

## First-Party Catalog (PACK-04)

First-party packs are exposed by:
- `GET /api/boards/{boardId}/starter-packs/catalog`

Catalog entry shape includes:
- `id` (must match `manifest.packId`)
- `category` (`label-pack`, `column-flow`, or `board-blueprint`)
- `title`, `summary`, `highlights`
- `manifest` (full `v1` manifest payload)

Current first-party coverage target:
- At least one `label-pack`
- At least one `column-flow`
- Exactly three `board-blueprint` packs

All first-party manifests are validated server-side through the same validator used by apply flows.

## Apply Result Conflict Semantics (PACK-07)

Starter-pack apply responses now distinguish blocking vs warning conflicts:

- `hasConflicts`: `true` when any conflict exists (blocking or warning)
- `hasBlockingConflicts`: `true` only when at least one blocking conflict exists
- `conflicts[*].severity`: `blocking` or `warning`

Decision rule for clients:
- treat apply as blocked only when `hasBlockingConflicts` is `true`
- do not treat `hasConflicts=true` as automatic hard-stop

Operational intent:
- warning conflicts are non-blocking and can coexist with `applied=true`
- blocking conflicts keep apply in preview/non-applied posture and use `409` when `dryRun=false`

## Validation and Migration Constraints (PACK-04)

Validation constraints:
- First-party catalog IDs must be unique.
- `id` and `manifest.packId` must stay aligned.
- All shipped manifests must pass `StarterPackManifestValidator` without warnings/errors.
- First-party manifests must remain on supported `schemaVersion` (`1.0`) until a new schema is introduced.

Migration constraints (operational policy):
- `packId` is treated as stable identity; existing IDs should not be repurposed.
- Breaking content changes for a shipped pack should use a new `packId` (or new schema) rather than mutating semantics in place.
- Non-breaking pack updates should remain additive and keep existing field meanings stable.

## Deterministic Validation Output

The validator returns:
- Parsed manifest (when parse succeeds)
- Ordered validation errors with:
  - `Path` (JSON-path-style pointer)
  - `Message` (human-readable failure reason)

Primary implementation:
- `backend/src/Taskdeck.Application/Services/StarterPackManifestValidator.cs`

Primary tests:
- `backend/tests/Taskdeck.Application.Tests/Services/StarterPackManifestValidatorTests.cs`

