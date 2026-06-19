# Taskdeck Privacy Policy (Draft)

> **Status: DRAFT — NOT LEGALLY BINDING**
> This is a pre-launch working draft. It has **not** been reviewed by qualified
> legal counsel and must not be treated as the governing privacy policy for any
> hosted Taskdeck instance until such review is complete. Placeholder sections
> are marked `[LEGAL REVIEW REQUIRED]`. Operators launching a hosted Taskdeck
> instance must customize this document for their jurisdiction, hosting provider,
> retention policy, and sub-processors before publishing it.

**Last updated:** 2026-04-23 (draft)
**Tracking issue:** `#548` (LEGAL-01)

> **⚠️ DRAFT — NOT IN USE.** This was prepared for a hosted cloud instance that is no longer planned (2026-06-13 archive pivot: Taskdeck is personal-use only, never distributed or hosted as a service). It is retained only as a template; any self-hosted deployment is the operator's sole responsibility. See `docs/STATUS.md`.

## 1. Who this policy applies to

This draft is intended to describe how a hosted Taskdeck instance operated by
`[OPERATOR LEGAL ENTITY — LEGAL REVIEW REQUIRED]` processes personal data of
users of that hosted instance. It does **not** apply to:

- Self-hosted Taskdeck deployments, where the operator of the deployment is the
  data controller and must publish their own policy.
- The Taskdeck source code, which is distributed under its repository license.

## 2. What we collect

We group data by how it enters Taskdeck. Each item below is tied to a feature
that is actually implemented in the current codebase; anything aspirational is
marked.

### 2.1 Account data

When you register, we store:

- A username and email address you supply.
- A password hash (BCrypt). We never store your plaintext password.
- Optional external-login linkage if you sign in via a configured OAuth
  provider (e.g., GitHub). Only the provider's subject identifier and the fields
  required to complete the login are stored.
- Multi-factor authentication (MFA) secrets and recovery codes, if you enable MFA.

### 2.2 Board and workspace content

Anything you type into Taskdeck — boards, columns, cards, labels, comments,
captures/inbox items, chat messages, saved views, archive items, notifications
you receive, and preferences — is stored on our servers so we can render it back
to you. Board content may include whatever you choose to put in it; treat it as
"content the operator can see" and avoid putting regulated data (health, payment
card, government ID, etc.) into Taskdeck unless you have confirmed with the
operator that doing so is appropriate. `[LEGAL REVIEW REQUIRED]` — operators
should decide whether to prohibit specific data categories in `TERMS_OF_SERVICE.md`.

### 2.3 Capture, proposal, and LLM data

Taskdeck's core flow involves capturing short notes and letting an LLM propose
structured changes to a board, which you then review and approve. This means:

- Captures and inbox items you create are stored until you dismiss or process them.
- If an LLM provider is enabled (see Section 4), the text of your captures and
  relevant board context (column names, card titles, card IDs) may be sent to
  that provider to generate proposals. **LLM providers are disabled by default**
  (the shipped default is a deterministic local "Mock" provider) and must be
  turned on explicitly in configuration.
- Proposals, chat sessions, chat messages, tool-call metadata, and the audit
  record of what was approved, rejected, applied, or expired are stored so you
  can review history.

### 2.4 Audit and operational data

Taskdeck records an audit trail of mutations (who changed what, when) so users
can understand how their board got to its current state. It also records
operational logs (request IDs, timings, error diagnostics). Logs are passed
through a redaction layer intended to strip secret-like values before
persistence (see `docs/security/SECURITY_LOGGING_REDACTION.md`). Logs may still
contain usernames, IP addresses, and request metadata.

### 2.5 What we deliberately do **not** collect (by default)

- **Tracking cookies.** The current frontend does not set any tracking,
  analytics, or advertising cookies.
- **Third-party analytics.** The shipped product does not call a third-party
  analytics service by default. An opt-in, consent-gated, cookie-free analytics
  surface exists in the codebase but is **off** unless explicitly enabled.
- **Telemetry event stream.** A product-telemetry recording surface **is
  implemented** (`TelemetryController` — `POST /api/telemetry/events` +
  `GET /api/telemetry/config` — backed by `TelemetryEventService`, taxonomy in
  `docs/product/TELEMETRY_TAXONOMY.md`) but is **opt-in and OFF by default**
  (`TelemetrySettings.Enabled = false`) and **strips PII via an allowlist**.
  Recorded events are currently only logged — they are **not persisted or
  forwarded** to any analytics backend. With the default configuration, no
  telemetry is collected.

