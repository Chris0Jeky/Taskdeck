# Taskdeck Sub-Processors (Draft Register)

> **Status: DRAFT — NOT LEGALLY BINDING**
> This register lists the sub-processors that a hosted Taskdeck instance would
> engage, based on what the codebase actually integrates with today. It has
> **not** been reviewed by qualified legal counsel and contains placeholders
> (`[LEGAL REVIEW REQUIRED]`) that an operator must resolve before publication.
> Before sending EU/UK personal data to any sub-processor below, the operator
> must have a Data Processing Addendum (DPA) in place with that sub-processor.

**Last updated:** 2026-04-23 (draft)
**Tracking issue:** `#548` (LEGAL-01)

## How to read this register

- **Purpose** — why the sub-processor exists in the architecture.
- **Data categories** — concrete shapes of data that would cross the boundary.
- **Region** — processing region(s), once the operator chooses them.
- **Gated by** — the config toggle or deployment choice that enables use.
- **Default state** — whether this sub-processor is active in an out-of-the-box
  deployment of the codebase today.

Everything marked `[TO BE NAMED]` or `[LEGAL REVIEW REQUIRED]` must be resolved
before this register is published on a hosted instance.

---

## 1. Hosting and infrastructure

| Field | Value |
|---|---|
| Name | `[TO BE NAMED — LEGAL REVIEW REQUIRED]` |
| Purpose | Compute, storage, and network for the hosted Taskdeck instance. |
| Data categories | All application data (account records, board content, captures, proposals, chat history, audit logs, operational logs, backups). |
| Region | `[LEGAL REVIEW REQUIRED]` |
| Gated by | Deployment choice (no in-product toggle). |
| Default state | Not applicable to self-hosting; required for hosted cloud. |
| DPA | `[REQUIRED BEFORE LAUNCH]` |

**Notes for the operator:** this is the most consequential sub-processor.
Its region, encryption-at-rest posture, backup retention, and incident-response
SLA directly determine what the Privacy Policy can truthfully claim.

## 2. LLM providers (optional, off by default)

Taskdeck supports three provider modes: `Mock` (deterministic local, default),
`OpenAI`, and `Gemini`. The latter two are enabled only by explicit
configuration (see `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`).

### 2a. OpenAI

| Field | Value |
|---|---|
| Name | OpenAI, L.L.C. |
| Purpose | Generates chat responses, automation proposals, and tool-call arguments when the operator configures the OpenAI provider. |
| Data categories | User chat messages, capture content, and bounded board context (column names, card titles, card ID prefixes) constructed by `BoardContextBuilder`. Sent only for requests that route through the LLM flow. |
| Region | Subject to OpenAI's processing regions under its DPA. |
| Gated by | `Llm:Provider = OpenAI` and a configured API key (see `docs/platform/CONFIGURATION_REFERENCE.md`). |
| Default state | **Off.** Out-of-the-box deployments use the `Mock` provider and do not call OpenAI. |
| DPA | `[REQUIRED BEFORE LAUNCH if OpenAI is enabled]` |

### 2b. Google (Gemini)

| Field | Value |
|---|---|
| Name | Google LLC / Google Ireland Limited, depending on user region. |
| Purpose | Same as OpenAI above, when the operator configures the Gemini provider. |
| Data categories | Same as OpenAI above. |
| Region | Subject to Google's processing regions under its DPA. |
| Gated by | `Llm:Provider = Gemini` and a configured API key. |
| Default state | **Off.** |
| DPA | `[REQUIRED BEFORE LAUNCH if Gemini is enabled]` |

**Notes for the operator:** because LLM providers see user content, the Privacy
Policy and Cookie/Terms drafts in this directory already call out that LLM
providers are opt-in by the operator. Do not enable these in production without
a DPA and without verifying that your users are on notice.

## 3. Identity / OAuth providers (optional)

### 3a. GitHub (OAuth)

| Field | Value |
|---|---|
| Name | GitHub, Inc. |
| Purpose | Authenticate users who choose "Sign in with GitHub" when the operator enables the GitHub OAuth flow. |
| Data categories | The OAuth subject identifier and the minimum profile fields required to complete the login (e.g., GitHub username / stable ID). No repository content is read. |
| Region | Subject to GitHub's processing regions. |
| Gated by | GitHub OAuth client ID/secret configured server-side; the frontend only exposes the button when `/api/auth/providers` reports GitHub as enabled. |
| Default state | **Off.** |
| DPA | `[REVIEW WHETHER DPA OR STANDARD TERMS ARE APPROPRIATE]` |

