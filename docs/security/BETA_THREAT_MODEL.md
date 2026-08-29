# Beta Threat Model

Last Updated: 2026-08-29

Owner: Taskdeck maintainers

Status: **Draft** — REVIVAL-00 (`#1311`) deliverable for `docs/REVIVAL_PLAN.md` Phase 0. Not yet
ratified. Every claim below is either cited to code/ADR/doc or explicitly marked unverified.

Scope note: this is the **deployment and beta-decision** model. Attacker-controlled content
(PDFs, images, transcripts, pasted text, file names, extracted URLs) is owned by the
[Untrusted Artefact Threat Model](UNTRUSTED_ARTEFACT_THREAT_MODEL.md); that submodel is
referenced here, not duplicated.

## 1. Scope and deployment models

### In scope for v0.3

| Model | Description | Evidence |
| --- | --- | --- |
| **A. Single-user Windows portable** | Unsigned portable ZIP run on the owner's machine; SQLite file on local disk; MCP over stdio. | `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md` "Current truth"; `docs/MCP_SERVER.md` |
| **B. Self-hosted Docker behind a tunnel** | `deploy/docker-compose.yml` exposed through Cloudflare Access or a Tailscale tailnet; a small named set of accounts. | `docs/platform/SELF_HOST_TUNNEL_GUIDE.md:53-96` |
| **C. LAN device access** | Plain HTTP on a trusted LAN for phone testing; a *testing posture*, torn down afterwards. | `docs/platform/LAN_DEVICE_ACCESS_GUIDE.md:23`, `:295`, `:306` |
| **D. Trusted shared instance** | Two named identities behind an access perimeter. **Direction only** — ADR-0061 is "Accepted as direction only, evidence pending"; Stage 1 deployment stays gated on `#1772`. | `docs/decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md` |

### Explicitly out of scope

- **Public multi-tenant SaaS.** ADR-0061 splits collaboration hosting into distinct milestones;
  managed SaaS is a later one and has no shipped evidence.
- **Untrusted registrants on an `Open` instance.** Taskdeck ships `RegistrationMode.Open` as the
  application default (`backend/src/Taskdeck.Application/Services/RegistrationSettings.cs:15`) for
  local single-user convenience. An internet-reachable `Open` instance is an **unsupported**
  configuration for the beta: no email verification, no anti-abuse beyond fixed-window rate limits,
  no per-tenant resource isolation, one shared SQLite file.
- **Hostile co-tenant.** Every supported model assumes accounts belong to people the operator
  already trusts. Cross-user isolation is enforced claims-first, but is not adversarially proven at
  beta scale — see §5.
- **Delegated autonomy.** ADR-0057 is accepted as *direction only*; no auto-approval surface exists
  or may be built without a separate gate.

## 2. Assets

1. Board and card integrity, including the review-before-apply invariant (ADR-0003 / GP-06).
2. Private captures, transcripts, artefact bytes, proposal provenance, audit history.
3. Identity and credentials: password hashes, JWTs, MFA TOTP secrets, recovery codes, API keys,
   invite codes, connector credentials, the connector encryption key, `Jwt:SecretKey`.
4. Third-party spend and secrets: the OpenAI API key and the per-user / global LLM token budget.
5. Host availability: CPU, memory, disk, the single SQLite/WAL file.
6. The distribution channel itself: published release artefacts and the GHCR image.

## 3. Trust boundaries and actors

| # | Boundary | Actor crossing it | Notes |
| --- | --- | --- | --- |
| B1 | Public network → origin | Anonymous internet when a tunnel is open | Login, health and SignalR answer anyone who reaches the URL; the perimeter must be an identity/access policy, not an unlisted URL (`SELF_HOST_TUNNEL_GUIDE.md:55-61`). |
| B2 | Unauthenticated → authenticated | Registrant, then owner | Registration mode + invite (`RegistrationPolicyService.cs:33-80`), password login, optional TOTP. |
| B3 | User → user | Invited collaborator with a board grant | Claims-first authorization via `AuthorizationService` / `BoardAccessService`; roles carry a read/write lane split. |
| B4 | HTTP API-key holder | Scoped `tdsk_` key — `Read` / `Propose` / `Manage` / `Full` (`backend/src/Taskdeck.Domain/Enums/ApiKeyScope.cs:9-13`) | Key is SHA-256 at rest (`ApiKeyService.cs:97`); an 8-char prefix is stored for display (`:46`). |
| B5 | MCP stdio client | Local agent process | Identity resolved fail-closed from `McpServer:DefaultUserId` (`Infrastructure/Mcp/StdioUserContextProvider.cs:33,80`); no approve/apply tool exists (`docs/MCP_SERVER.md:3`). |
| B6 | Taskdeck → LLM provider | OpenAI only (ADR-0055) | Transcript-source triage may send bounded chunks off-device; mock is the default. |
| B7 | Local machine access | Anyone with the OS account | SQLite file, `appsettings.local.json`, connector key, JWT in `localStorage`. |
| B8 | Release channel → user | Downloader | Unsigned artefacts (§4, B8). |
| B9 | Untrusted content → prompt/board | Artefact/transcript ingress | Delegated to `UNTRUSTED_ARTEFACT_THREAT_MODEL.md`. |

