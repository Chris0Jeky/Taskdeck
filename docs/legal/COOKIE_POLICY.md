# Taskdeck Cookie and Local-Storage Policy (Draft)

> **Status: DRAFT — NOT LEGALLY BINDING**
> This is a pre-launch working draft describing what the shipped Taskdeck
> frontend actually stores in the browser today. It has **not** been reviewed
> by qualified legal counsel. Operators launching a hosted instance must
> validate this disclosure against their deployed configuration (they may have
> added a reverse proxy, analytics, or similar that changes the picture) and
> revise before publishing. Placeholder sections are marked
> `[LEGAL REVIEW REQUIRED]`.

**Last updated:** 2026-04-23 (draft)
**Tracking issue:** `#548` (LEGAL-01)

## 1. Summary

Taskdeck's shipped frontend **does not set cookies** for authentication,
analytics, or advertising. Instead, it uses the browser's `localStorage` for
a small number of strictly functional items. As a result, a cookie-consent
banner is not strictly required for the product's default surface, because
there is no non-essential cookie to consent to.

`[LEGAL REVIEW REQUIRED]` — some EU DPAs apply ePrivacy / PECR-style consent
requirements to any non-essential *client-side storage*, not only to cookies.
Operators publishing this document should confirm with counsel whether their
jurisdiction requires consent for the non-essential items in Section 3. The
default configuration only uses *essential* items, so the question is only
live if the operator enables analytics.

## 2. Essential browser storage (default, active)

These items are necessary for Taskdeck to function as the user requested.
They are set in `localStorage` under predictable keys.

| Item | Key | Purpose | Lifetime |
|---|---|---|---|
| Auth token | `taskdeck_token` | Holds the JWT used to authenticate API requests. Without this the app cannot stay signed in across reloads. | Persists until sign-out, explicit account deletion, or manual browser clear. Rejected and removed if structurally invalid. |
| Session metadata | `taskdeck_session` | Holds the signed-in user's ID, username, and email, displayed in the UI shell. | Same lifetime as the auth token. |
| Workspace mode | workspace-mode key | Remembers whether the user opted into a particular workspace mode (novice / advanced). | Persists until the user changes mode or clears browser storage. |
| Feature flag overrides | feature-flags key | Stores local feature-flag overrides set via DevTools / QA flows. | Persists until cleared. |
| Saved-view preferences | saved-views key | Stores user-authored saved views (filters, groupings) for boards. | Persists until the user deletes the view or clears browser storage. |
| Archive-view UI hint | archived-boards visibility key | Remembers whether archived boards are hidden/shown in the sidebar. | Persists until the user toggles it or clears storage. |

None of the items above are sent to third parties. They are all read and
written by the Taskdeck frontend, and they do not act as tracking identifiers.

## 3. Non-essential browser storage (off by default, opt-in)

The following items exist in the codebase but are **off unless explicitly
enabled**. If an operator enables analytics, the relevant items become active,
and consent handling must be followed.

| Item | Key | Purpose | Default state |
|---|---|---|---|
| Analytics-consent flag | consent key in `telemetryStore` | Records whether the user has opted in to product analytics. | **Not written unless the user interacts with the consent UI.** The code explicitly refuses to auto-restore consent when the browser sends Do-Not-Track or Global Privacy Control signals. |
| Analytics script state | managed by `useAnalyticsScript` | Loads a third-party analytics script only after opt-in and only if the operator has configured one. The composable is cookie-free by design. | **Off by default.** No third-party analytics script is shipped or configured. |

If the operator enables analytics:

1. Update `SUB_PROCESSORS.md` with the analytics vendor.
2. Update Section 3 of this file to describe the analytics surface in concrete
   terms (vendor, categories of data collected, retention).
3. Add a consent banner/UI that matches the operator's jurisdiction's rules.
4. Confirm that the analytics vendor is cookie-free if this document continues
   to claim so, and remove the "cookie-free" claim otherwise.

## 4. Third-party cookies set by sub-processors

If the operator enables OAuth sign-in (e.g., GitHub), the third party may set
cookies on its own domains during the OAuth redirect flow. Those cookies are
governed by the third party's cookie policy, not this one. The hosted Taskdeck
instance itself does not mirror those cookies.

If the operator places Taskdeck behind a CDN, reverse proxy, or WAF, that
infrastructure layer may set essential infrastructure cookies (e.g., for
load-balancer stickiness or bot protection). `[LEGAL REVIEW REQUIRED]` —
operators should enumerate any such cookies here before publishing.

## 5. Your choices

- You can clear Taskdeck's essential storage by signing out or by clearing
  site data in your browser. Doing so will sign you out; your server-side
  account data is not affected.
- You can revoke analytics consent (if you ever granted it) via the
  in-product controls; this clears the consent flag and stops the analytics
  script.
- You can request data export and account deletion via the endpoints
  described in the Privacy Policy (Section 7).

## 6. Changes to this policy

Material changes (e.g., introduction of new analytics, a new category of
client-side storage, a change from `localStorage` to cookies) will be
announced in-product and reflected in the `Last updated` header of this file.

---

**Out of scope for this draft:** consent-banner UI design, analytics vendor
selection, CDN/WAF cookie enumeration, and jurisdiction-specific consent
mechanics. See `README.md` in this directory for the launch checklist.
