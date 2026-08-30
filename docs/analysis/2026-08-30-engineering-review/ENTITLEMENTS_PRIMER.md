# Entitlement boundary primer

> **Non-normative design scaffold (2026-08-30).** This is not an Accepted ADR, a pricing plan, a
> licence change, or authorization to implement billing. [#2353](https://github.com/Chris0Jeky/Taskdeck/issues/2353)
> owns the decision-preparation surface; #2012 and the gates below block implementation.

## Purpose

Retain the useful core of the external gating architecture while fitting Taskdeck's modular
monolith, review-first trust model, local/self-hosted posture, and existing GPL commitments. The
design must support a future managed or separately licensed additive capability without letting
payment state become identity, resource authorization, or a kill switch for user-owned data.

## Immutable free boundary

`LICENSING.md` is binding until an authorized, legally reviewed decision changes it. These remain
in the GPL-covered free core:

- capture -> proposal -> review -> apply;
- data export and portability;
- bring-your-own API key and local-LLM use;
- single-user self-hosting; and
- already-shipped free capabilities, including MFA, OIDC, and board sharing.

Entitlement code should encode this as a monotonic core capability set plus tests. No subscription,
grant, lease, payment failure, provider outage, invalid webhook, operator override, or unknown plan
may turn a core capability into an entitlement denial. Authentication, resource authorization, and
security controls can still deny unsafe or cross-user operations for their own reasons; they must
not disguise those decisions as plan enforcement.

Reading existing data, exporting it, recovering it, and using security controls remain available
when paid extras are expired or unavailable. A paid feature may stop new provider-funded work while
preserving its existing receipts and user-owned outputs.

## Request and worker boundary

```text
authenticate
  -> authorize resource and subject
  -> resolve immutable core capability
  -> read local entitlement snapshot
  -> evaluate additive capability
  -> reserve allowance when the operation creates bounded cost
  -> execute
  -> commit or release reservation
  -> write receipt/audit
```

The server/application use case is authoritative. The frontend can explain a decision and avoid a
doomed request, but localStorage, route metadata, feature flags, or client payloads cannot grant a
capability.

Queued work repeats resource authorization, entitlement, expiry, and allowance checks when claimed.
Enqueue-time approval does not spend a future allowance or survive a downgrade indefinitely. MCP
tool discovery may hide unavailable additive tools for usability, but every invocation performs the
same application check. A worker or sidecar receives a bounded claim/receipt contract, never an
unscoped plan object.

## Decision model

A single permissive `IsAllowed` flag is insufficient. In particular, a value named `Degraded`
must not accidentally authorize new managed-cost work. The conceptual contract needs independent
read/start/fallback semantics:

```csharp
// Conceptual only; names and types are not yet repository contracts.
public sealed record CapabilityAccessDecision(
    bool CanReadExistingData,
    bool CanStartNewWork,
    CapabilityExecutionMode ExecutionMode,
    string ReasonCode,
    DateTimeOffset? RecheckAfter,
    long SnapshotVersion);

public enum CapabilityExecutionMode
{
    Core,
    Granted,
    LocalOrByoFallback,
    GraceReadOnly,
    Denied,
    Unavailable
}
```

Examples:

| State | Existing data | New managed work | Local/BYO fallback |
| --- | --- | --- | --- |
| Core capability | Allowed | Allowed under ordinary auth/security rules | N/A |
| Paid grant active | Allowed | Allowed after allowance reservation | As configured |
| Grace period | Allowed | Explicit product decision; default deny new managed cost | Prefer explicit fallback |
| Payment/provider outage | Allowed | Use last safe short-lived snapshot or deny new managed cost | Preserve |
| Expired/invalid offline lease | Allowed | Deny paid extra | Preserve |
| Resource authorization denied | Denied | Denied | Denied |

UI and API behavior branch on stable reason codes and modes, not prose messages or plan names.

## Module boundary

Keep one bounded Entitlements module inside the modular monolith until measured scale, ownership, or
licensing requires a process boundary.

| Layer | Owns | Must not own |
| --- | --- | --- |
| Domain/Application | capability keys, abstract subject ID, decisions, snapshot contract, allowance semantics | payment SDK types, checkout URLs, UI flags |
| Infrastructure | local snapshot store, plan/grant projection, SQLite allowance transaction, lease verification adapter | resource authorization policy |
| API/MCP/Workers | authenticated subject mapping, use-case invocation, claim-time recheck, stable explanation DTO | authoritative client-side grants |
| Billing adapter (future) | signature verification, durable webhook inbox, provider-ID vault/mapping, projection | synchronous checks on normal request paths |
| Frontend | explanatory snapshot, feature presentation, upgrade placeholder | security or entitlement authority |

Provider identifiers needed for reconciliation may require an encrypted adapter-owned mapping.
Hash-only storage is not automatically sufficient when the operator must call the provider, while
raw IDs must not leak into Domain/Application or logs.

## Subject model

`EntitlementSubjectId` should be an opaque application concept. Initial implementation must not
make `UserId` the permanent billing unit or assume that an unfinished workspace/project model is a
legal customer. The later mapping may be personal account, workspace, organization, installation,
or a managed-service tenant, with explicit migration and isolation tests.

Resource ownership is evaluated independently. Possessing an entitlement for subject A never
authorizes access to subject B's board, capture, export, or worker claim.

## Snapshot and grant precedence

Normal application requests read a versioned local `SubscriptionSnapshot`; they do not call Stripe
or another provider. A future evaluator should make precedence explicit and test every pair:

1. authenticate and authorize the resource;
2. if the capability is immutable core, allow under ordinary security/validation rules;
3. reject unknown capability keys;
4. apply explicit security/abuse controls separately from commercial state;
5. evaluate active plan grants and bounded operator/test grants;
6. apply expiry/revocation/downgrade rules;
7. select explicit fallback/read-only behavior;
8. return the snapshot version and recheck time.

Operator grants are useful for tests and support, but cannot revoke core. Emergency provider/egress
kill switches can stop the affected managed execution while preserving local/BYO paths, existing
data, exports, and recovery.

## Allowance lifecycle

Do not introduce generic allowances before a concrete costed capability exists. When one does, its
semantic contract is:

```text
reserve(idempotencyKey, subject, capability, amount, window)
  -> commit(actual amount, receipt)
  -> or release(reason)
  -> reconcile expired/orphaned reservations
```

Reservation and remaining-budget checks are one database transaction. A unique idempotency key
prevents retries from double-spending. State transitions are monotonic; commit/release are
idempotent; reservations expire; reconciliation is observable. Cancellation and worker crashes
release or expire safely. The provider-specific SQLite transaction stays behind one interface; a
PostgreSQL implementation is added only if PostgreSQL enters active deployment scope.

Keep LLM quota and generic allowance implementations separate until two consumers demonstrate a
stable shared model. Similar nouns are not sufficient evidence for a migration.

## Webhook projection

If a billing provider is later selected:

1. verify signature over the exact raw body;
2. write an idempotent inbox row before acknowledging;
3. preserve provider event identity and ordering metadata in the adapter boundary;
4. project into a provider-neutral local snapshot;
5. tolerate duplicates, retries, out-of-order events, refunds, pauses, and cancellation;
6. invalidate evaluator caches by snapshot version; and
7. expose operator reconciliation without payment PII in ordinary logs.

Checkout and customer portals come after inbox/projector correctness. Provider downtime does not
block ordinary core requests.

## Offline lease

An offline paid-extra lease, if the chosen business model needs one, should be signed, short-lived,
versioned, audience/installation/subject-bound, capability-scoped, and key-rotatable. Validation
must define clock rollback, last-trusted-time storage, expiry, grace, revocation limits, and backup
restore behavior. A development issuer does not ship in production binaries.

Invalid or expired leases deny only the additive capability. Grace should default to read existing
data and local/BYO fallback, not start new provider-funded work. This remains deferred until a
human-owned legal/product grace policy exists.

## Threat and edge-case checklist

- client plan/localStorage/header spoofing;
- direct API, MCP, worker, and queued-claim bypasses;
- cross-subject snapshot or allowance reuse;
- downgrade/expiry between enqueue and claim;
- duplicate, replayed, forged, and out-of-order webhooks;
- reservation crash, cancellation, retry, and reconciliation;
- payment-provider or entitlement-store outage;
- offline clock rollback and restored old snapshots;
- unknown capability/plan/key/lease versions;
- existing-data/export/recovery access after expiry;
- paid execution that falls back to local/BYO without surprise cost or data egress; and
- audit/receipt content that avoids payment PII and provider secrets.

## Decision gates

Implementation remains blocked until:

1. #2012 records the business/licensing model and contribution/rightsholder consequences;
2. retention evidence justifies commercial work under `PRODUCT_DIRECTION.md`;
3. the billing/entitlement subject and work/account model are explicit;
4. legal/rights review settles distribution/process boundaries—an `ee/` folder alone is not a
   safe proprietary boundary;
5. the free baseline is ratified against `LICENSING.md`; and
6. one genuinely additive dark capability is selected for a provider-free kernel proof.

After those gates, the smallest first slice is capability keys + subject abstraction + immutable
core tests + a test/admin grant source + one dark additive use case enforced server-side. Allowance,
offline lease, billing webhook, checkout, and pricing remain separate later issues.

## Non-goals

No current plan names, prices, payment provider, checkout, tax handling, proprietary module, licence
change, entitlement UI campaign, or migration of already-free capabilities. No claim that this note
is legal advice or an Accepted architecture decision.