## 4. Threats, current control, status

Status key: **shipped** (control exists and is exercised) · **partial** (control exists with a known
gap) · **open** (tracked issue, no control) · **out** (deliberately not in scope). Issue titles below
are taken from the task brief; the issues themselves were **not read** — treat their detail as
unverified.

### B1 — anonymous internet

| Threat | Current control | Status |
| --- | --- | --- |
| Anyone who finds the tunnel URL reaches login / health / SignalR | Operator-side identity perimeter (Cloudflare Access, or a Tailscale tailnet with an ACL); Funnel and quick tunnels explicitly rejected for the private instance | **partial** — the control is *operator procedure*, not enforced by Taskdeck (`SELF_HOST_TUNNEL_GUIDE.md:55-81`) |
| Credential stuffing / brute force | Fixed-window `AuthPerIp`, production default 120/60s (`CONFIGURATION_REFERENCE.md:531`); MCP has a separate authentication-failure budget and pre-auth concurrency cap (`:541-543`) | **partial** — behind a reverse proxy, `AuthPerIp` can collapse users into one bucket (`Api/Extensions/PipelineConfiguration.cs:57`) |
| Plaintext traffic on the LAN posture | None in-product; login works over plain HTTP and the JWT rides it | **partial (documented)** — `LAN_DEVICE_ACCESS_GUIDE.md:295`; teardown is the mitigation |
| Volumetric abuse / DoS | Rate limits only; one SQLite file, no per-account resource isolation | **partial** |

### B2 — registration and the account boundary

| Threat | Current control | Status |
| --- | --- | --- |
| Untrusted registrant creates an account on an exposed instance | `Open` / `InviteOnly` / `Closed` enforced in `RegistrationPolicyService.CheckNewUserEligibilityAsync` and `AuthorizeNewUserAsync` (`:33-80`), called from `AuthenticationService.cs:108,121,214,227`; `UsersController.CreateUser` refuses outside `Open` (`:78`) | **shipped**, but the **shipped default is `Open`** (`RegistrationSettings.cs:15`) |
| Invite replay, or a third account after the perimeter is set | Invite codes are hashed, one-time and expiring; the bootstrap slot is claimed even in `Open` so a later mode switch cannot reopen it (`RegistrationPolicyService.cs:63-74`) | **shipped** |
| Password compromise | BCrypt at library defaults (`Application/Services/IPasswordHasher.cs:12`); login pre-checks before paying the BCrypt cost (`AuthenticationService.cs:106`) | **shipped** — work factor not tuned or measured (*unverified*) |
| MFA secret theft from a copied database | TOTP seed stored as plain Base32 (`Domain/Entities/MfaCredential.cs:20-22`); recovery codes *are* BCrypt-hashed (`MfaService.cs:75`) | **open — `#1653`** MFA TOTP seeds unencrypted at rest |
| MFA not enforced | `MfaPolicy:EnableMfaSetup` defaults `false` and MFA is never forced (`CONFIGURATION_REFERENCE.md:270-271`) | **partial by design** |

### B3 — user to user

| Threat | Current control | Status |
| --- | --- | --- |
| Collaborator reads or writes outside their grant | Claims-first authorization, client identity fields rejected, per-board (not global) SignalR | **shipped** as an invariant in `CLAUDE.md` / `docs/STATUS.md`; **not re-measured in this draft (unverified here)** |
| Authorization diverges under development sandbox settings | `AuthorizationService` takes a `DevelopmentSandboxSettings` dependency (`:11-16`) | **open — `#1866`** sandbox-mode authz divergence |
| Automation mutates a board without review | Proposal-first: approve, then separately execute; preview equals apply (ADR-0003, GP-06). ADR-0056 confirms the loop governs **non-human** actors only — humans edit their own boards directly | **shipped** |
| Proposals persisted before permission validation | — | **open — `#1433`**, fix PR `#2219` in flight |
| Writes to archived history | `CardService` and the bulk writers reject archived boards with `409`; the automatic expiry paths now honour the same guard | **shipped** — ADR-0063; `docs/STATUS.md` post-v0.2.0 `#2197` entry |

