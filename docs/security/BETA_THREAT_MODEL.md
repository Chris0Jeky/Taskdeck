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
| B1 | Public network → origin | Anonymous internet when a tunnel is open | Login, health and SignalR answer anyone who reaches the URL; the perimeter must be an identity/access policy, not an unlisted URL (`SELF_HOST_TUNNEL_GUIDE.md:55-61`). The perimeter provider itself is a separate boundary — B13. |
| B2 | Unauthenticated → authenticated | Registrant, then owner | Registration mode + invite (`RegistrationPolicyService.cs:33-80`), password login, optional TOTP. When an external identity provider is configured it is a second door into this boundary — B11. |
| B3 | User → user | Invited collaborator with a board grant | Claims-first authorization via `AuthorizationService` / `BoardAccessService`; roles carry a read/write lane split. |
| B4 | HTTP API-key holder | Scoped `tdsk_` key — `Read` / `Propose` / `Manage` / `Full` (`backend/src/Taskdeck.Domain/Enums/ApiKeyScope.cs:9-13`) | Key is SHA-256 at rest (`ApiKeyService.cs:97`); an 8-char prefix is stored for display (`:46`). |
| B5 | MCP stdio client | Local agent process | Identity comes from `McpServer:DefaultUserId` when it is set, and **falls back to the sole active local user when it is unset** — only zero or multiple active users fail closed (`Infrastructure/Mcp/StdioUserContextProvider.cs:33-45,68-88,91-117`). Every resolved context is granted `ApiKeyScope.Full` (`:48-52`). No approve/apply tool exists (`docs/MCP_SERVER.md:3`). |
| B6 | Taskdeck → LLM provider | OpenAI is the supported vendor-hosted provider, but **`OpenAiCompatible` is retained** (ADR-0055 decisions 1 and 6) alongside `Ollama` and `Mock` | Transcript-source triage may send bounded chunks off-device; mock is the default. Under `Llm:Provider = OpenAiCompatible` the API key **and** the transcript chunks go to whatever operator-configured third-party endpoint `Llm:OpenAiCompatible:BaseUrl` names — OpenRouter, Groq and DeepSeek are the documented examples (`docs/platform/LLM_PROVIDER_SETUP_GUIDE.md:156-222`; `CONFIGURATION_REFERENCE.md:294-296`). That endpoint's operator becomes a third party to assets 2 and 4, and the URL is SSRF/egress-validated but not vendor-vetted. **Egress is not only transcript chunks.** The same live provider serves Automation Chat: the registered egress classification for both `api.openai.com` and an `OpenAiCompatible` base URL is "LLM prompt with board context and user input" (`Api/Extensions/LlmProviderRegistration.cs:393-397`; `Application/Services/EgressRegistry.cs:164-168`), and `ChatService.SendMessageAsync` (`Application/Services/ChatService.cs:164`) sends the user's chat text together with board context on the `Chat` surface. Transcript-chunk egress is bounded per capture and only for transcript-source captures; chat/board-context egress follows **every** chat turn. |
| B7 | Local machine access | Anyone with the OS account | SQLite file, `appsettings.local.json`, connector key, JWT in `localStorage`. |
| B8 | Release channel → user | Downloader | Unsigned artefacts **and an unsigned, unattested GHCR image** (§4, B8). |
| B11 | External identity provider → Taskdeck identity | GitHub OAuth App, or any operator-configured generic OIDC provider | **Off unless configured** (`GitHubOAuth` needs both `ClientId` and `ClientSecret`; each `Oidc:Providers` entry needs `Authority`, `ClientId` and `ClientSecret` — `CONFIGURATION_REFERENCE.md:233-260`). When on, `GET /api/auth/github/login`/`callback` and `POST /api/auth/github/exchange`/`link`, plus `GET /api/auth/oidc/{provider}/login`/`callback` and `POST /api/auth/oidc/exchange` (`Api/Controllers/AuthController.cs:193,228,361,409,546,573,655`), create **or link** a local identity from provider claims (`AuthenticationService.ExternalLoginAsync`, `:168`). The IdP, its own account-recovery process and the stored client secret all join the login trust chain. |
| B12 | Taskdeck → telemetry / analytics vendors | Operator who opts in | Sentry, an OTLP collector, and a Plausible/Umami script — all three off by default; see §4, B12. |
| B13 | Perimeter provider → origin traffic | Cloudflare (Access / tunnel) or Tailscale | **Materially different trust.** `cloudflared tunnel --url http://localhost:8080` (`SELF_HOST_TUNNEL_GUIDE.md:70`) makes Cloudflare terminate the public TLS session and re-originate over **plaintext** to the local port, so Cloudflare is inside the confidentiality boundary. `tailscale serve 8080` (`:76`) terminates inside the tailnet instead. Threats and residuals are in the B1 table (§4). |
| B9 | Untrusted content → prompt/board | Artefact/transcript ingress | Delegated to `UNTRUSTED_ARTEFACT_THREAT_MODEL.md`. |
| B10 | Taskdeck → operator-chosen webhook endpoint | Board owner registering an outbound webhook | A **live runtime surface** (`docs/STATUS.md:496`), not a dormant registry entry: `POST /api/boards/{boardId}/webhooks` (`Api/Controllers/OutboundWebhooksController.cs:21,88`) registers a caller-supplied URL; board mutation events are queued as `OutboundWebhookDelivery` rows whose `Payload` carries **event metadata (identifiers) only** — delivery ID, event type, board ID, entity type, operation, entity ID and timestamp, with no card title, description or comment text (`Application/Services/OutboundWebhookService.cs:185-194`) — and is persisted in the same SQLite file (`Domain/Entities/OutboundWebhookDelivery.cs:15`), and `OutboundWebhookDeliveryWorker` POSTs them signed to that URL. The `SigningSecret` is stored **plaintext** (`Domain/Entities/OutboundWebhookSubscription.cs:16`; no converter in `Persistence/Configurations/OutboundWebhookSubscriptionConfiguration.cs:25`). |

