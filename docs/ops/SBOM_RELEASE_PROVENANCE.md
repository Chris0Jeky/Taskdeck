# SBOM Generation and Release Provenance

Last Updated: 2026-03-30
Owner: Taskdeck maintainers
Linked issue: `#103` (OPS-11)

## Purpose

This document defines the SBOM (Software Bill of Materials) generation and release provenance policy for Taskdeck. It covers:

- What SBOM artifacts are generated and in what format
- When and how provenance metadata is captured
- Artifact retention and access policy
- Failure handling and review process

## SBOM Format

Taskdeck uses the [CycloneDX](https://cyclonedx.org/) format (JSON, spec version 1.5) for all SBOMs. CycloneDX is an OWASP project and an ISO standard (ISO/IEC 27036) widely supported by vulnerability scanners and dependency analysis tools.

### Backend SBOM

- **Tool:** `CycloneDX` .NET global tool (`dotnet CycloneDX`)
- **Input:** `backend/Taskdeck.sln` (all projects and transitive dependencies)
- **Output:** `backend-sbom.json` (CycloneDX JSON)
- **Scope:** All NuGet packages including transitive dependencies; project references included

### Frontend SBOM

- **Tool:** `@cyclonedx/cyclonedx-npm` (npx, latest)
- **Input:** `frontend/taskdeck-web` (production dependencies only, dev omitted)
- **Output:** `frontend-sbom.json` (CycloneDX JSON)
- **Scope:** All npm production packages including transitive dependencies

## Build Provenance

A SLSA (Supply-chain Levels for Software Artifacts) v1 provenance manifest is generated alongside the SBOMs. This manifest captures:

- **Subject:** Repository name and ref/tag
- **Source digest:** Git SHA at build time
- **Builder identity:** GitHub Actions run ID and URL
- **Build inputs:** Resolved dependency reference (git URI + SHA)
- **Byproducts:** References to generated SBOM files
- **Tool versions:** .NET SDK, Node.js, CycloneDX tool versions
- **Invocation metadata:** Run ID, run number, attempt, actor, event name

The provenance manifest is stored as `build-provenance.json`.

All artifacts include a `checksums.sha256` file with SHA-256 digests for integrity verification.

## Workflow Integration

SBOM and provenance generation is implemented as a reusable GitHub Actions workflow:

- **Reusable workflow:** `.github/workflows/reusable-sbom-provenance.yml`
- **Called by:**
  - `ci-release.yml` -- on tag push, release publish, and manual dispatch
  - `release-security.yml` -- on tag push, release publish, and manual dispatch

### Trigger Matrix

| Trigger | Workflow | SBOM Generated |
|---------|----------|----------------|
| Tag push (`v*`) | ci-release, release-security | Yes |
| Release published | ci-release, release-security | Yes |
| Manual dispatch | ci-release, release-security | Yes |
| PR merge | ci-required | No (not in critical path) |
| Nightly | nightly-quality | No (dependency signals only) |

## Artifact Retention

| Artifact | Retention | Location |
|----------|-----------|----------|
| Backend SBOM | 90 days | GitHub Actions artifact |
| Frontend SBOM | 90 days | GitHub Actions artifact |
| Build Provenance | 90 days | GitHub Actions artifact |
| Checksums | 90 days | GitHub Actions artifact |
| Stderr logs | 90 days | GitHub Actions artifact |

The 90-day retention aligns with typical audit and compliance review windows. For release tags, artifacts should be downloaded and archived in long-term storage before expiry if required by compliance policy.

## Failure Handling

- SBOM generation steps use `continue-on-error: true` by default so that a single ecosystem failure does not block the entire release pipeline
- Exit codes and stderr are captured as artifacts for post-mortem review
- The `fail-on-error` input can be set to `true` to enforce strict SBOM generation success (recommended for production release gates once tooling is proven stable)
- The workflow summary step always reports generation status in the GitHub Actions step summary

## Security Review Process

When reviewing dependencies for a release:

1. Check the SBOM artifacts from the release workflow run
2. Cross-reference with the dependency vulnerability report from `release-security.yml`
3. Any component in the SBOM with a known vulnerability should be triaged per `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`
4. The provenance manifest confirms the build was produced from the expected source commit

## Permissions

The SBOM workflow uses `contents: read` permission only. No elevated permissions (write, packages, attestations) are required for the current implementation. If GitHub artifact attestation signing is added in the future, `id-token: write` and `attestations: write` permissions will be needed.

## Future Enhancements

- **Artifact attestation signing:** Use GitHub's `actions/attest-build-provenance` for cryptographic attestation once the workflow is proven stable
- **Container image SBOM:** Generate SBOMs for the Docker container images (e.g., using Syft/Grype)
- **SBOM diff on PR:** Compare SBOM between releases to surface new/removed/changed dependencies
- **Long-term archival:** Integrate with a dedicated artifact store for compliance retention beyond 90 days
- **SLSA Level 3:** Full SLSA Level 3 compliance with hermetic builds and non-forgeable provenance
