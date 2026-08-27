# Release trust and distribution programme

Status: **Active**

Last reconciled: **2026-08-27**

Tracker: [#1167](https://github.com/Chris0Jeky/Taskdeck/issues/1167)

This document is the active execution contract for making Taskdeck's direct releases trustworthy. It distils the detailed
[programme spike](../spikes/TASKDECK_RELEASE_TRUST_DISTRIBUTION_AND_CLOUD_PROGRAMME.md) into the bounded, live backlog.
`docs/STATUS.md` remains authoritative for shipped behaviour; the release workflow and published assets remain the executable
evidence.

## Current truth

The latest release, [v0.1.2](https://github.com/Chris0Jeky/Taskdeck/releases/tag/v0.1.2), contains a Windows x64 portable ZIP,
its SHA-256 file, and Taskdeck's custom provenance record. The workflow already resolves and rechecks the exact tag/commit,
builds from a pinned checkout, checks the untouched archive, runs acceptance checks, and supports resumable publication.

The Windows executable and ZIP are **not Authenticode-signed**. There is no user-grade installer, release SBOM, or GitHub
artifact attestation. The current [Windows quick start](../releases/WINDOWS_QUICK_START.md) is therefore correct to describe
the unsigned-download warning; it must not promise a signed path before one is published.

Current end-user distribution is Windows-first. SmartScreen reputation testing, Microsoft Store, winget, macOS
signing/notarization, and Linux package channels are deferred. They are not hidden requirements of this wave.

## Dependency-correct first wave

| Order | Issue | Outcome | Initial Project state |
| --- | --- | --- | --- |
| 1 | [#2148](https://github.com/Chris0Jeky/Taskdeck/issues/2148) | Decide publisher/product/domain identity and primary/fallback signing route. | Blocked on maintainer/legal decision |
| 2 | [#2149](https://github.com/Chris0Jeky/Taskdeck/issues/2149) | Provision the signing identity and protected CI boundary. | Blocked on #2148 and human enrolment |
| 3 | [#2150](https://github.com/Chris0Jeky/Taskdeck/issues/2150) | Authenticode-sign, timestamp, and fail closed before packaging. | Blocked on #2149 |
| 4 | [#2151](https://github.com/Chris0Jeky/Taskdeck/issues/2151) | Publish a signed user-grade installer with stable metadata. | Blocked on #2150 |
| Parallel, joins before publish | [#2152](https://github.com/Chris0Jeky/Taskdeck/issues/2152) | Publish SBOMs, GitHub attestations, and public verification instructions. | Pending |

All five issues are sub-issues of #1167, have exactly one Priority label, and begin without release milestones. A maintainer
must explicitly decide that an item gates a release before it receives a milestone. #1947 remains the release-history and
dogfooding tracker; #1167 does not replace it.

## Release contract

The trusted Windows lane must keep this order:

1. Resolve the immutable tagged commit and build from the pinned checkout.
2. Stage the exact executable and metadata that will ship.
3. Sign with the approved publisher identity and an approved timestamp service.
4. Verify publisher, chain, digest, and timestamp; fail closed on any mismatch or missing input.
5. Package the already-signed payload, then extract and verify it again.
6. Generate checksums, fail-closed release SBOMs, existing custom provenance, and GitHub attestations over the final artifacts.
7. Run clean-machine acceptance and public-instruction verification.
8. Recheck the tag and publish without rebuilding or repacking.

The signing job must be unreachable from pull requests, forks, untrusted branches, and ordinary CI. Provider-held or
hardware-protected key custody is preferred; no signing key, PFX file/password, token, private account evidence, or recovery
material belongs in the repository, issue tracker, artifacts, or logs.

GitHub attestations complement rather than replace Authenticode, checksums, or Taskdeck's provenance record. They bind an
artifact digest to a workflow and source identity; they do not prove that the software is defect-free. See GitHub's
[artifact-attestation guidance](https://docs.github.com/en/actions/concepts/security/artifact-attestations).

## Human decision boundary

Agents may prepare code, tests, redacted evidence, documentation, and reversible repository settings within declared
authority. Only the maintainer may decide or perform legal-publisher selection, domain or account registration, identity
validation, provider enrolment, purchases, billing, benefit redemption, credential/key recovery, protected-environment
approval, and subjective installer/SmartScreen acceptance. The open human actions are in `OUTSTANDING_TASKS.md`.

The dated Taskdeck-name decision remains: keep the name for the free beta. [#1482](https://github.com/Chris0Jeky/Taskdeck/issues/1482)
continues to own trademark, domain, handle, and namespace residuals. [#550](https://github.com/Chris0Jeky/Taskdeck/issues/550)
owns visual identity and release artwork only.

## Private cloud boundary

Distribution work does not create a public SaaS plan. [ADR-0061](../decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md)
and [#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772) permit only one trusted private instance: one application
instance, one SQLite volume, a few known users, private access, InviteOnly onboarding, exact-image evidence, SignalR/reconnect
and durable-reload proof, application-consistent encrypted backup, separate connector-key backup, one clean restore drill,
an infrastructure cost owner, and explicit LLM payer/egress disclosure. [#1777](https://github.com/Chris0Jeky/Taskdeck/issues/1777)
is the Render implementation split after those decisions. Neither issue authorizes tenancy, public signup, billing, horizontal
scale, or a managed-service claim.

Azure Student credit is lab/staging leverage only. Microsoft states that Artifact Signing requires a paid subscription and
does not support free, trial, or sponsored subscriptions; student credit must not be treated as a signing entitlement or a
permanent architecture decision.

## Dated cost and timeline envelope

These are planning inputs, not purchases or quotes. Re-verify them immediately before an owner decision.

| Route or service | 2026-08-27 planning input | Gate or risk |
| --- | --- | --- |
| Microsoft Artifact Signing | Basic is listed at USD 9.99/account/month for 5,000 signatures, plus overage. | Paid Azure subscription, identity/region eligibility, renewal and cost owner; student/sponsored subscriptions are unsupported. |
| SignPath Foundation | Free of charge for accepted open-source projects. | Eligibility and acceptance are not guaranteed; its Foundation is the certificate publisher and its OSS conditions bind the project. |
| Traditional public-trust CA | Fallback quote required. | Identity validation, hardware/provider key custody, annual renewal, and CI integration vary by CA. |
| GitHub artifact attestations | Available for public repositories on current GitHub plans. | Requires least-privilege OIDC/attestation permissions and consumer verification. |
| Render private shared instance | Repository default is one paid `starter` service plus a 1 GB persistent disk; the programme's dated floor is roughly USD 7/month plus storage/egress. | Confirm the current dashboard quote, spend alerts, downtime-with-disk behaviour, backups, and exit path before activation. |
| Apple Developer Program | Deferred; Apple lists USD 99 per membership year. | Human enrolment and legal identity; not part of the Windows-first wave. |

Planning duration after decisions: 1–3 working days for the owner route decision, provider-dependent identity/enrolment lead
time, 2–4 engineering days for the signing seam, 3–5 for installer/upgrade proof, and 1–3 for SBOM/attestation integration.
These stages may overlap only where the dependency table permits. Provider approval, hardware delivery, legal review, and
SmartScreen reputation can extend the calendar and are not agent-controlled.

## Principal risks and stop conditions

- Stop if the publisher identity or signing route is ambiguous; do not create a temporary public identity that users later
  cannot trust through upgrades.
- Stop publication if signing, timestamp, publisher, chain, digest, SBOM, provenance, or attestation verification fails.
- Treat first-run SmartScreen warnings as a reputation QA result, not proof that Authenticode is absent or invalid.
- Do not let installer convenience remove the portable path or user data without an explicit compatibility decision.
- Do not let a successful cloud launch stand in for backup/restore, SignalR/reconnect, access-control, cost, or egress proof.
- Do not record private benefit, student, identity, billing, recovery, or credential evidence in public repository surfaces.

## Update triggers

Update this document only when the live release state, dependency order, provider decision, distribution scope, cost envelope,
or private-cloud boundary changes. Implementation evidence belongs on the owning issue and in release assets; shipped reality
belongs in `docs/STATUS.md`.