## 4. Threats, current control, status

Status key: **shipped** (control exists and is exercised) · **partial** (control exists with a known
gap) · **open** (tracked issue, no control) · **out** (deliberately not in scope). Issue titles below
are taken from the task brief; the issues themselves were **not read** — treat their detail as
unverified.

### B1 — anonymous internet

| Threat | Current control | Status |
| --- | --- | --- |
| The **direct origin bypasses the perimeter entirely** | None in-product. The baseline compose stack publishes the proxy as `"${TASKDECK_PROXY_PORT:-8080}:8080"` (`deploy/docker-compose.yml:108-109`) — a bare host port with no interface prefix, so Docker binds it on **all** host interfaces — and the tunnel guide's `deploy/.env` sets `TASKDECK_PROXY_PORT=8080` (`SELF_HOST_TUNNEL_GUIDE.md:25`; `deploy/.env.example:6`). Cloudflare Access policy and Tailscale ACLs govern only traffic arriving *through* the tunnel; anyone who can reach the host on that port — LAN neighbour, co-tenant, a cloud VM with an open security group — gets the unfiltered app, `/health/ready` included | **open** — the tunnel is **not** a boundary for the origin itself. Bind the publish to loopback or firewall the port (§6.2) |
| Anyone who finds the tunnel URL reaches login / health / SignalR | Operator-side identity perimeter (Cloudflare Access, or a Tailscale tailnet with an ACL); Funnel and quick tunnels explicitly rejected for the private instance | **partial** — the control is *operator procedure*, not enforced by Taskdeck (`SELF_HOST_TUNNEL_GUIDE.md:55-81`) |
| The perimeter provider itself reads the traffic — **Cloudflare route** | None. The documented invocation is `cloudflared tunnel --url http://localhost:8080` (`SELF_HOST_TUNNEL_GUIDE.md:70`): Cloudflare terminates the public TLS session at its edge and re-originates over **plaintext HTTP** to the local port, so Cloudflare can observe request and response bodies, the submitted password and the `Authorization` bearer JWT | **accepted residual** — inherent to the deployment choice, not a defect; see B13 |
| The perimeter provider is an availability and policy dependency — **Cloudflare route** | None in-product | **accepted residual** — the Cloudflare account, the Access application's policy and Cloudflare's own uptime all gate reachability; account takeover or a policy edit is an authentication bypass, and the operator gets no in-product signal that either happened |
| Same two threats — **Tailscale route** | `tailscale serve 8080` (`SELF_HOST_TUNNEL_GUIDE.md:76`) keeps the session inside a WireGuard mesh terminating on the host, so the coordination service brokers keys and sees connection metadata rather than plaintext content | **materially better** — Tailscale remains an availability and ACL dependency, but not a content observer. **Never** Tailscale Funnel, which is public (`:60`) |
| Credential stuffing / brute force | Fixed-window `AuthPerIp`, production default **20 permits / 60 s** (`backend/src/Taskdeck.Api/appsettings.json:87-90`; `CONFIGURATION_REFERENCE.md:531`) — 120/60 s is the **Development** override (`appsettings.Development.json:64-67`); MCP has a separate authentication-failure budget and pre-auth concurrency cap (`:541-543`) | **partial** — behind a reverse proxy, `AuthPerIp` can collapse users into one bucket (`Api/Extensions/PipelineConfiguration.cs:57`) |
| Plaintext traffic on the LAN posture | None in-product; login works over plain HTTP and the JWT rides it | **partial (documented)** — `LAN_DEVICE_ACCESS_GUIDE.md:295`; teardown is the mitigation |
| Volumetric abuse / DoS | Rate limits only; one SQLite file, no per-account resource isolation | **partial** |
| Unauthenticated readiness probe is an unthrottled work amplifier | None. `HealthController.ReadyCheck` is `[AllowAnonymous]` with **no** `[EnableRateLimiting]` attribute (`backend/src/Taskdeck.Api/Controllers/HealthController.cs:75-77`) and does real work per request — an EF `CanConnectAsync` against SQLite (`:82-86`) plus two pending-queue counts (`:113-115`) — so any anonymous caller who reaches the origin can drive DB and queue queries at request rate. It also discloses queue depth, worker staleness and circuit-breaker state anonymously (the comment at `:56-59` records this) | **open (availability residual)** — the mitigation is perimeter-side: tunnel or firewall the origin so `/health/ready` is not on the public perimeter (§6.2), and point container/orchestrator probes at the loopback bind rather than a published port |

