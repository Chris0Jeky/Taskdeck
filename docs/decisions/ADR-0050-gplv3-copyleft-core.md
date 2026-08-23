# ADR-0050: Adopt GPLv3-only for the Taskdeck core

- **Status:** Accepted
- **Date:** 2026-08-12
- **Decision owner:** Maintainer
- **Amended:** 2026-08-23 — automated DCO enforcement paused by explicit maintainer decision;
  `#2019` tracks a possible future restoration
- **Supersedes:** ADR-0044 Decision 3, ADR-0046's MIT constraint, and the
  MIT-forever portion of REVIVAL-03

## Context

Taskdeck previously distributed its core under MIT and recorded an explicit
commitment not to change that licence. On 12 August 2026, after an estate-wide
licensing review, the maintainer explicitly directed that Taskdeck move to GNU
GPL version 3 and that the decision be recorded.

The change is a governance event, not a revocation of permissions already
granted. Copies and versions already received under MIT remain usable under
their accompanying terms. Compatible permissive notices must continue to be
preserved where applicable.

## Decision

1. The current Taskdeck open-source core is licensed `GPL-3.0-only` from the
   repository state released on or after 12 August 2026.
2. New core contributions are accepted for inclusion under GPL-3.0-only. Automated DCO
   attestation is paused by explicit maintainer decision dated 2026-08-23; `#2019` tracks a
   possible future restoration, which requires a new explicit decision. This amendment changes
   neither the core licence nor contributor copyright ownership.
3. The former MIT text and copyright notice remain in `LICENSES/MIT.txt` to
   preserve prior grants, attribution, and compatibility obligations. They do
   not constitute an alternative MIT licence for the current project as a
   whole.
4. The existing free-core product boundary remains open source. Separately
   licensed additive modules may still live under `ee/`, but no code currently
   exists there.
5. Package metadata, generated API metadata, contributor guidance, active
   product documents, and public licence messaging must agree with this ADR.

## Alternatives considered

- **Keep MIT:** rejected by the maintainer's explicit direction to adopt GPLv3.
- **GPL-3.0-or-later:** rejected because the instruction was GPLv3 without an
  express “or later” grant; `GPL-3.0-only` is the narrower unambiguous choice.
- **AGPL-3.0:** rejected because the requested change was GPLv3, not network
  copyleft.
- **Dual MIT/GPL licensing:** rejected because it would preserve the permissive
  distribution option the maintainer chose to replace for the current core.

## Consequences

- Distributors of modified current versions must comply with GPLv3 copyleft and
  corresponding-source requirements.
- Earlier MIT releases remain available under their original terms; this
  decision cannot claw those rights back.
- The prior no-relicensing promise is explicitly superseded, so historical
  documents must point to this ADR rather than silently rewriting the record.
- Third-party notices and separately licensed material remain unchanged.

## References

- `LICENSE`
- `LICENSES/MIT.txt`
- `LICENSING.md`
- ADR-0044
- ADR-0046
- REVIVAL-03 / issue #1299