### B4 / B5 — API keys and MCP

| Threat | Current control | Status |
| --- | --- | --- |
| A stolen key confers full account power | Scopes validated on mint (`ApiKeyService.cs:33-52`) and enforced fail-closed on invocation; unknown or unauthorized direct invocations fail closed (`docs/MCP_SERVER.md:222-229`) | **shipped** |
| Key recoverable from the database | SHA-256 of the key at rest; only an 8-char prefix is plaintext (`ApiKeyService.cs:46,97`) | **shipped** — sound for a 62-alphabet random key, not for a guessable one |
| An agent silently applies board changes | The MCP surface has no approve or apply tool (`docs/MCP_SERVER.md:3`) | **shipped** |
| The stdio server acts as the wrong user | `McpServer:DefaultUserId` must name an existing **active** local user; empty, zero, malformed, missing or inactive fails closed without trying another account (`StdioUserContextProvider.cs:33-86`; `CONFIGURATION_REFERENCE.md:849`) | **shipped** |
| Runtime tool-hash approval | Explicitly not claimed (`docs/MCP_SERVER.md:229`) | **out (this release)** |

### B6 — LLM egress and spend

| Threat | Current control | Status |
| --- | --- | --- |
| Private transcript content leaves the device | Live providers off by default (`Llm__EnableLiveProviders=false`, `Llm__Provider=Mock` in the compose stack); egress disclosure documented (ADR-0055; `SELF_HOST_TUNNEL_GUIDE.md:107`) | **shipped** |
| Runaway spend | `LlmQuota:RequestsPerHour` 60, `TokensPerDay` 100000, optional `GlobalBudgetCeilingTokens` (`CONFIGURATION_REFERENCE.md:349-353`) | **shipped** — the global ceiling defaults to unlimited |
| Concurrent callers overshoot the budget | Reservation of `ReservationEstimatedTokens` (default 2000) with a TTL sweep (`:352-353`) | **open — `#1435`** LLM quota reservation TOCTOU |
| A provider call hangs and pins resources | Per-client `HttpClient.Timeout` (`Api/Extensions/LlmProviderRegistration.cs:136,164,194`); connector calls 10s (`Application/Connectors/ConnectorExecutionService.cs:20`) | **shipped** |
| Provider-side retention or compromise | Disclosure only | **accepted residual** |
| Prompt injection from ingested content | See `UNTRUSTED_ARTEFACT_THREAT_MODEL.md` | **partial** — the hostile-fixture suite is still not bound to the effective prompt/parser path (`#1323`) |
| Extraction bomb (archive / PDF / OCR amplification) | — | **open — `#1429`**. Extraction is currently **not wired to any request path**, so the exposure is latent rather than live |

### B7 — local machine

| Threat | Current control | Status |
| --- | --- | --- |
| Connector credentials readable from disk | AES-256 with `Connectors:EncryptionKey`; Production **refuses to start** without it, Development uses a deterministic fallback (`CONFIGURATION_REFERENCE.md:824`; `Api/FirstRun/FirstRunBootstrapper.cs:646-653`) | **shipped** |
| Silent key rotation destroying stored credentials | Startup stops instead of rotating (`CONFIGURATION_REFERENCE.md:200`) | **shipped** |
| JWT theft via XSS or local access | Token in `localStorage` (ADR-0009, per `LAN_DEVICE_ACCESS_GUIDE.md:295`) | **accepted residual** |
| SQLite file copied off the machine | No database-level encryption | **accepted residual** — this is what makes `#1653` material |

### B8 — distribution

| Threat | Current control | Status |
| --- | --- | --- |
| Tampered or spoofed download | SHA-256 file plus a custom provenance record, built from a pinned checkout; **no Authenticode signature, no user-grade installer, no release SBOM, no GitHub attestation** | **partial — `#1167` wave (`#2148`–`#2152`)**, `RELEASE_TRUST_AND_DISTRIBUTION.md` "Current truth" |
| Prerelease semantics on the release lane | — | **open — `#2217`** release lane prerelease semantics |

## 5. Residual risks for the beta — the honest list

1. **The perimeter is procedural.** Taskdeck cannot tell whether an operator actually put an
   identity policy in front of the tunnel. A misconfigured beta instance is internet-reachable with
   the shipped `Open` registration default.