### B2 — registration and the account boundary

| Threat | Current control | Status |
| --- | --- | --- |
| Untrusted registrant creates an account on an exposed instance | `Open` / `InviteOnly` / `Closed` enforced in `RegistrationPolicyService.CheckNewUserEligibilityAsync` and `AuthorizeNewUserAsync` (`:33-80`), called from `AuthenticationService.cs:108,121,214,227`; `UsersController.CreateUser` refuses outside `Open` (`:78`) | **shipped**, but the **shipped default is `Open`** (`RegistrationSettings.cs:15`) |
| Invite replay, or a third account after the perimeter is set | Invite codes are hashed, one-time and expiring; the bootstrap slot is claimed even in `Open` so a later mode switch cannot reopen it (`RegistrationPolicyService.cs:63-74`) | **shipped** |
| Password compromise | BCrypt at library defaults (`Application/Services/IPasswordHasher.cs:12`) | **partial** — the work factor is not tuned or measured (*unverified*), and **there is no server-side password-strength enforcement at all**. The six-character minimum is a client-side form check in `frontend/taskdeck-web/src/views/RegisterView.vue:62-63`; `CreateUserDto.Password` carries no validation attribute (`Application/DTOs/UserDtos.cs:15-20`) and `AuthenticationService.RegisterAsync` validates only username and email presence before hashing whatever arrived (`:96-116`), so a direct `POST /api/auth/register` accepts a one-character password. BCrypt is the only protection — see residual 14 |
| Account enumeration via login timing | None. `LoginAsync` returns `AuthenticationFailed` immediately when `ResolveLoginCandidatesAsync` yields no candidate (`AuthenticationService.cs:46-48`), so an **absent** identifier never pays the BCrypt verify cost while a present one does — a measurable timing difference between the two. Only the fixed-window `AuthPerIp` limit slows the probe | **accepted residual** — an earlier draft credited a login-side BCrypt precheck here; the precheck at `AuthenticationService.cs:106` is `RegisterAsync`'s invite-eligibility check (deliberately run before hashing), not a login control |
| MFA secret theft from a copied database | TOTP seed stored as plain Base32 (`Domain/Entities/MfaCredential.cs:20-22`); recovery codes *are* BCrypt-hashed (`MfaService.cs:75`) | **open — `#1653`** MFA TOTP seeds unencrypted at rest |
| MFA not enforced | `MfaPolicy:EnableMfaSetup` defaults `false` and MFA is never forced (`CONFIGURATION_REFERENCE.md:270-271`) | **partial by design** |

### B11 — external identity providers (opt-in)

