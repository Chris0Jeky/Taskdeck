# ADR-0068: The Development Sandbox Never Widens Write-Class Authorization

- **Status**: Accepted
- **Date**: 2026-09-04
- **Deciders**: coordinator ruling on `#1866` (story S2), under the standing delegation; not a
  separate maintainer decision
- **Related**: `#1866`, `#1861` (the review that surfaced the divergence), `#1836` / `#1794` /
  `#1827` (the write bar the policy engine mirrors), ADR-0003 / GP-06 (review-first automation),
  `docs/security/BETA_THREAT_MODEL.md` §B3

## Context

`DevelopmentSandboxSettings.Enabled` is a Development-only convenience: `SettingsRegistration.cs`
force-disables it outside the `Development` environment, `appsettings.Development.json` ships it
`false`, and every deployment artefact pins `DevelopmentSandbox__Enabled=false`. Production is
therefore unaffected by anything below; the blast radius is local dev, demos, and dogfooding.

Within that scope, the flag had drifted from "convenience" to "authorization bypass". Three
components disagreed about what a sandbox user may do:

| Component | Sandbox behaviour before this ADR |
| --- | --- |
| `AuthorizationService.CanWriteBoardAsync` (and the delete / manage / role / writable-set siblings) | returns `true` unconditionally — no membership consulted |
| `BoardAccessService.EnsureCanManageBoardAccessAsync` | returns success unconditionally |
| `ProposalExecutionAuthorizationSnapshotReader` | drops the whole owner-or-member scope filter on the execute path |
| `AutomationPolicyEngine.HasAccessAsync` (the worker/proposal gate) | **no sandbox branch, and never had one** — repository-backed membership in every environment |

A sandbox user holding only a `Viewer` row therefore passed the API bar and was refused at the
worker. `#1861` widened the worker mirror from "any membership" to "write-capable membership",
which sharpened the divergence but did not create it. The issue asked for one story, not both.

Two candidates were considered:

- **S1 — the policy engine honours sandbox mode.** Teach `AutomationPolicyEngine` (and the
  repository gate under it) the same bypass, so the two agree at the permissive end.
- **S2 — narrow the API-side bypass.** Remove the sandbox branch from the write-class gates so the
  two agree at the strict end; sandbox still requires write-capable membership.

## Decision

**S2.** The development sandbox is a **read and export convenience**. It never widens write-class
authorization.

Concretely:

- **Removed** — the sandbox branch in `AuthorizationService.CanWriteBoardAsync`,
  `GetWritableBoardIdsAsync`, `CanDeleteBoardAsync`, `CanManageBoardAccessAsync`, and the synthetic
  `UserRole.Owner` in `GetUserRoleForBoardAsync` (which now reports the caller's real role);
  `BoardAccessService.EnsureCanManageBoardAccessAsync`; and the scope-widening branch in
  `ProposalExecutionAuthorizationSnapshotReader`, whose execute path is a write lane.
- **Kept** — the read-side bypasses (`CanReadBoardAsync`, `GetReadableBoardIdsAsync`), which make
  local fixtures browsable without hand-seeding grants, and the export/import services' use of the
  flag as a *requirement* (those endpoints are gated **on** sandbox, not widened **by** it).

S1 was rejected. It would carry a Development-only bypass into the review-gated automation lane —
the surface ADR-0003 / GP-06 exist to keep honest — and would make the strictest gate in the system
the one most easily switched off by configuration. It also inverts the sandbox's job: the flag
exists so a developer can *see* seeded state, not so a Viewer can mutate boards they were not
granted. Converging on the strict end costs a dev fixture one real `BoardAccess` row; converging on
the permissive end costs the automation lane its only environment-independent gate, and makes local
demos a poor rehearsal for production behaviour.

## Consequences

- A sandbox user now needs a real owner row or a write-capable `BoardAccess` row to write, manage
  access, delete, or execute proposals. Boards created through the API already set `OwnerId` to the
  creator, so the common demo and dogfooding path is unchanged.
- Sandbox authorization answers now match production answers on every write-class gate, so a local
  demo is a valid rehearsal. The batch-execute path in particular collapses to `404` for a caller
  with no visibility instead of a sandbox-only `403`.
- `BoardAccessService` and `ProposalExecutionAuthorizationSnapshotReader` no longer take a
  `DevelopmentSandboxSettings` dependency at all — the divergence cannot silently return.
- Residual, out of scope for this ADR and left on `#1866` for follow-up: `LlmQueueService` (:133)
  still relaxes its cross-user ownership check under sandbox, and `BoardJsonExportImportService`
  (:239) retains a sandbox branch on a **read** check, which is consistent with the read bypasses
  kept above but is left named rather than silently folded in.