## 3. Legal basis for processing (GDPR)

For users within the UK/EEA, the lawful bases under Article 6 GDPR are drafted as:

- **Contract performance (Art. 6(1)(b))** — processing account data, board/workspace
  content, and captures/proposals is necessary to provide the service you
  requested.
- **Legitimate interests (Art. 6(1)(f))** — minimal operational logging and audit
  trail is processed on the basis of the operator's legitimate interest in
  operating a secure, debuggable service, balanced against user expectations.
- **Consent (Art. 6(1)(a))** — any optional analytics or telemetry is processed
  only on the basis of explicit opt-in consent that can be withdrawn at any time.
- **Legal obligation (Art. 6(1)(c))** — retention of limited records to comply
  with applicable law, where required.

`[LEGAL REVIEW REQUIRED]` — the specific balancing test for legitimate interests
and the legal-obligation retention periods depend on the operator's jurisdiction
and must be reviewed by counsel before publication. Special-category data
(Art. 9 GDPR) is out of scope: users should not place it into Taskdeck.

## 4. Sub-processors

See `SUB_PROCESSORS.md` for the current register. In short:

- **Hosting provider:** `[TO BE NAMED — LEGAL REVIEW REQUIRED]`.
- **LLM providers:** used only if the operator enables them. Supported: OpenAI
  and Google (Gemini). When enabled, the content of your chat messages and
  relevant board context is sent to the chosen provider subject to that
  provider's terms.
- **Email provider:** `[NONE BY DEFAULT — the codebase does not ship a transactional
  email integration. If the operator adds one, add it to the register.]`
- **OAuth providers:** optional (e.g., GitHub). When you sign in with an OAuth
  provider, that provider receives the fact of your sign-in attempt; we receive
  only the identifiers needed to complete the login.

Every named sub-processor should have a Data Processing Addendum (DPA) in place
with the operator before EU/UK personal data is sent to it. `[LEGAL REVIEW REQUIRED]`

## 5. Retention

`[LEGAL REVIEW REQUIRED]` — retention periods depend on the operator's hosting
provider and operational posture. As a working baseline for legal review:

- **Active account data and board content:** retained for as long as the account
  is active, plus a short grace period for account restoration after deletion
  requests, after which the data is deleted or irreversibly anonymized by the
  `AccountDeletionService` flow (see Section 7).
- **Audit logs:** retained alongside the user they describe. On account
  deletion, audit-log rows referencing the deleted user are anonymized rather
  than removed, so the audit trail remains coherent for other users who
  collaborated on shared boards.
- **Operational logs:** `[LEGAL REVIEW REQUIRED]` — no automatic purge is
  currently implemented in the codebase; operators must pick and enforce a
  retention window at the infrastructure layer (e.g., at the log aggregator).
- **Backups:** `[LEGAL REVIEW REQUIRED]` — backup retention and deletion-from-backup
  semantics depend entirely on the operator's chosen hosting and backup tools.

## 6. Security

- Passwords are stored as BCrypt hashes.
- Requests to protected endpoints require a valid JWT; tokens can be invalidated
  server-side when an account is deleted or deactivated, and an active-user
  middleware re-checks account status on each request.
- Rate limiting, CSRF/XSS baseline headers, and secret-redaction on logs are in
  place at the application layer (see `docs/security/`).
- **Encryption in transit** is required between browser and server for any
  hosted deployment. TLS termination is the operator's responsibility.
- **Encryption at rest:** `[LEGAL REVIEW REQUIRED — NOT CLAIMED BY DEFAULT]`
  The application itself does not perform application-layer encryption of
  board content at rest. Any at-rest encryption comes from the storage layer
  the operator chooses (e.g., encrypted volumes). Do not claim "encryption at
  rest" in a published policy unless the operator has actually enabled it at
  the infrastructure layer and the claim has been verified.

## 7. Your rights (GDPR / UK GDPR)

Users in the UK/EEA have rights including access, rectification, erasure,
restriction, portability, and objection. Taskdeck implements the following
primitives that support these rights today (delivery `#83` / `#666` in
`docs/STATUS.md`):

