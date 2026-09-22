# JWT payload admission boundary

Status: draft PR #3325, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defect

`jwt.ts` cast arbitrary JSON to `JwtPayload`. Wrong-type or out-of-range `exp`
claims could reach `Date.toISOString()` and throw. `sessionStore.setSession`
sets reactive identity fields before formatting the expiry, so admission must
reject these payloads before that point. `exp: 0` was also mistaken for an absent
claim, and binary strings returned by `atob` corrupted UTF-8 claims.

## Contract

- Decode UTF-8 strictly and accept only a non-null, non-array JSON object.
- An optional expiry must be a finite number representable by JavaScript Date.
- Preserve valid fractional, negative and boundary NumericDates.
- Epoch zero is present and expired. An absent expiry retains the existing policy.
- Invalid payloads are unusable, not implicitly non-expiring.
- `tokenStorage` applies this validation before persistence and during restoration.
- This is client-side payload admission, not cryptographic verification or a
  replacement for server-side authentication/authorization.

No route, DTO, schema, dependency, migration or server authorization changes.
Public function signatures are unchanged. Reverting the patch needs no data migration.

## Verification and remaining gates

A supplemental Node 22 runner imported the actual production TypeScript modules:
29 cases passed; 24 of those cases failed against the original implementation.
It also exercised the real token-storage functions using an in-memory storage
fixture. A standalone TypeScript 5.8.3 check of the production modules passed.

Canonical regressions were added to `jwt.spec.ts` and `tokenStorage.spec.ts`.
They have NOT been executed in Vitest in this environment. The required Node 24
runtime/dependencies were unavailable; npm registry DNS failed. The supplemental
runner and older standalone compiler do not replace project qualification.

Before ready-for-review: run frontend lint, `npm run typecheck`, `npm run build`,
the full Vitest suite and exact-head hosted CI on the pinned Node 24 toolchain.
Obtain independent review, including the compatibility of the unchanged
missing-expiry policy and invalid-token failure behavior. No release readiness,
secrets-scan result, approval or deployment is claimed by this note.
