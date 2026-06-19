# Taskdeck Cookie and Local-Storage Policy (Draft)

> **Status: DRAFT — NOT LEGALLY BINDING**
> This is a pre-launch working draft describing what the shipped Taskdeck
> frontend actually stores in the browser today. It has **not** been reviewed
> by qualified legal counsel. Operators launching a hosted instance must
> validate this disclosure against their deployed configuration (they may have
> added a reverse proxy, analytics, or similar that changes the picture) and
> revise before publishing. Placeholder sections are marked
> `[LEGAL REVIEW REQUIRED]`.

> **⚠️ NOT IN USE — parked by the 2026-06-13 archive pivot.** Like the rest of the `docs/legal/` package, this draft is no longer planned — Taskdeck is personal-use only, never distributed or hosted as a service. Retained only as a template; any self-hosted deployment is the operator's sole responsibility. See `docs/legal/README.md` and `docs/STATUS.md`.

**Last updated:** 2026-04-23 (draft)
**Tracking issue:** `#548` (LEGAL-01)

## 1. Summary

Taskdeck's shipped frontend **does not set cookies** for day-to-day
authentication, analytics, or advertising. It uses the browser's `localStorage`
for a small number of strictly functional items (Section 3). However, the
backend **does** set one strictly necessary, short-lived HTTP cookie during
OAuth / OIDC sign-in handshakes (Section 2). No non-essential cookies are set,
so a cookie-consent banner is not strictly required for the product's default
surface.

`[LEGAL REVIEW REQUIRED]` — some EU DPAs apply ePrivacy / PECR-style consent
requirements to any non-essential *client-side storage*, not only to cookies.
Operators publishing this document should confirm with counsel whether their
jurisdiction requires consent for the non-essential items in Section 4. The
default configuration only uses *essential* items, so the question is only
live if the operator enables analytics.

## 2. Strictly necessary cookies

The Taskdeck backend sets a single HTTP cookie during OAuth / OIDC sign-in
flows. This cookie is strictly necessary for the external-authentication
handshake to complete and is exempt from consent under ePrivacy rules.

| Field | Value |
|---|---|
| Name | `.Taskdeck.ExternalAuth` |
| Purpose | Holds temporary authentication state while the browser is redirected to an external identity provider (e.g., GitHub, a configured OIDC provider) and back. Without it, the OAuth handshake cannot correlate the redirect response with the original sign-in request. |
| Set by | ASP.NET Core cookie authentication middleware (`AuthenticationRegistration.cs`). |
| Type | HTTP cookie (server-set via `Set-Cookie` header). |
| Attributes | `HttpOnly`, `Secure` (when served over HTTPS), `SameSite=Lax`. |
| Lifetime | 5 minutes (absolute expiry, no sliding renewal). The cookie is consumed and cleared once the handshake completes. |
| Sent to third parties | No. The cookie is scoped to the Taskdeck host and is never shared with external services. |
| Default state | **Only set when an OAuth / OIDC provider is configured and a user initiates external sign-in.** Not set during local username/password authentication. |

If the operator does not configure any OAuth or OIDC provider, this cookie is
never issued.

## 3. Essential browser storage (default, active)

These items are necessary for Taskdeck to function as the user requested.
They are set in `localStorage` under predictable keys.

| Item | Key | Purpose | Lifetime |
|---|---|---|---|
| Auth token | `taskdeck_token` | Holds the JWT used to authenticate API requests. Without this the app cannot stay signed in across reloads. | Persists until sign-out, explicit account deletion, or manual browser clear. Rejected and removed if structurally invalid. |
| Session metadata | `taskdeck_session` | Holds the signed-in user's ID, username, and email, displayed in the UI shell. | Same lifetime as the auth token. |
| Workspace mode | `taskdeck_workspace_mode` | Remembers whether the user opted into a particular workspace mode (guided / advanced). | Persists until the user changes mode or clears browser storage. |
| Workspace help dismissals | `taskdeck_workspace_help_dismissals` | Remembers which in-product help/tips the user has dismissed, so they don't reappear. | Persists until the user clears the dismissals or browser storage. |
| Feature flag overrides | `taskdeck_feature_flags` | Stores local feature-flag overrides set via DevTools / QA flows. Not expected in normal user sessions. | Persists until cleared. |
| Saved-view preferences | `taskdeck_saved_views` | Stores user-authored saved views (filters, groupings) for boards. | Persists until the user deletes the view or clears browser storage. |
| Archive-view UI hint | `taskdeck_archive_hidden_boards` | Remembers whether archived boards are hidden/shown in the sidebar. | Persists until the user toggles it or clears storage. |
| Demo-mode marker | `taskdeck_demo` | Set only when the operator enables demo mode and the user starts a demo session. Flags the current browser session as running against the demo dataset. | Persists until the demo session ends or the user clears storage. Not set in normal (non-demo) deployments. |

None of the items above are sent to third parties. They are all read and
written by the Taskdeck frontend, and they do not act as tracking identifiers.

## 4. Non-essential browser storage (off by default, opt-in)

The following items exist in the codebase but are **off unless explicitly
enabled**. If an operator enables analytics, the relevant items become active,
and consent handling must be followed.

| Item | Key | Purpose | Default state |
|---|---|---|---|
| Analytics-consent flag | `taskdeck_telemetry_consent` | Records whether the user has opted in to product analytics. | **Not written unless the user interacts with the consent UI.** The code explicitly refuses to auto-restore consent when the browser sends Do-Not-Track or Global Privacy Control signals. |
| Analytics script state | managed by `useAnalyticsScript` | Loads a third-party analytics script only after opt-in and only if the operator has configured one. The composable is cookie-free by design. | **Off by default.** No third-party analytics script is shipped or configured. |

If the operator enables analytics:

1. Update `SUB_PROCESSORS.md` with the analytics vendor.
2. Update Section 4 of this file to describe the analytics surface in concrete
   terms (vendor, categories of data collected, retention).
3. Add a consent banner/UI that matches the operator's jurisdiction's rules.
4. Confirm that the analytics vendor is cookie-free if this document continues
   to claim so, and remove the "cookie-free" claim otherwise.

## 5. Third-party cookies set by sub-processors

If the operator enables OAuth sign-in (e.g., GitHub), the third party may set
cookies on its own domains during the OAuth redirect flow. Those cookies are
governed by the third party's cookie policy, not this one. The hosted Taskdeck
instance itself does not mirror those cookies.

If the operator places Taskdeck behind a CDN, reverse proxy, or WAF, that
infrastructure layer may set essential infrastructure cookies (e.g., for
load-balancer stickiness or bot protection). `[LEGAL REVIEW REQUIRED]` —
operators should enumerate any such cookies here before publishing.

## 6. Your choices

- You can clear Taskdeck's essential storage by signing out or by clearing
  site data in your browser. Doing so will sign you out; your server-side
  account data is not affected.
- You can revoke analytics consent (if you ever granted it) via the
  in-product controls; this clears the consent flag and stops the analytics
  script.
- You can request data export and account deletion via the endpoints
  described in the Privacy Policy (Section 7).

## 7. Changes to this policy

Material changes (e.g., introduction of new analytics, a new category of
client-side storage, a change from `localStorage` to cookies) will be
announced in-product and reflected in the `Last updated` header of this file.

---

**Out of scope for this draft:** consent-banner UI design, analytics vendor
selection, CDN/WAF cookie enumeration, and jurisdiction-specific consent
mechanics. See `README.md` in this directory for the launch checklist.
