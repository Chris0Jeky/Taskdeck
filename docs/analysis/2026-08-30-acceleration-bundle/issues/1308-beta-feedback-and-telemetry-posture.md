# REVIVAL-12 — Beta feedback channel + opt-in telemetry posture (#1308)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

A user can reach the maintainer without the product reaching a server. v0.3 ships zero telemetry and says so in a file that is versioned with the code; v0.4 adds Home-Assistant-model opt-in analytics — off by default, consent card, instance UUID and aggregate counters only, self-hostable endpoint — and every network byte is documented before it is sent.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| Maintainer Option A/B ruling | **recorded** | Issue comment 2026-08-29T23:54:43Z: RC deck **q-5 = B**, opt-in Home-Assistant-style analytics for v0.4; v0.3 stays zero-telemetry. The body's AC3 was rewritten on 2026-08-30T22:12:55Z to match. Nothing here is inferred |
| `docs/TELEMETRY.md` (AC7) | **shipped** | 53 lines on `main`; enumerates every destination a v0.3 build can contact, the defaults that make that true, a self-check procedure, and names Option B as the v0.4 plan |
| GitHub Discussions (AC1) | **off** | `repos/Chris0Jeky/Taskdeck` → `has_discussions: false`, measured 2026-09-02. Still the single prerequisite for AC1, for #1310 AC6's participants metric, and for #2242's triage routing |
| In-app "Send feedback" (AC2) and diagnostic bundle (AC4) | **absent** | No match for `Send feedback`, `feedback`-named component, or a diagnostics/bundle action in `frontend/taskdeck-web/src` |
| Beta badge (AC5) | **absent** | No match for a beta badge in `frontend/taskdeck-web/src` or `README.md` |
| Endpoint ownership / retention / aggregate publication | **not decided** | Option B was ruled; its operational values were not. These are the real remaining blockers, and they are human-only |
| #2243 hosted open beta | open | Telemetry Option B lands in the same v0.4 milestone; the epic explicitly lists it as step 3 material |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `TLM-0-discussions` | Enable Discussions with the five AC1 categories; point README and `docs/TELEMETRY.md`'s reporting section at them | — | **No — human-only.** Enabling Discussions is a repository setting the maintainer flips; the agent slice is the README/doc wiring that follows |
| `TLM-1-feedback-url` | Deterministic prefilled-issue URL builder (app version, OS, feature area as query params) plus the menu affordance that opens it. Zero network calls from the app | TLM-0 for the Discussions target; can target the issue tracker meanwhile | **Yes.** The builder and its vitest suite are pure; only the destination URL depends on TLM-0 |
| `TLM-2-diagnostic-bundle` | User-triggered "copy diagnostic bundle": versions, OS, bounded recent log excerpt, redaction pass, shown to the user before it leaves the clipboard | — | **Yes.** Local-only by construction |
| `TLM-3-beta-badge` | Paper-styled Beta badge + version string in the app header | — | **Yes.** The 2026-08-23 comment already calls this independently pullable |
| `TLM-4-payload-contract` | Versioned allowlist payload record, instance UUID, opt-in state, **null sink**, redaction tests, `EgressRegistry` registration. No transport | TLM-3 (nothing hard) | **Yes**, but v0.4-scoped — do not ship it inside a v0.3 tag |
| `TLM-5-transport` | Bounded retry/backoff, short timeout, never blocks a product path, `TASKDECK_TELEMETRY=off`, kill switch | TLM-4 **and** the endpoint/retention/publication decisions | **No — blocked on human values** |
| `TLM-6-evidence` | Release-build network capture receipt + field-level sample payload published in `docs/TELEMETRY.md` | TLM-5 | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Declared egress inventory | `EgressRegistry` | **exists** | `docs/TELEMETRY.md` is explicit that it is a *declared* inventory enforced as an allowlist only where a client is wired through it (the LLM providers) — not a universal guard. A telemetry client must be wired through it, and the doc's wording must be updated in the same PR |
| Telemetry kill switch config | `Telemetry.Enabled=false`, `Analytics.Enabled=false` in `backend/src/Taskdeck.Api/appsettings.json` | **exists** | The off-by-default defaults `docs/TELEMETRY.md` cites |
| Content-security policy | `script-src 'self'; connect-src 'self'` in the production CSP | **exists** | A misconfigured analytics script cannot beacon from the shipped UI |
| Internal taxonomy | `docs/product/TELEMETRY_TAXONOMY.md` | **exists** | Internal vocabulary, **not** the user-facing disclosure; do not conflate the two |
| User-facing disclosure | `docs/TELEMETRY.md` | **exists** | AC7 is done for v0.3; the v0.4 payload dictionary is appended to this file, not to a new one |
| Feedback URL builder, diagnostic bundle, beta badge, consent card, payload record, transport | — | **new** | None exists |

## Implementation plan

**Preflight.** Read the four issue comments in order; the 2026-08-23 comment's "no decision recorded" state is superseded by the 2026-08-29 q-5 ruling. Do not restate the issue as blocked on Option A/B — it is not.

**v0.3 lane (agent-executable now):** TLM-1, TLM-2, TLM-3. All three are frontend-only plus a README line, all three are LIGHT-review docs/UI, and none touches a network path. Sequence them independently; they share only `frontend/taskdeck-web/src/components/ui/` primitives.