2. **MFA TOTP seeds are plaintext at rest** (`#1653`). Anyone holding the SQLite file can mint valid
   codes; recovery codes are hashed, the seed is not.
3. **Prompt injection is mitigated, not solved**, and the hostile-fixture suite is still not bound to
   the effective prompt/parser path (`#1323`). Human review remains a load-bearing control.
4. **Authorization has a known sandbox divergence** (`#1866`) and a known persist-before-validate
   ordering defect (`#1433`, fix in flight as PR `#2219`).
5. **Quota is bounded, not exact** (`#1435`); worst-case overshoot is roughly one call's real usage
   beyond the estimate per boundary crossing.
6. **Availability is unprotected beyond rate limits** — one SQLite file, no per-account resource
   caps, no isolation between accounts.
7. **Artefacts are unsigned**, with no SBOM or attestation; users must verify a SHA-256 by hand and
   click through a SmartScreen warning.
8. **Plain HTTP is a documented supported posture** for LAN testing, and the JWT travels over it.
9. **Cross-user isolation is asserted but not adversarially proven at beta scale** — no third-party
   penetration test, no fuzzing of the authorization surface. *Unverified.*
10. **Client-side robustness gaps can lose user work** — a 401 hard-navigation destroys an
    in-progress capture draft (`#2142`) and the Review poll lacks a per-poll deadline (`#2214`).
    Not confidentiality defects, but trust defects in a beta.

## 6. Operator guidance — minimum safe posture for a self-hosted beta

1. **Registration.** Start in `InviteOnly`, register yourself first (the first registration claims
   the bootstrap slot), mint one invite per person, then switch to `Closed` and recreate the
   container. Verify that a fresh `POST /api/auth/register` is refused
   (`SELF_HOST_TUNNEL_GUIDE.md:87-106`).
2. **Perimeter.** Never expose the origin behind only an unlisted URL. Use a named Cloudflare tunnel
   fronted by a Cloudflare Access application, or `tailscale serve` inside an ACL-scoped tailnet —
   **never** Tailscale Funnel and never a quick tunnel beyond a minutes-long smoke test. Verify from
   an identity outside the policy that the login page is unreachable
   (`SELF_HOST_TUNNEL_GUIDE.md:53-81`).
3. **LAN access is testing-only.** Trusted network, firewall rule scoped and removed in teardown
   (`LAN_DEVICE_ACCESS_GUIDE.md:23`, `:251-273`).
4. **Key custody.** Supply `Connectors:EncryptionKey` and `Jwt:SecretKey` from the environment or a
   secret store, never the repository. Back both up *separately from the database* — losing the
   connector key makes stored credentials unrecoverable, and the app refuses to start rather than
   rotate.
5. **Backups.** The SQLite file plus the two keys. Treat the file as credential-bearing: today it
   contains plaintext TOTP seeds (`#1653`).
6. **API keys.** Mint the narrowest scope that works — `Read` or `Propose`, not `Full` — and set an
   expiry. Rotate on any suspicion; the plaintext is shown once.
7. **LLM spend.** Keep live providers off unless needed. When on, set
   `LlmQuota:GlobalBudgetCeilingTokens` as well as the per-user limits (the global ceiling defaults
   to unlimited) and set a hard spend cap at the provider too.
8. **MFA.** Turn on `MfaPolicy:EnableMfaSetup` and enroll, accepting the seed-at-rest residual.
9. **Downloads.** Verify the published SHA-256 and expect the unsigned-binary warning
   (`docs/releases/WINDOWS_QUICK_START.md`, `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md`).

## 7. What would change this model

- **Opening registration to strangers.** Requires at minimum: a non-`Open` shipped default, or a
  startup refusal when `Open` is combined with a non-loopback bind; email verification or an
  equivalent; per-account resource quotas; an abuse-response path; and `#1653` closed first. This
  document does **not** cover that configuration and must be rewritten before it is offered.
- **The trusted shared instance going live** (ADR-0061 Stage 1). Adds a real B3 adversary model, an
  availability commitment, an incident path and a data-retention statement. ADR-0061 is
  direction-only until `#1772`'s remaining human acts are recorded.
- **Delegated autonomy** (ADR-0057, direction only, with the maintainer's openness caveat).
  Auto-approval removes human review as the terminal control for prompt injection and for
  `#1433`-class ordering defects. It cannot ship without its own gate and a rewrite of the B6 and B9
  rows above.
- **Managed SaaS.** Out of scope entirely; a different document.
- **Signed artefacts** (`#2148`–`#2152`). Would retire residual risk 7 and change the B8 row.
