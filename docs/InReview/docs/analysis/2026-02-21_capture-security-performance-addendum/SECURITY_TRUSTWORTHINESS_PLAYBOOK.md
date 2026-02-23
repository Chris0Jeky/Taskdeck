# Security & Trustworthiness Playbook (Practical)

Last Updated: 2026-02-21  
Applies to: Taskdeck local dev, self-hosted Docker, and a future hosted SaaS.

This is a **practical** playbook: it focuses on the 20% of security work that buys 80% of real-world safety and user trust, while staying feasible for a solo developer.

---

## 1) Security goals (what “secure” means here)

### 1.1 Goals

1) **Cross-user isolation is correct** (no data leaks between users/boards).  
2) **Auth is robust** (no trivial token theft / replay / privilege escalation).  
3) **Data is protected** in transit, at rest (where feasible), and in logs.  
4) **Behavior is predictable** (no silent destructive actions; automation requires review).  
5) **Incidents are diagnosable** (audit trails + correlation IDs + stable error contract).

### 1.2 Non-goals (for MVP)

- Formal compliance programs (SOC2/ISO)  
- Enterprise SSO + MFA (can be later)  
- Perfect secrecy for a compromised host (impossible if attacker owns your machine)

---

## 2) Threat model (Taskdeck’s real threat surface)

### 2.1 Assets
- User account credentials (password hashes, tokens)
- Boards/cards/notes (personal and possibly sensitive)
- Automation proposals + diffs (can disclose structure and content)
- LLM prompts/responses (can contain raw notes/transcripts)
- Audit logs + system logs (often contain “accidental secrets”)

### 2.2 Trust boundaries
1) Browser (frontend)  
2) API server  
3) DB file (SQLite)  
4) External LLM provider (optional, gated)

### 2.3 Attackers (realistic)
- Opportunistic attacker on the public internet (if you expose the app)
- Malicious collaborator (in future collaboration features)
- XSS payload inside user content (cards, logs, descriptions)
- Local malware / compromised machine (hard to defend fully)
- Supply chain attacker (dependency compromise)

---

## 3) What Taskdeck already does well (keep + extend)

- JWT auth with server-side validation.
- Role/board access checks (AuthorizationService + controller access helpers).
- Queue + proposals enforce **review before execution** (this is a huge trust win).
- Nginx reverse proxy includes CSP, X-Frame-Options, nosniff, referrer policy.
- Live LLM providers are gated behind config flags.
- Correlation ID middleware exists.

---

## 4) Practical hardening roadmap (phased)

### Phase 0 — “MVP public demo safe” (do these first)
Goal: You can confidently demo this on a small server without obvious holes.

#### A) Centralize exception handling and make error responses consistent
**Why:** Unhandled exceptions leak stack traces and create inconsistent API responses.

- Add an exception-handling middleware or `UseExceptionHandler(...)` pipeline.
- Ensure *all* errors still respect the project error contract `{ errorCode, message }`.
- Add tests that assert:
  - 500 responses never contain stack traces
  - error contract is returned even for unhandled errors

(You already have a backlog issue for this: `API-06 centralized exception handling`.)

#### B) Rate limit auth + “expensive” endpoints
Target endpoints:
- `/api/auth/login` (brute force)
- `/api/auth/register` (spam)
- Capture / triage endpoints (can be DoS if you add LLM)

Implementation (practical):
- Add ASP.NET Core Rate Limiting middleware.
- Use per-IP for anonymous endpoints, per-user for authenticated endpoints.
- Start with conservative defaults and load test.

#### C) Token storage: stop treating localStorage as “good enough” for prod
MVP reality:
- Storing access tokens in `localStorage` is XSS-sensitive.

Practical path:
- Short term: keep localStorage but enforce strict CSP and never render unsanitized HTML.
- Medium term: switch to **HttpOnly, Secure, SameSite cookies** for access/refresh tokens + CSRF defenses.
- Add “session hardening” doc and treat it as a major security milestone.

(You already track this: `SEC-12 session-token storage hardening plan`.)

#### D) Input constraints (DoS guardrails)
- Add request body size constraints for “capture” endpoints (server side).
- Enforce max lengths:
  - capture text
  - card description
  - proposal diff preview
- Validate enum values and GUIDs strictly.

#### E) Logging redaction policy (trustworthiness)
- Do not log raw capture text or secrets.
- Never log auth headers.
- Avoid logging entire request bodies by default.

Add a short “logging policy” doc:
- what is allowed
- what is forbidden
- how to sanitize error messages

---

### Phase 1 — “Self-hosted trustworthy”
Goal: A technical user can self-host safely and feel confident.

#### A) Dependency security policy
- Enable Dependabot (GitHub).
- Add CI step:
  - `dotnet list package --vulnerable --include-transitive`
  - `npm audit` (or equivalent) for frontend
