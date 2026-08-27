# External services register

Status: **Active, sanitized**

Last reconciled: **2026-08-27**

This register records only the external-service facts that contributors need for architecture, release, cost, expiry, and
exit-path decisions. It must never contain account identifiers, student or legal identity evidence, payment details, private
quotes, recovery codes, signing keys, PFX files/passwords, API keys, secret names that reveal values, or screenshots from
private consoles. The maintainer keeps benefit redemption, identity, billing, and credential evidence in a private ledger.

`OUTSTANDING_TASKS.md` is the public human-action queue. [#1167](https://github.com/Chris0Jeky/Taskdeck/issues/1167) owns
release trust, while [ADR-0061](../decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md) and
[#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772) own the private shared-instance boundary.

## Register

| Service | Purpose and current posture | Owner class | Cost, expiry, or lock-in risk | Exit path / continuity | Authority |
| --- | --- | --- | --- | --- | --- |
| GitHub Actions, Releases, Environments, and artifact attestations | **Active** release control plane. v0.1.2 uses Actions/Releases for exact-source build, checksums, custom provenance, acceptance, and publication. Standard attestations and release SBOMs are planned, not shipped. | Repository maintainer/admin | Plan/feature availability, Actions consumption, environment-policy drift, action-supply-chain risk. | Preserve reproducible local build, today's public checksum/custom provenance, immutable source tags, and final artifacts so another forge can reproduce the release; add public release SBOMs and attestations only after #2152 ships. | [release workflow](../../.github/workflows/release-desktop.yml), [#1504](https://github.com/Chris0Jeky/Taskdeck/issues/1504), [#2152](https://github.com/Chris0Jeky/Taskdeck/issues/2152) |
| Microsoft Artifact Signing | **Candidate**, not selected or provisioned. Public-trust Authenticode for direct Windows downloads. | Maintainer/legal publisher and release-security owner | Basic is currently listed at USD 9.99/account/month; identity/region validation, paid-subscription requirement, quota/overage, renewal, provider availability. Free/trial/sponsored Azure subscriptions are unsupported. | Keep a documented SignPath or traditional-CA fallback and make the signing seam provider-replaceable without changing product identity. | [#2148](https://github.com/Chris0Jeky/Taskdeck/issues/2148), [Microsoft pricing](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku), [Microsoft FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq) |
| SignPath Foundation / SignPath.io | **Candidate**, not applied for or accepted. Free signing for eligible accepted open-source projects; Foundation-held HSM key. | Maintainer plus SignPath project approvers | Eligibility/acceptance and continuing compliance; Foundation is the certificate publisher; service or policy withdrawal/revocation. | Retain Artifact Signing/traditional-CA fallback, reproducible unsigned build, and independent checksum/attestation evidence. | [#2148](https://github.com/Chris0Jeky/Taskdeck/issues/2148), [programme terms](https://signpath.org/terms.html) |
| Traditional public-trust certificate authority | **Fallback class**, provider undecided. | Maintainer/legal publisher and release-security owner | Quote, identity validation, hardware/provider key custody, annual renewal, token availability, revocation and CI integration. | Select a second compatible provider before the primary route becomes a release gate; keep signing/verification scripts provider-neutral. | [#2148](https://github.com/Chris0Jeky/Taskdeck/issues/2148), [#2149](https://github.com/Chris0Jeky/Taskdeck/issues/2149) |
| Domain registrar and DNS provider | **Undecided**. Canonical release/support/security identity only; no domain is required to keep the current free beta downloadable. | Maintainer/legal publisher | Renewal lapse, price change, registrar lock, DNS/account recovery, public identity drift. | Record transferable DNS configuration and recovery ownership privately; retain GitHub release URLs until a canonical domain is decided. | [#1482](https://github.com/Chris0Jeky/Taskdeck/issues/1482), [#2148](https://github.com/Chris0Jeky/Taskdeck/issues/2148) |
| Render | **Candidate, not an evidenced live Taskdeck service.** Repository default for the bounded private shared instance is one Docker web service, one `starter` instance, and one persistent SQLite disk. | Maintainer/operator and infrastructure cost owner | Paid compute, disk, pipeline/bandwidth, renewal/billing, single-disk downtime, provider account loss. A disk cannot scale across instances and disables zero-downtime deploys. | Keep self-host+tunnel available until acceptance; retain encrypted application-consistent backups, separate connector-key backup, exact image reference, and a rehearsed restore to another single-instance host. | [#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772), [#1777](https://github.com/Chris0Jeky/Taskdeck/issues/1777), [Render disk limits](https://render.com/docs/disks) |
| Railway or another single-instance host | **Deferred alternative**, not a parallel implementation track. | Maintainer/operator | Current quote, volume semantics, egress, secret custody, provider-specific deployment coupling. | Evaluate only if the maintainer rejects Render or a tested exit drill requires it; preserve the same one-instance/one-SQLite-volume contract. | [#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772), [#1777](https://github.com/Chris0Jeky/Taskdeck/issues/1777) |
| OpenAI-compatible LLM provider | **Optional external data processor.** Local/demo defaults remain Mock/off; a private shared instance may use BYO or explicitly operator-funded credentials only. | Credential owner, infrastructure operator, and board users who accept the egress | Token spend, key expiry/revocation, model/provider change, data egress and retention posture. | Keep Mock/local-compatible operation, removable per-provider keys, explicit provider health checks, and exportable local data. | [#1879](https://github.com/Chris0Jeky/Taskdeck/issues/1879), [#1992](https://github.com/Chris0Jeky/Taskdeck/issues/1992), [#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772) |
| GitHub Student Developer Pack and Azure for Students | **Optional private benefit discovery only.** May support a disposable lab/staging experiment; not production evidence, Artifact Signing payment, or permanent architecture. | Eligible maintainer/student, privately | Eligibility and renewal dates, benefit changes/expiry, sponsored-subscription restrictions, accidental production dependency. | Keep the detailed ledger private; set expiry reminders; delete or migrate labs before benefits end; choose production/signing services on ordinary exit-capable terms. | `OUTSTANDING_TASKS.md`, [Microsoft FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq) |
| Microsoft Store and winget | **Deferred** additive Windows channels. Direct downloads must become trustworthy first. | Maintainer/legal publisher | Account/enrolment policy, package identity, review lead time, listing upkeep and metadata drift. | Continue direct GitHub distribution; require the #2148-#2151 signing chain before describing it as signed or trustworthy, and do not make Store acceptance the only installation path. | [#1167](https://github.com/Chris0Jeky/Taskdeck/issues/1167) |
| Apple Developer Program | **Deferred** until a macOS release exists. | Maintainer/legal publisher | Apple lists USD 99/year; legal identity, renewal, notarization credentials and platform-policy change. | Keep macOS out of the committed channel matrix until separately authorized; preserve cross-platform source/build work without claiming a signed product. | [#1167](https://github.com/Chris0Jeky/Taskdeck/issues/1167), [Apple enrolment](https://developer.apple.com/programs/enroll/) |

## Operating rules

1. Re-verify prices, eligibility, plan features, and terms in the provider's official surface immediately before a decision.
2. Record only the owner **class** here. Names, account IDs, receipts, identity documents, private correspondence, and exact
   renewal data stay in the private ledger.
3. Every production dependency needs a cost owner, renewal/expiry owner, least-privilege access boundary, backup/recovery plan,
   and tested exit path before activation.
4. Student or promotional credit may lower the cost of a disposable experiment; it never lowers the evidence or exit bar.
5. A vendor account or successful deployment is not product proof. Release signatures, artifact verification, private access,
   SignalR/reconnect, durable reload, backup, connector-key recovery, and restore each need their own direct evidence.
6. Add a new public register row only when the service has an accepted architectural purpose or a live decision issue. Keep
   speculative vendor shopping in the private evaluation notes.

## Review cadence

Reconcile this file when a service is selected, activated, replaced, or retired; when a plan/price/eligibility fact affects a
decision; and before each signed release or private-instance acceptance. A stale row is a planning lead, not confirmed-current
account evidence.
