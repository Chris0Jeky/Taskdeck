# Taskdeck Terms of Service (Draft)

> **Status: DRAFT — NOT LEGALLY BINDING**
> This is a pre-launch working draft and has **not** been reviewed by qualified
> legal counsel. Do not deploy as the governing terms of any hosted Taskdeck
> instance without such review. Placeholder sections are marked
> `[LEGAL REVIEW REQUIRED]`. Operators launching a hosted Taskdeck instance
> must customize this document for their jurisdiction, pricing model, and
> acceptable-use posture before publishing it.

**Last updated:** 2026-04-23 (draft)
**Tracking issue:** `#548` (LEGAL-01)

> **⚠️ DRAFT — NOT IN USE.** This was prepared for a hosted cloud instance that is no longer planned (2026-06-13 archive pivot: Taskdeck is personal-use only, never distributed or hosted as a service). It is retained only as a template; any self-hosted deployment is the operator's sole responsibility. See `docs/STATUS.md`.

## 1. Scope

These terms, when adopted by an operator, govern use of a hosted Taskdeck
instance provided by `[OPERATOR LEGAL ENTITY — LEGAL REVIEW REQUIRED]`
("**we**", "**us**"). They do **not** govern:

- The open-source Taskdeck codebase, which is licensed under its repository
  license.
- Self-hosted deployments, where the operator of the deployment sets the terms
  applicable to their users.

By creating an account on a hosted Taskdeck instance, you agree to the terms
published at that instance's terms URL.

## 2. Beta disclaimer

Taskdeck is, at the time of this draft, a pre-launch product. During the beta
period:

- Features may change, be added, or be removed without notice.
- There is **no service-level agreement (SLA)**. Uptime, latency, and data
  durability are provided on a best-effort basis.
- Data loss is possible and you should export your data regularly using the
  in-product export endpoint (`GET /api/account/export`).
- The product may include features that are flagged, partially implemented, or
  behind config gates. Do not treat beta features as production-ready.

`[LEGAL REVIEW REQUIRED]` — operators should confirm with counsel whether the
beta disclaimer alone is sufficient in their jurisdiction, or whether a
separate beta agreement is needed.

## 3. Account eligibility

`[LEGAL REVIEW REQUIRED]` — operators should set age, jurisdiction, and
entity-type eligibility (e.g., "must be 16+", "must be a legal adult in your
jurisdiction", "must not be on a sanctions list"). A suggested baseline:

- You must be at least the age of digital consent in your jurisdiction (16 in
  most of the EEA; 13 in some other jurisdictions; 18 if required by the
  operator).
- You are responsible for keeping your credentials secure and for any activity
  performed under your account.
- One person or entity per account; do not share credentials.

## 4. Acceptable use

You agree not to use Taskdeck to:

- Violate applicable law, including data-protection, export-control, and
  intellectual-property law.
- Upload or process personal data of third parties without a lawful basis.
- Upload regulated categories of data (health, payment card, government ID,
  biometric, etc.) unless you have confirmed with the operator that the
  operator accepts that data on the service. The default posture is that these
  categories are **not** accepted.
- Attempt to disrupt the service, exceed documented rate limits, attempt to
  access another user's data, or probe the service for vulnerabilities outside
  a coordinated disclosure arrangement.
- Use the LLM/automation surface to generate content that is illegal,
  fraudulent, or designed to impersonate a specific real person.
- Resell, white-label, or sublicense the service without an explicit written
  agreement.

`[LEGAL REVIEW REQUIRED]` — operators may wish to add sector-specific or
jurisdiction-specific restrictions.

## 5. Your content and IP ownership

- You retain all rights in the content you create in Taskdeck (boards, cards,
  captures, chat messages, etc.). We do not claim ownership of your content.
- You grant us a limited, non-exclusive licence to host, process, and display
  your content only to the extent necessary to provide the service to you and
  the collaborators you authorize. This licence ends when you delete the content
  or the account, subject to the retention/backup caveats in the Privacy Policy.
- When you use a third-party LLM provider through Taskdeck (OpenAI or Gemini,
  when enabled by the operator), the provider processes the relevant content
  under its own terms. Review `SUB_PROCESSORS.md` and the provider's terms
  before enabling these features.
- Taskdeck, the Taskdeck name, and any operator-provided branding remain the
  property of the operator or its licensors.

## 6. Availability and changes

- We aim to keep the service available but do not guarantee uptime during the
  beta (see Section 2).
- We may change features, remove features, or discontinue the service. Where
  reasonably possible, we will announce material changes in advance and
  provide an opportunity for you to export your data.

## 7. Suspension and termination

- You may terminate your use at any time by deleting your account via the
  in-product account-deletion flow. Deletion triggers the data-erasure steps
  described in the Privacy Policy.
- We may suspend or terminate your account if you violate these terms,
  create a safety/security risk, or if required by law. Where reasonably
  possible, we will give notice and a chance to cure non-critical violations.
- `[LEGAL REVIEW REQUIRED]` — operators should specify notice periods, refund
  treatment (if any), and a coordinated-disclosure policy for security
  researchers.

## 8. Warranties and liability

`[LEGAL REVIEW REQUIRED]` — operators must replace this placeholder section
with liability language tailored to their jurisdiction. A starting point:

- The service is provided "as is" and "as available" during the beta, to the
  maximum extent permitted by law.
- We make no warranty of merchantability, fitness for a particular purpose,
  or non-infringement, except as required by non-waivable law (e.g., UK/EU
  consumer rights).
- To the maximum extent permitted by applicable law, our aggregate liability
  for the service is capped at a placeholder to be set by counsel (commonly the
  fees paid in the prior twelve months, with carve-outs for death/personal
  injury/fraud/intentional misconduct where required by law).

## 9. Indemnity

`[LEGAL REVIEW REQUIRED]` — standard mutual indemnity language is
jurisdiction-sensitive and must be drafted by counsel.

## 10. Governing law and jurisdiction

`[LEGAL REVIEW REQUIRED]` — operators must pick a governing law and venue.
Typical options include the operator's country of establishment with
appropriate carve-outs for consumer-protection law (e.g., UK/EEA consumers
retain the protections of their local law regardless of choice-of-law).

## 11. Changes to these terms

We may update these terms. Material changes will be announced in-product and
at the published terms URL. Continued use after a material change constitutes
acceptance; if you do not accept, you may terminate your account under
Section 7.

## 12. Contact

`[LEGAL REVIEW REQUIRED]`

- Operator legal entity: `[TO BE NAMED]`
- Notice address: `[TO BE ADDED]`
- Contact email: `[TO BE ADDED]`

---

**Out of scope for this draft:** final jurisdiction, final liability caps, final
indemnity language, consumer-rights carve-outs, refund/billing terms, enterprise
DPA terms, coordinated-disclosure policy. See `README.md` in this directory for
the launch checklist.