| Threat | Current control | Status |
| --- | --- | --- |
| Provider-account takeover becomes Taskdeck-account takeover | None in-product — this is the boundary's nature: a compromised GitHub or OIDC account signs in as the linked Taskdeck user | **accepted residual** — the surface is off unless configured, and the operator inherits the IdP's MFA and account-recovery posture wholesale |
| Attacker registers a provider account with a victim's email to seize the account | **Explicitly prevented.** `ExternalLoginAsync` never auto-links by email; an unlinked external login always creates a *new* account, and a colliding email is rewritten to `{provider}-{providerUserId}@external.taskdeck.local` (`AuthenticationService.cs:207-210,242-245`) | **shipped** |
| External login bypasses the registration perimeter | The same `RegistrationPolicyService` eligibility and authorization checks run for a new external account (`AuthenticationService.cs:214-235`); already-linked accounts sign in unaffected | **shipped** |
| A new external (GitHub/OIDC) sign-up under the recommended `InviteOnly`/`Closed` posture | Both callbacks build `ExternalLoginDto` without its optional `InviteCode`, so `CheckNewUserEligibilityAsync` receives `null` and rejects the new account; GitHub can still be *linked* after a local invite-based registration, generic OIDC has no linking endpoint at all | **limitation, by construction** — under the safe posture external providers work only for accounts that already exist locally (GitHub via linking); treat OIDC as unusable there until an invite-aware callback ships |
| GitHub withholds the user's email | A synthetic `{providerUserId}@users.noreply.github.com` address is stored (`Api/Controllers/AuthController.cs:313-315`) | **shipped** — but that address is not a deliverable contact and must not be treated as a verified one |
| Client secret disclosure | Configuration/environment custody only; the same rules as `Jwt:SecretKey` apply (`CONFIGURATION_REFERENCE.md:241,260`) | **partial** — an earlier draft of this row called a leaked client secret an *account-minting* capability. That is wrong, and the correction matters: the callback still requires a provider-validated principal. `GitHubCallback` calls `HttpContext.AuthenticateAsync("GitHub")` and returns `401` unless the handler succeeds, then takes every identity claim from that principal rather than from the request (`Api/Controllers/AuthController.cs:228-249`) — so holding the secret does not let an attacker assert an arbitrary provider identity. What it does confer is **Taskdeck's identity in the OAuth exchange**: the holder can perform the code-for-token exchange as this application, and can stand up a consent screen the real provider will honour — phishing and redirect/`returnUrl` abuse aimed at intercepting a legitimate user's authorization code. Rotation is an IdP-side act with no in-product prompt |

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
| The stdio server acts as the wrong user — **misconfigured or ambiguous** | When `McpServer:DefaultUserId` is *set* it must parse as a non-empty GUID (`StdioUserContextProvider.cs:37-44`) and name an existing **active** user, or resolution throws without falling back to another account (`:68-79`). With it *unset*, **zero** active users and **more than one** active user both throw (`:98-111`) | **shipped** — fail-closed |
| The stdio server acts as the wrong user — **unset with exactly one active user** | Not a failure path: with `McpServer:DefaultUserId` unset and exactly one active user, the provider silently selects that account (`StdioUserContextProvider.cs:91-96,113-117`) and hands it an `ApiKeyScope.Full` context (`:48-52`) | **partial by design** — convenient for model A (single-user portable), but it means any local process that can start the stdio server acts as that user at full scope, and the *same* configuration flips from "silently full access" to a startup error the moment a second active account exists (`CONFIGURATION_REFERENCE.md:849`) |
| Runtime tool-hash approval | Explicitly not claimed (`docs/MCP_SERVER.md:229`) | **out (this release)** |

### B6 — LLM egress and spend

| Threat | Current control | Status |
| --- | --- | --- |
| Private transcript content leaves the device | Live providers off by default (`Llm__EnableLiveProviders=false`, `Llm__Provider=Mock` in the compose stack); egress disclosure documented (ADR-0055; `SELF_HOST_TUNNEL_GUIDE.md:107`) | **shipped** |
| Runaway spend | `LlmQuota:RequestsPerHour` 60, `TokensPerDay` 100000, optional `GlobalBudgetCeilingTokens` (`CONFIGURATION_REFERENCE.md:349-353`) | **partial** — the ceiling defaults to unlimited **and is not a whole-instance cap**: it is evaluated **per `LlmSurface`**. The check reads `GetTotalTokensAsync(null, surface, …)` (`Application/Services/LlmQuotaService.cs:88-91`) and the reservation SQL filters `WHERE Surface = {surfaceValue}` (`Infrastructure/Repositories/LlmUsageRecordRepository.cs:149,183`), so every `LlmSurface` that actually reaches the quota path gets its own full ceiling. **Only two do:** `Chat` (`Application/Services/ChatService.cs`) and `CaptureTriage` (`Application/Services/LlmCaptureTriageExtractor.cs`). The enum's third member, `Worker` (`Domain/Enums/LlmSurface.cs:7-11`), has **no production caller** — a repo-wide grep for `LlmSurface.` across `backend/src` outside the enum declaration returns only `Chat` and `CaptureTriage` uses. Worst-case daily spend is therefore **up to 2 × the configured value** today, rising to 3 × only if a worker surface is ever wired up, and that is before the `#1435` reservation overshoot below |
| Concurrent callers overshoot the budget | Reservation of `ReservationEstimatedTokens` (default 2000) with a TTL sweep (`:352-353`) | **open — `#1435`** LLM quota reservation TOCTOU |
| A provider call hangs and pins resources | Per-client `HttpClient.Timeout` (`Api/Extensions/LlmProviderRegistration.cs:136,164,194`); connector calls 10s (`Application/Connectors/ConnectorExecutionService.cs:20`) | **shipped** |
| Provider-side retention or compromise | Disclosure only | **accepted residual** |
| Prompt injection from ingested content | See `UNTRUSTED_ARTEFACT_THREAT_MODEL.md` | **partial** — the hostile-fixture suite is still not bound to the effective prompt/parser path (`#1323`) |
| Extraction bomb (archive / PDF / OCR amplification) | — | **open — `#1429`**. Extraction is currently **not wired to any request path**, so the exposure is latent rather than live |

### B12 — opt-in telemetry and analytics egress