- **Data export (portability).** `GET /api/account/export` and
  `GET /api/account/export/stream` produce a versioned JSON archive
  (`version` `1.0` at the time of this draft) of your account-scoped records.
  The export covers: your profile (username, email, active flag, default role,
  account-creation timestamp); the board-access records that link you to
  shared boards (board ID, name, description, role, owner flag, timestamp) —
  *not* the full column/card/label/comment tree, which is board-owner-scoped
  and exported separately via the board-owner backup flow; your notifications
  (id, type, title, message, read state, timestamp); your capture/inbox items
  as metadata (id, status, request type, timestamp) — the original capture
  text is not included in this archive today; your automation proposals (id,
  status, summary, associated board ID, timestamp); your chat sessions (id,
  status, **count of messages**, timestamp) — individual chat message bodies
  are not currently included; your audit-trail rows (id, entity type, entity
  id, action, timestamp); your workspace preferences and notification
  preferences. The export format is versioned so future additions can be
  introduced without breaking integrations. `[LEGAL REVIEW REQUIRED]` —
  operators must decide whether the current scope satisfies the portability
  right in their jurisdiction, or whether additional fields (e.g., capture
  text, chat message bodies) must be added before publication.
- **Account deletion (erasure).** The account-deletion flow requires password
  re-authentication and a confirmation phrase, deletes personal content
  (notifications, captures, chat sessions/messages, external logins,
  preferences, board-access records), anonymizes residual references (audit
  logs, sole-owner-guarded board content), anonymizes the user record itself
  (username/email replaced with deleted-`<random>` placeholders), and
  invalidates previously-issued JWTs so stale tokens cannot be replayed.

  `[LEGAL REVIEW REQUIRED]` — **Known deletion-scope gaps.** The current
  `AccountDeletionService` implementation does **not** delete or anonymize the
  following categories of user-linked data during account deletion:
    - **Automation proposals.** Proposals generated for the user (stored in the
      Proposals table with the user's ID) are retained after deletion. These
      may contain board-context summaries and LLM-generated content attributable
      to the user.
    - **MFA credentials.** TOTP secrets and recovery codes associated with the
      user's multi-factor authentication setup are not explicitly removed by the
      deletion flow. While the account is deactivated and PII is scrubbed, the
      cryptographic MFA material may persist in the database.

  Both gaps must be resolved in the codebase before the erasure claim in this
  policy can be considered complete. Operators should not publish this policy
  without either (a) patching the deletion service to cover these records, or
  (b) disclosing the retention to users and documenting the lawful basis for
  keeping the data post-deletion.

Other rights (access, rectification, restriction, objection) are exercisable
by writing to the contact in Section 9. `[LEGAL REVIEW REQUIRED]` — operators
must define concrete SLAs (e.g., respond within 30 days) and a verification
procedure for rights requests.

Users in other jurisdictions (e.g., California CCPA/CPRA) have analogous rights;
the underlying export/deletion primitives apply, but the specific disclosures
and opt-out language vary and must be added by counsel.

## 8. International transfers

`[LEGAL REVIEW REQUIRED]` — if any sub-processor (including LLM providers)
processes data outside the UK/EEA, the operator must document the transfer
mechanism (adequacy decision, SCCs, UK IDTA, etc.) and include it here before
publication.

## 9. Contact

`[LEGAL REVIEW REQUIRED]`

- Operator legal entity: `[TO BE NAMED]`
- Postal address: `[TO BE ADDED]`
- Privacy contact email: `[TO BE ADDED]`
- Data Protection Officer (if required): `[TO BE NAMED OR EXPLICITLY MARKED NOT REQUIRED]`
- Supervisory authority (UK/EEA users have the right to complain to their local
  authority): `[OPERATOR TO NAME PRIMARY SUPERVISORY AUTHORITY]`

## 10. Changes to this policy

Material changes will be announced in-product and at the published location of
this policy. A changelog entry at the bottom of the published page is the
operator's commitment that prior versions remain reviewable.

---

**Out of scope for this draft:** publishing on a public domain, cookie-banner UI,
executed DPAs, final jurisdiction, final retention periods, final contact
details. See `README.md` in this directory for the launch checklist.