Additional OIDC providers can be added here if the operator enables them via the
generic OIDC integration.

## 4. Email delivery

| Field | Value |
|---|---|
| Name | `[NONE BY DEFAULT]` |
| Purpose | Transactional email (e.g., registration confirmation, password reset, deletion receipt). |
| Data categories | Recipient email address, email body. |
| Region | N/A until a provider is chosen. |
| Gated by | Operator deployment choice. |
| Default state | **No email provider is integrated in the shipped codebase.** If the operator adds one (e.g., for password reset), it must be added to this register at the same time. |
| DPA | `[REQUIRED BEFORE LAUNCH if email is enabled]` |

## 5. Analytics

| Field | Value |
|---|---|
| Name | `[NONE BY DEFAULT]` |
| Purpose | Product analytics. |
| Data categories | N/A. |
| Region | N/A. |
| Gated by | Operator deployment choice + end-user opt-in consent. |
| Default state | **Off.** The codebase contains a consent-gated, cookie-free analytics composable (`useAnalyticsScript`) and a drafted telemetry taxonomy, neither of which is enabled by default. If the operator enables any analytics surface, it must be listed here, covered by a DPA if it processes personal data, and reflected in `COOKIE_POLICY.md`. |
| DPA | `[REQUIRED BEFORE LAUNCH if analytics is enabled]` |

## 6. Error and log aggregation

### 6a. Sentry (optional, off by default)

| Field | Value |
|---|---|
| Name | Functional Software, Inc. (Sentry) |
| Purpose | Error tracking and exception monitoring for backend and frontend. Captures unhandled exceptions, error diagnostics, and stack traces to help the operator identify and resolve production issues. |
| Data categories | Error/exception messages (PII-scrubbed before transmission — emails and JWTs are redacted by a `BeforeSend` hook), stack traces, error context (breadcrumbs with sensitive headers stripped), request metadata (HTTP method, URL path, status code). `SendDefaultPii` is hard-coded to `false`, so usernames, emails, IP addresses, and request bodies are never sent. Hostnames are suppressed (`ServerName` set to empty). On the frontend, Sentry is detected at runtime via `window.Sentry` — no Sentry SDK is bundled; the integration is opt-in and runtime-detected. |
| Region | Subject to Sentry's processing regions under its DPA. Sentry offers US and EU data residency; operator must select at provisioning time. |
| Gated by | **Backend:** `Sentry:Enabled = true` and a valid `Sentry:Dsn` in configuration (see `SentryRegistration.cs`, `docs/platform/CONFIGURATION_REFERENCE.md`). **Frontend:** operator must load the Sentry browser SDK on the host page; the Vue app detects `window.Sentry` at runtime and forwards exceptions if present (see `utils/errorReporting.ts`). |
| Default state | **Off.** Out-of-the-box deployments do not enable Sentry on either backend or frontend. No data is sent to Sentry unless the operator explicitly enables it. |
| DPA | `[REQUIRED BEFORE LAUNCH if Sentry is enabled]` |

### 6b. Other log aggregation (operator-chosen)

| Field | Value |
|---|---|
| Name | `[NONE BY DEFAULT]` |
| Purpose | Centralized log collection for operations (beyond error tracking). |
| Data categories | Operational logs (request IDs, timings, error diagnostics, redacted user identifiers). |
| Region | N/A until a provider is chosen. |
| Gated by | Operator deployment choice (external to the application code). |
| Default state | **No external log aggregator is integrated by default.** Logs are written to the standard .NET logging pipeline, which the operator can wire to a destination of their choosing. |
| DPA | `[REQUIRED BEFORE LAUNCH if logs are shipped to a third party]` |

## 7. Payment / billing

| Field | Value |
|---|---|
| Name | `[NONE — BETA IS FREE]` or `[TO BE NAMED if/when billing launches]` |
| Purpose | Subscription billing, payment processing. |
| Data categories | N/A during beta. |
| Region | N/A. |
| Gated by | Future billing launch. |
| Default state | **Not applicable.** Taskdeck has no billing surface today. |
| DPA | `[REQUIRED BEFORE LAUNCH if billing is introduced]` |

## Change log for this register

Maintain a dated list of additions, removals, and region/purpose changes here
once the register is published on a hosted instance. Each material change
should be reflected in the Privacy Policy's `Last updated` field and announced
to users as required by the applicable jurisdiction.

---

**Out of scope for this draft:** signed DPAs, named hosting provider,
named email/analytics/log-aggregation vendors, final processing regions,
change-notification SLA to users. See `README.md` in this directory for the
launch checklist.