| Threat | Current control | Status |
| --- | --- | --- |
| Error reports carry user content to Sentry | Off by default — `Sentry:Enabled` **and** a non-empty DSN are both required (`Api/Extensions/SentryRegistration.cs:28,33-40`); the egress registry declares `*.ingest.sentry.io` as "Error reports with stack traces and request metadata", `MetadataOnly` (`Application/Services/EgressRegistry.cs:183-188`) | **partial** — that classification is a *declaration*, not a scrubber. Exception messages and request context can still carry board or capture strings, and nothing in the pipeline proves otherwise; treat enabling Sentry as consenting to content egress |
| Traces and metrics reach an operator-named OTLP collector | Off unless `Observability:OtlpEndpoint` parses as an absolute URI, then gRPC to that endpoint only (`Api/Extensions/ObservabilityRegistration.cs:42-48,67-73`) | **partial** — span and metric attributes are operational rather than content, but the endpoint is unvetted and **not** seeded in the egress registry, so it is undeclared egress |
| A third-party analytics script runs in the app origin | Consent-gated and HTTPS-only: the script is injected only after telemetry consent and a valid `https` `scriptUrl`, and and the `<script>` element is removed if consent is withdrawn (`frontend/taskdeck-web/src/composables/useAnalyticsScript.ts:41-44,57-58,74-88`). **Withdrawal stops future loads; it does not stop the script already running.** `removeScript` detaches the DOM element by id and nothing else (`:163-168`, and the component-scoped twin at `:91-96`), so any listener, timer, global or in-flight beacon the vendor script already installed keeps running — **a page reload is required** before withdrawal takes effect; the backend settings are off by default (`Application/Services/AnalyticsSettings.cs:8-29`) | **partial** — consent does not contain the script: anything running in the app origin can read the JWT in `localStorage` (B7). Self-host the Plausible/Umami instance, or leave analytics off **In the checked-in Compose deployment this integration is latent:** nginx sends `script-src 'self'; connect-src 'self'` and the API's production CSP matches, so a cross-origin Plausible/Umami script is blocked before it executes and a same-origin copy cannot beacon elsewhere — it functions only with a same-origin proxy or a deliberately widened CSP, which is then the boundary to model |
| The operator cannot enumerate where data goes | The egress registry is the single declaration point (`Application/Services/EgressRegistry.cs:159-204`) | **partial** — it seeds `*.ingest.sentry.io` and `*.plausible.io` but covers neither an OTLP collector nor a self-hosted Plausible/Umami host, which is the configuration this document recommends |

### B7 — local machine

| Threat | Current control | Status |
| --- | --- | --- |
| Connector credentials readable from disk — **headless Production** (Docker/GHCR, model B) | AES-256 with `Connectors:EncryptionKey`. `ShouldAutoGenerateConnectorKey => !isProduction \|\| !isHeadless` (`Api/FirstRun/FirstRunBootstrapper.cs:684-685`) is **false** here, so no key is generated and `ValidateProductionSecrets` throws (`:623,648-657`). The operator must supply the key out of band | **shipped** — fail-closed |
| Connector credentials readable from disk — **non-headless Production** (the desktop exe, model A) and every non-Production environment | The app does **not** fail fast. `RunFirstRunChecks` (`Api/Program.cs:363`) runs *before* `ValidateProductionSecrets` (`:369`) and auto-generates a random 256-bit key, persisting it to `appsettings.local.json` (`FirstRunBootstrapper.cs:811-818,963-1005`), so validation always finds a key. The key then sits **next to the database on the same disk**, defeating the AES-at-rest control against anyone with the OS account | **partial** — deliberate (a self-contained exe stays runnable), but it is *not* a "Production refuses to start" guarantee. Model B is the only one that enforces external key custody |
| Silent key rotation destroying stored credentials | Startup stops instead of rotating (`CONFIGURATION_REFERENCE.md:200`) | **shipped** |
| JWT theft via XSS or local access | Token in `localStorage` (ADR-0009, per `LAN_DEVICE_ACCESS_GUIDE.md:295`) | **accepted residual** |
| SQLite file copied off the machine | No database-level encryption | **accepted residual** — this is what makes `#1653` material |

### B10 — outbound webhooks

