# ADR-0039: Central Package Management, SDK Pin, and 8.x Dependency Alignment

- **Status**: Accepted
- **Date**: 2026-06-13
- **Deciders**: Repository maintainers

## Context

The backend (`net8.0`) accumulated three related build-hygiene problems that this
decision addresses together (issue #1127):

1. **Scattered, drifting NuGet versions.** Each of the 11 backend `.csproj` files
   declared its own `Version=` on every `PackageReference`. The same package could be
   pinned to different versions in different projects, and a Dependabot bump to one
   project silently desynchronized the rest. There was no single place to audit or change
   a version.

2. **Mixed package majors against the `net8.0` TFM.** Several packages had drifted above
   the .NET 8 line while the projects still target `net8.0`:
   - `Microsoft.EntityFrameworkCore.Tools` was on **10.0.8**. Although it is a
     design-time-only package (`PrivateAssets`), on its 10.x line it pulled **EF Core 9.x**
     in transitively at restore, undermining the deliberate runtime pin to EF 8.x
     (#760/#767, ADR-0034).
   - `Npgsql.EntityFrameworkCore.PostgreSQL` was on **9.0.4**, ahead of the EF Core 8.x
     family it must match.
   - `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.Extensions.Hosting`,
     `Microsoft.Extensions.Http.Polly`, and `Microsoft.Extensions.Logging.Abstractions`
     had drifted to **10.0.8**.

3. **No SDK pin.** Contributors and CI used whatever .NET SDK happened to be installed.
   A newer SDK can change analyzer behavior, restore resolution, and default warnings,
   producing "works on my machine" divergence between local builds, CI runners, and the
   Docker build images.

ADR-0034 capped EF Core and FluentAssertions majors via Dependabot `ignore` rules but
**explicitly ruled Central Package Management out of scope** and **left
`Microsoft.EntityFrameworkCore.Tools` uncapped** on its 10.x line on the (then-correct)
reasoning that a design-time-only package "does not affect the runtime compile." This ADR
records what changed: `Tools` on 10.x drags EF Core 9.x in transitively, so production
(which runs EF **8.x**) is affected, and `Tools` must be pinned to 8.0.x with the rest of
the EF stack.

## Decision

### 1. Central Package Management (CPM)

All backend NuGet versions live in **`backend/Directory.Packages.props`**
(`ManagePackageVersionsCentrally = true`) as `<PackageVersion Include="..." Version="..." />`
entries. Individual `.csproj` files carry only `<PackageReference Include="..." />` — no
`Version=` attribute.

**Consequence for contributors:** adding a `Version=` attribute to a `PackageReference`
in any `.csproj` under CPM fails restore with **NU1008** ("Projects that use central
package version management should not define the version on the PackageReference items").
To add or change a dependency version, edit `backend/Directory.Packages.props`, not the
`.csproj`.

### 2. SDK pin via `global.json`

A `global.json` at the **repository root** pins the SDK to **8.0.415** with
**`rollForward: latestFeature`** and `allowPrerelease: false`.

- The file is at the **repo root** (not `backend/`) because the .NET muxer begins
  `global.json` discovery at the current working directory and walks **upward**; the
  documented commands and all CI jobs invoke `dotnet` from the repo root, so a
  `backend/global.json` would be invisible to them.
- `rollForward` is **`latestFeature`** (not `latestPatch`): the pin still locks the
  **8.0** major.minor line, but tolerates feature-band rollovers. CI uses
  `setup-dotnet` with `dotnet-version: 8.0.x` and the Docker builds use the
  `mcr.microsoft.com/dotnet/sdk:8.0` image — both deliver only the **newest** 8.0.x
  feature band. With `latestPatch`, the day Microsoft ships only `8.0.5xx` SDKs the pin
  to `8.0.415` would stop resolving and every CI job and Docker build would fail
  simultaneously. `latestFeature` keeps the major.minor guarantee while surviving that
  rollover.

### 3. Align mixed majors down to the 8.x line

In `backend/Directory.Packages.props`, the drifted packages are aligned to the `net8.0`
family:

| Package | Was | Now |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.8 | 8.0.27 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.0.4 | 8.0.11 |
| `Microsoft.AspNetCore.SignalR.Client` | 10.0.8 | 8.0.27 |
| `Microsoft.Extensions.Hosting` | 10.0.8 | 8.0.1 |
| `Microsoft.Extensions.Http.Polly` | 10.0.8 | 8.0.27 |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.8 | 8.0.3 |

To keep the alignment durable, all six are capped against `version-update:semver-major`
in `.github/dependabot.yml` (extending ADR-0034). Without the caps, the next weekly
Dependabot run re-proposes the same majors and reverts the alignment — the exact
self-reinflating recurrence ADR-0034 was created to stop. **This explicitly supersedes
ADR-0034's note that `Microsoft.EntityFrameworkCore.Tools` is intentionally not capped:**
because `Tools` on 10.x drags EF Core 9.x transitively, it is now pinned and capped to
8.0.x with the rest of the EF stack.

## Alternatives

- **Keep per-`.csproj` versions (status quo).** Rejected: version drift between projects,
  no single audit point, and Dependabot desyncs the stack one project at a time.
- **Adopt CPM but skip the SDK pin.** Rejected: CPM centralizes versions but does nothing
  about SDK-driven analyzer/restore divergence; the two problems are independent.
- **Pin the SDK with `rollForward: latestPatch`.** Rejected: brittle against feature-band
  rollovers (see Decision §2) — would break all of CI and Docker the day MS retires the
  pinned feature band.
- **Migrate the whole stack to EF/.NET 9 instead of aligning down.** Out of scope: the
  project deliberately runs `net8.0`/EF 8.x (#760/#767, ADR-0034). Moving to 9.x is a
  separate coordinated migration, not a build-hygiene fix.

## Consequences

- One audit point (`backend/Directory.Packages.props`) for every backend NuGet version;
  Dependabot bumps land there and apply uniformly.
- Adding a `Version=` to a `.csproj` `PackageReference` now fails restore (NU1008) — a
  guardrail, but a surprise for contributors unfamiliar with CPM. Documented in
  `docs/ops/DEPENDENCY_UPDATE_POLICY.md`.
- All contributors, CI runners, and Docker builds resolve the same 8.0.x SDK feature
  band, removing SDK-driven divergence while surviving feature-band rollovers.
- The EF runtime stack (including `Tools`) is consistently on 8.x; the transitive EF 9.x
  pull-in via `Tools` 10.x is gone.
- The six new major caps must be removed **deliberately and atomically** when the backend
  migrates off `net8.0`, alongside the existing EF Core caps. Removal conditions are
  tracked in `docs/ops/DEPENDENCY_UPDATE_POLICY.md`.

## References

- Issue #1127; PR #1196
- `backend/Directory.Packages.props` (CPM version file)
- `global.json` (repo root — SDK pin + `latestFeature`)
- `.github/dependabot.yml` (the six new major caps)
- `docs/ops/DEPENDENCY_UPDATE_POLICY.md` (CPM workflow, caps table, SDK pin)
- ADR-0034 — Dependency Version Caps via Dependabot Ignore Rules (extended here; `Tools`
  cap reversal recorded above)
- Prior EF pin rationale: #760/#767