- Add an SBOM generation step later.

#### B) Secrets/config management baseline
- No secrets in repo.
- Use:
  - user-secrets for dev
  - env vars for containers
- Add a `SEC-10 secrets/configuration management baseline` doc:
  - where secrets live
  - rotation expectations
  - minimal entropy/length requirements

#### C) Data protection keys (for cookie auth / CSRF tokens / etc)
If you move to cookies, ASP.NET Core Data Protection becomes important:
- Ensure keys persist across restarts
- Protect keys at rest in production environments (e.g., vault/KMS or file permissions)

#### D) Audit log completeness for automation
You already write audit logs for executed proposal operations.
Extend this concept for “security relevant” events:
- login failures (rate-limited summary)
- password changes
- token invalidation (if added)
- proposal approvals/rejections

---

### Phase 2 — “SaaS readiness”
Goal: If you ever host this for other people, the foundation is there.

- Multi-tenancy strategy ADR (already planned)
- Organization/workspace isolation
- SSO/OIDC (issue exists)
- Optional MFA
- Data portability + deletion flow (issue exists)
- Backups + restore + DR runbooks
- Security incident playbook (lightweight)

---

## 5) OWASP-guided checklist mapping (do the high-leverage bits)

### 5.1 OWASP API Security Top 10 (2023) mapping
Use this as a “category checklist” for your endpoints:

- Broken Object Level Authorization → every `GET /resource/{id}` must check the user can access that object.
- Broken Authentication → protect login, tokens, session management, brute force defenses.
- Broken Object Property Level Authorization → don’t allow changing forbidden properties via DTOs (mass assignment).
- Unrestricted Resource Consumption → rate limiting, request size limits, pagination, quotas.
- Security Misconfiguration → consistent headers, env separation, disable dev-only swagger in prod, etc.

### 5.2 OWASP ASVS (use as a maturity target)
Don’t try to “do ASVS” all at once.
Pick **Level 1** controls as a baseline target for a personal product.

---

## 6) LLM-specific security & trust (this is where products die)

If you add live providers, the biggest practical threats are:

1) **Data exfiltration** (you sent sensitive notes to an external provider unexpectedly)  
2) **Prompt injection** (model obeys hostile input text and generates dangerous operations)  
3) **Trust collapse** (model output is wrong, unpredictable, or too opaque)

Mitigations that match Taskdeck’s architecture:

### 6.1 Consent + visibility
- Make “send to external model” an explicit opt-in.
- UI should clearly show:
  - what text is being sent
  - which provider/model
  - whether data is stored by provider (if known)
- Provide a “privacy mode” feature toggle.

### 6.2 Data minimization
- Only send the smallest text necessary:
  - prefer extracted bullets, not entire board dumps
- Strip:
  - emails/phone numbers (optional)
  - secrets-looking strings (basic regex)
- Never send access tokens.

### 6.3 Always keep the “proposal review” gate
- Never execute actions directly from LLM output.
- Always convert output to a proposal that requires explicit approval.

This is already Taskdeck’s strongest trust mechanism — keep it sacred.

### 6.4 Output validation + policy enforcement
- Treat model output as untrusted.
- Validate schema.
- Run policy engine checks.
- Run permission checks.

---

## 7) Concrete security milestones (issue seeds)

If you want to seed issues, these are cleanly scoped:

1) **API-06** Centralized exception handling that preserves `{ errorCode, message }`
2) **SEC-06** Rate limiting for auth + capture endpoints
3) **SEC-12** Token storage hardening plan (localStorage → cookies or refresh tokens)
4) **SEC-05** OWASP baseline hardening pass (headers, misconfig, DTO binding)
5) **SEC-09** Dependency vulnerability policy + CI gates
6) **SEC-10** Secrets/config baseline + developer ergonomics

---

## 8) Verification (how you know you didn’t regress security)

Minimum automated checks:

- API unauthorized matrix tests exist for every controller family.
- Cross-user access regression tests exist (403 vs 404 behavior consistent).
- Model validation errors return error contract (400 + ValidationError).
- No endpoint accepts actor IDs from the client (claims-first identity).

Minimum manual checks:

- Try to access another user’s board/card by guessing IDs.
- Run a small login brute force test and confirm rate limiting.
- Search logs for accidental secrets after common actions.

---

## 9) References (for later deep dives)

- OWASP API Security Top 10 (2023): https://owasp.org/API-Security/editions/2023/en/0x11-t10/
- OWASP ASVS: https://owasp.org/www-project-application-security-verification-standard/
- OWASP Secure Headers: https://owasp.org/www-project-secure-headers/
- OWASP CSP cheat sheet: https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html