| Threat | Current control | Status |
| --- | --- | --- |
| A registered endpoint is used to reach the host's own network (SSRF) | `OutboundWebhookEndpointGuard` blocks private/loopback/link-local addresses, `.local` / `.internal` / `.home.arpa` / `.localhost` / `.localtest.me` suffixes, cloud-metadata hosts and `nip.io`-class dynamic-DNS rebinding roots, resolving the host and rejecting when no address survives (`Application/Services/OutboundWebhookEndpointGuard.cs:9-43,45,71`); `OutboundWebhookConnectCallback` re-pins the connect target and the handler sets `UseProxy = false` (`docs/STATUS.md:264`) | **shipped** — with one configurable hole. The bound key is **`OutboundWebhooks:Security:AllowLocalhostEndpoints`**, not the `Connectors:…` name an earlier draft gave: `WorkerRegistration` binds `configuration.GetSection("OutboundWebhooks:Security")` onto `OutboundWebhookSecuritySettings` (`Api/Extensions/WorkerRegistration.cs:18-19`; `Application/Services/OutboundWebhookSecuritySettings.cs:5`). Its default differs by environment: when the key is **absent**, Development is forced to `true` (`WorkerRegistration.cs:20-23`) while every other environment takes the `bool` default `false`. No shipped `appsettings*.json` sets it, so Production and Staging are closed by default and Development re-opens loopback implicitly. Set it explicitly to `false` anywhere the instance is reachable |
| Board **event metadata** leaves the instance to a caller-chosen URL | The envelope carries identifiers only — delivery ID, event type, board ID, entity type, operation, entity ID, timestamp — and **no card title, description or comment text** (`Application/Services/OutboundWebhookService.cs:185-194`). Non-localhost endpoints **must** be `https` (`:227-246`); subscriptions are board-scoped and revocable, with secret rotation (`OutboundWebhooksController.cs:140`) | **partial** — narrower than card content, but not harmless: the receiver learns which boards exist, their entity IDs, and a precise timeline of activity, and those IDs are the join keys for anyone who also holds an export or an API key. The destination is the *operator's own* choice, so this is disclosure, not prevention |
| Delivery payloads readable from a copied database | None — `OutboundWebhookDelivery.Payload` is stored as plaintext beside the plaintext `SigningSecret` (`OutboundWebhookSubscription.cs:16`). The payload is event metadata rather than card text (`OutboundWebhookService.cs:185-194`), so a file-copy attacker gains a board-activity timeline, not card content | **accepted residual** — same root cause as `#1653`: no database-level encryption. The plaintext `SigningSecret` is the sharper half of this row |
| A stolen signing secret lets an attacker forge deliveries to the receiver | HMAC signing (`OutboundWebhookSignature.cs`); the secret is plaintext at rest and readable by anyone holding the SQLite file | **partial** — rotate via the endpoint above after any file exposure |
| A hostile or dead endpoint pins the worker | Bounded attempts with configured backoff then a dead-letter terminal state (`Api/Workers/OutboundWebhookDeliveryWorker.cs:323-340`); failure messages are redacted before persistence (`docs/STATUS.md:502`) | **shipped** |

### B8 — distribution

| Threat | Current control | Status |
| --- | --- | --- |
| Tampered or spoofed download | SHA-256 file plus a custom provenance record, built from a pinned checkout; **no Authenticode signature, no user-grade installer, no release SBOM, no GitHub attestation** | **partial — `#1167` wave (`#2148`–`#2152`)**, `RELEASE_TRUST_AND_DISTRIBUTION.md` "Current truth" |
| Tampered or substituted **container image** | `release-container.yml` publishes the GHCR image on `v*` tags under `{{version}}`, `{{major}}.{{minor}}` and `latest` (`.github/workflows/release-container.yml:92-96`) via `docker/build-push-action@v7` with **no checksum sidecar, no cosign signature and no `provenance` or `sbom` attestation** (`:98-110`) — the registry addresses it by digest, but nothing signs or attests it | **open** — the `#1167` wave (`#2148`–`#2152`) is scoped to *file* artefacts; container signing and attestation are not covered by it |
| A mutable tag is repointed under a running operator | None in-product. The operator mitigation is **digest pinning**: deploy the `@sha256:` digest form rather than `:latest` or `:0.2`, record the digest, and re-pin deliberately at upgrade time | **partial (operator procedure)** |
| `latest` follows a prerelease | **Closed by PR `#2223` (merged 2026-08-29, `ecc02a6ae`):** `release-container.yml` gates `latest` and `{{major}}.{{minor}}` on `!contains(github.ref_name, '-')`, so `v0.3.0-rc.1` publishes only `0.3.0-rc.1`, and `release-desktop.yml` flags the GitHub Release as a prerelease from draft creation through publish. Rehearsals cannot exercise the publish job; the first real prerelease tag is the proof | **shipped — verification pending** until the first real prerelease tag (`v0.3.0-rc.1`) publishes and the `:latest` digest is shown unchanged |

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
   caps, no isolation between accounts. The sharpest instance is `GET /health/ready`:
   `[AllowAnonymous]` with no rate-limiting policy attached
   (`Api/Controllers/HealthController.cs:75-77`), performing a DB connectivity check and two queue
   counts on every request (`:82-86,113-115`). The mitigation is perimeter-side only — tunnel or
   firewall the origin and keep `/health/ready` off the public perimeter (§6.2).
7. **Artefacts are unsigned**, with no SBOM or attestation; users must verify a SHA-256 by hand and
   click through a SmartScreen warning. **The GHCR image is worse off than the file artefacts** — it
   has no checksum sidecar at all (`latest` no longer follows prereleases since PR `#2223`). Pin by
   digest.