**v0.4 lane:** TLM-4 first as a contract-only PR (FULL review because it is the telemetry code path), then hold TLM-5 until the maintainer supplies endpoint owner/host/region/TLS, raw-event retention, aggregate publication cadence, and instance-UUID reset semantics. Record those four on this issue before writing transport code.

**Never:** a third-party SDK, a silent key, an install identifier beyond the documented instance UUID, or any content field. The issue's own trap list and `docs/TELEMETRY.md` both say so.

## Test plan

- [ ] Frontend: the feedback URL is deterministic and correctly percent-encoded for a title containing `&`, `#`, a newline and a non-ASCII character — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <feedback spec>`
- [ ] Frontend: the diagnostic bundle redacts a bearer token, an `sk-`-shaped key, a `tdsk_` API key and an e-mail address from the log excerpt; the bundle is rendered to the user before any copy
- [ ] Frontend: the Beta badge renders in both Paper and Legacy headers and carries the version string
- [ ] Backend (v0.4): the payload schema rejects an unknown field rather than dropping it; a denylist scan over the serialized payload catches any content-bearing key
- [ ] Backend (v0.4): with telemetry off there is **no DNS resolution attempt** — assert at the client seam, not by inspection
- [ ] Backend (v0.4): a 429, a 5xx, a TLS failure and a captive-portal redirect each leave the capture/review/apply result byte-identical
- [ ] Evidence: ten minutes of release-build use under a network capture, zero unexpected egress, receipt attached to the PR
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Offline, captive portal, corporate proxy with a custom CA, and a malformed endpoint configuration — all must be indistinguishable from "telemetry is off" to the product path.
- The opt-in toggle changes while a send is queued; the instance UUID is reset mid-flight.
- Clock skew between the instance and the endpoint (do not let a timestamp become an identifier).
- A diagnostic log excerpt that happens to contain a connector secret or a JWT.
- `TASKDECK_TELEMETRY=off` present but the settings UI says on — the environment override must win and the UI must say the environment overrode it.
- A v0.3 build upgraded in place to v0.4: consent must be asked, never inherited.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Docs draft | `docs/analysis/2026-08-30-acceleration-bundle/docs-drafts/TELEMETRY.md` | The never-send list and the "disabling telemetry never changes behaviour" invariant | Superseded as a whole by the shipped `docs/TELEMETRY.md`; harvest the never-send list only. Its "installation identifier hashed with a fixed namespace" contradicts the ruled **instance UUID** |
| Python candidate | `.../candidates/python/telemetry_payload_linter.py` + `telemetry-policy.sample.json` | The allowlist-plus-denylist shape and the "field paths, not free-form JSON" discipline | Path-only scanning (a content string inside an allowed field passes), snake_case field names, a 64-hex `installation_id` regex that rejects a UUID, and no CI wiring. See the defects table in `../candidates/README.md` |
| Test vector | `.../testing/test-vectors/telemetry-payload.sample.json` | A concrete shape for the v0.4 payload dictionary | Uses the 64-hex identifier the ruling does not authorize |
| Diagram | `.../diagrams/hosted-beta-gates.svg` | Where telemetry sits in the gate ladder (Stage 4 evidence, not a Stage 0/1 prerequisite) | Explanatory only |
| Blueprint | `.../architecture/HOSTED_BETA_READINESS_MODEL.md` §4 "Trust and telemetry" | The disclosure checklist for the hosted gate | Read its validation preface first |

## Corrections to the bundle

1. **Bundle pack:** "The remaining blockers are endpoint ownership, retention, public aggregate publication, and exact payload policy." **True on 2026-09-02:** this is right, and it is the *only* claim in the pack that survives — but the pack never says the Option A/B ruling itself is recorded and dated (q-5 = B, 2026-08-29T23:54:43Z), so a reader still treats the whole issue as decision-blocked. **Consequence:** the four sub-decisions are the blockers; the choice is not.
2. **Bundle pack TLM-2:** "Add feedback URL builder, diagnostic bundle, telemetry status/enable/disable command and TELEMETRY.md." **True:** `docs/TELEMETRY.md` **exists on `main`** (53 lines). **Consequence:** drop it from TLM-2; the v0.4 payload dictionary is an append to that file.
3. **Bundle pack file ownership:** lists `frontend/src/**/feedback*`. **True:** the frontend root is `frontend/taskdeck-web/src/`. **Consequence:** the ownership fence as written matches nothing.
4. **Bundle pack:** proposes a "local random installation ID". **True:** the recorded ruling says **instance UUID**, and the candidate linter enforces 64-hex. **Consequence:** pick the UUID; a hashed 64-hex identifier is a different disclosure and would contradict the ruling published in `docs/TELEMETRY.md`.
5. **Bundle pack:** treats `EgressRegistry` as the control that would cover a telemetry endpoint. **True:** `docs/TELEMETRY.md` states it is a *declared inventory*, enforced as a destination allowlist only where a client is wired through it. **Consequence:** wiring a telemetry client through it is real work, not a checkbox, and the doc sentence changes with it.
6. **Coordinator `docs-drafts/README.md` row for `TELEMETRY.md`** says "Blocked on the maintainer's Option A / Option B decision". **True:** that decision is recorded (q-5 = B). **Consequence:** the row should read "Option B ruled 2026-08-30; blocked on endpoint ownership, retention and aggregate-publication values" — reported here rather than edited, per this pass's contract.