8. **Plain HTTP is a documented supported posture** for LAN testing, and the JWT travels over it.
9. **Cross-user isolation is asserted but not adversarially proven at beta scale** — no third-party
   penetration test, no fuzzing of the authorization surface. *Unverified.*
10. **The perimeter provider is inside the boundary on the Cloudflare route.** Cloudflare
    terminates TLS and re-originates in plaintext to the local port, so it can observe credentials
    and content, and its account, policy and availability gate access. Tailscale Serve does not
    have this property.
11. **Opt-in telemetry can carry content.** Sentry's `MetadataOnly` classification is a
    declaration, not a scrubber; an OTLP collector is undeclared egress; and an analytics script
    in the app origin can read the `localStorage` JWT.
12. **The stdio MCP server grants `Full` scope to the only active user when `DefaultUserId` is
    unset.** It is not unconditionally fail-closed — only zero or multiple active users fail.
13. **Client-side robustness gaps can lose user work** — a 401 hard-navigation destroys an
    in-progress capture draft (`#2142`) and the Review poll lacks a per-poll deadline (`#2214`).
    Not confidentiality defects, but trust defects in a beta.
14. **There is no server-side password-strength enforcement.** The six-character minimum lives only
    in the registration form (`RegisterView.vue:62-63`); the API hashes whatever it is handed
    (`AuthenticationService.RegisterAsync:96-116`). BCrypt at library defaults is the only thing
    between a weak password and an offline cracker holding the SQLite file.
15. **Withdrawing analytics consent does not tear down the loaded script** — it removes the
    `<script>` element only (`useAnalyticsScript.ts:163-168`); listeners, timers and globals the
    vendor script already installed run until the page is reloaded.

## 6. Operator guidance — minimum safe posture for a self-hosted beta

1. **Registration.** Start in `InviteOnly`, then **mint the first invite from the CLI before you
   register** — `taskdeck invite create --expires 7` (`backend/src/Taskdeck.Cli/Commands/InvitesCommandHandler.cs:21-24`;
   `CONFIGURATION_REFERENCE.md:220`). There is **no unauthenticated bootstrap exemption**: outside
   `Open`, `RegistrationPolicyService.CheckNewUserEligibilityAsync` requires a well-formed, available
   invite for *every* registration including the first
   (`backend/src/Taskdeck.Application/Services/RegistrationPolicyService.cs:33-55`), so a fresh
   `InviteOnly` or `Closed` instance answers the very first `POST /api/auth/register` with `403`.
   Register yourself with that invite (the registration claims the bootstrap slot), mint one invite
   per person and stay in `InviteOnly` **until every named person has redeemed theirs** — once the
   bootstrap slot is taken, `Closed` refuses every registration *before* any invite is looked up
   (`RegistrationPolicyService.cs:33-55`), so switching early disables the unredeemed invites. Only
   then switch to `Closed` and recreate the container, and verify that a fresh
   `POST /api/auth/register` is refused (`SELF_HOST_TUNNEL_GUIDE.md:87-106`).
2. **Perimeter — and bind the origin to loopback.** Never expose the origin behind only an unlisted
   URL. Use a named Cloudflare tunnel fronted by a Cloudflare Access application, or
   `tailscale serve` inside an ACL-scoped tailnet — **never** Tailscale Funnel and never a quick
   tunnel beyond a minutes-long smoke test. Verify from an identity outside the policy that the
   login page is unreachable (`SELF_HOST_TUNNEL_GUIDE.md:53-81`).

   **That is not sufficient on its own.** Neither an Access policy nor a tailnet ACL protects the
   direct origin: `deploy/docker-compose.yml:108-109` publishes `"${TASKDECK_PROXY_PORT:-8080}:8080"`,
   a bare port Docker binds on every host interface, and the guide's `deploy/.env` sets
   `TASKDECK_PROXY_PORT=8080` (`SELF_HOST_TUNNEL_GUIDE.md:25`). Close it by one of:
   - set **`TASKDECK_PROXY_PORT=127.0.0.1:8080`** in `deploy/.env`. Compose interpolates the variable
     into the whole left-hand side of the mapping, so this yields `"127.0.0.1:8080:8080"` — the
     host-IP form of the short syntax — and the tunnel, which connects to `http://localhost:8080`,
     still reaches it. Confirm with `docker compose -f deploy/docker-compose.yml --profile baseline ps`
     that the published address reads `127.0.0.1:8080` and not `0.0.0.0:8080`;
   - or edit the `ports:` line in `deploy/docker-compose.yml` to `"127.0.0.1:8080:8080"` directly, if
     you would rather not depend on that interpolation;
   - or leave the publish as it is and firewall the port to loopback at the host.

   Do this before the tunnel goes up, and re-verify after any compose or `.env` change. It is also
   what keeps the unthrottled anonymous `/health/ready` probe (B1) off the public perimeter.
3. **LAN access is testing-only.** Trusted network, firewall rule scoped and removed in teardown
   (`LAN_DEVICE_ACCESS_GUIDE.md:23`, `:251-273`).
4. **Key custody.** Supply `Connectors:EncryptionKey` and `Jwt:SecretKey` from the environment or a
   secret store, never the repository. Back both up *separately from the database* — losing the
   connector key makes stored credentials unrecoverable, and the app refuses to start rather than
   rotate.
5. **Backups.** **Never raw-copy the live database file.** SQLite runs in WAL mode, so committed
   rows sit in the `-wal` sidecar and a plain `cp` of `taskdeck.db` yields an incomplete or corrupt
   snapshot (`SELF_HOST_TUNNEL_GUIDE.md:201-208`). Take an **application-consistent** backup by one
   of two routes:
   - the SQLite online-backup API via `scripts/backup.sh` — **but `sqlite3` must be installed on
     whatever host runs the script.** The online-backup route is conditional: `if command -v sqlite3`
     (`scripts/backup.sh:137`). When `sqlite3` is missing the script does **not** stop; it warns and
     falls back to a sequential raw `cp` of the main database file followed by a separate `cp` of the
     `-wal` sidecar (`:144-151`) — two copies taken at different instants, with no lock and no
     checkpoint. **Treat that fallback as not writer-safe**; its output is unusable for a live
     database. It also skips the integrity check, which is itself `sqlite3`-gated (`:160`).
     So **verify each run's output names the online-backup route**: a good run prints
     `Method: sqlite3 hot backup (safe with active writers)` (`:143`) followed by `Integrity: ok`
     (`:167`). A run that prints `WARNING: sqlite3 not found. Falling back to cp.` (`:145`) is a
     **failed** backup — discard it, and either install `sqlite3` or take the stop-the-container
     route below. The script defaults to `~/.taskdeck/taskdeck.db`, so pass `--db-path` explicitly;
     for the Docker stack the guide's throwaway-container invocation
     (`SELF_HOST_TUNNEL_GUIDE.md:209-218`) is the supported form precisely because it `apk add`s
     `sqlite` first — the production image ships neither the script nor `sqlite3` (`#1772`);
   - or **stop the container first**, then copy `taskdeck.db` *together with* its `-wal` and `-shm`
     sidecars, and restore all of them together.

   Back the two keys up separately from the database, never in the same bundle
   (`SELF_HOST_TUNNEL_GUIDE.md:29-34`). Treat the database as credential-bearing: today it contains
   plaintext TOTP seeds (`#1653`), plaintext webhook signing secrets and persisted webhook payloads.
6. **API keys.** Mint the narrowest scope that works — `Read` or `Propose`, not `Full` — and set an
   expiry. Rotate on any suspicion; the plaintext is shown once.
7. **LLM spend.** Keep live providers off unless needed. When on, set
   `LlmQuota:GlobalBudgetCeilingTokens` as well as the per-user limits (the global ceiling defaults
   to unlimited) and set a hard spend cap at the provider too.
8. **MFA.** **Leave `MfaPolicy:EnableMfaSetup` at its `false` default for the self-hosted beta.**
   Production MFA remains blocked until `#1653` ships seed encryption, key management, migration,
   rotation and fail-closed handling (`docs/STATUS.md:219`): the TOTP seed is stored as plaintext
   Base32 (`Domain/Entities/MfaCredential.cs:20-22`), so enrolling would put a working second factor
   in every copy and every backup of the database — the seed-at-rest exposure is *created* by
   enrolling, and it buys no protection against the file-copy attacker it would be defending against.
   Revisit only when `#1653` has landed.
9. **Downloads.** Verify the published SHA-256 and expect the unsigned-binary warning
   (`docs/releases/WINDOWS_QUICK_START.md`, `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md`). For the
   Docker stack, **pin the image by digest** (`@sha256:`), not by `:latest` or a floating
   `major.minor` tag: the image carries no checksum sidecar, no signature and no attestation,
   and `latest` currently follows prereleases as well as stable tags
   (`.github/workflows/release-container.yml:92-96`).
10. **MCP stdio.** Set `McpServer:DefaultUserId` explicitly even on a single-user instance.
    Leaving it unset is not fail-closed — the provider selects the only active user and grants
    it a `Full` context (`StdioUserContextProvider.cs:91-96,113-117,48-52`) — and the same
    configuration starts failing the moment a second active account exists.
11. **Telemetry and analytics.** Leave Sentry, OTLP export and web analytics off unless you
    need them. Sentry's `MetadataOnly` classification is a declaration, not a scrubber; an
    OTLP endpoint is undeclared egress; and an analytics script runs in the app origin
    alongside the `localStorage` JWT, so self-host Plausible or Umami if you enable it.
12. **External login.** GitHub OAuth and OIDC are off unless configured. If you enable one,
    the provider's MFA and account-recovery posture becomes yours, and the client secret
    needs the same custody as `Jwt:SecretKey`.

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
