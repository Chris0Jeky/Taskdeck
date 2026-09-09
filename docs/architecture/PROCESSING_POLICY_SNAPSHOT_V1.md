# Processing Policy Snapshot v1

**Status:** accepted contract for CF03-1 (`#2257`); no job, queue, runner, persistence, or policy-profile writer is added by this document.

A processing job will retain the SHA-256 digest of the immutable policy that applied when it was
created. This contract deliberately has no profile id or profile version: a future profile is an
input used to create a snapshot, not an indirect reference that could reinterpret a historical job.
The v0.6 draft's profile wrapper remains planning input; it is not this job-policy byte contract.

## Fields

The v1 snapshot contains only the routing and execution constraints needed before the job schema is
designed:

| Field | Meaning |
| --- | --- |
| `egressClass` | `ProcessingEgressClass` written as its exact kebab-case name. |
| `allowedProcessorIds` | Processor-id allowlist set. It is ordinal-sorted and deduplicated before serialization. An ordered route preference is a separate future field. |
| `allowDiarisation` | Whether the policy permits diarisation. |
| `allowAlignment` | Whether the policy permits alignment. |
| `deadlineUtc` | Absolute UTC deadline, or `null` when this policy does not set one. |
| `costCeiling` | Currency-qualified maximum charge, or `null` when this policy does not set one. A runtime job still applies its own limits; an absent policy constraint grants no authority. |

`costCeiling.currency` is an ISO-4217-shaped three-letter uppercase string. The contract validates
the shape only; it does not claim to validate a current currency registry. `amount` is a
non-negative .NET decimal. It has no provider billing-unit field: it caps the monetary amount in
the stated currency.

## Canonical UTF-8 JSON

The digest source is the following compact UTF-8 JSON, with fields in exactly this order:

```text
schemaVersion, egressClass, allowedProcessorIds, allowDiarisation, allowAlignment, deadlineUtc, costCeiling
```

`schemaVersion` is the literal integer `1`. All fields are always present, including `null` limits.
Dates are UTC only and use `yyyy-MM-ddTHH:mm:ss.fffffffZ`. Decimals use invariant fixed-point form
with no exponent and no insignificant trailing fractional zeroes. The nested `costCeiling` object
orders `amount` before `currency`.

For example, the pinned golden bytes are:

```json
{"schemaVersion":1,"egressClass":"approved-destinations","allowedProcessorIds":["provider.cloud-speech","taskdeck.whisperx"],"allowDiarisation":true,"allowAlignment":false,"deadlineUtc":"2026-09-08T12:34:56.1230000Z","costCeiling":{"amount":1.5,"currency":"GBP"}}
```

Their digest is `sha256:2f874cb048ee092d6a78e3075f37fef8254827a0cc1b3e6fa7bdfc80f0d4c9b5`.

The digest is `sha256:` followed by lowercase hexadecimal SHA-256 over those exact UTF-8 bytes.
Ordinary `JsonSerializerOptions` must not serialize the digest source because property ordering,
null handling, date formatting, and decimal formatting are part of this contract.

## Validation

Construction rejects undefined egress enum values, non-UTC deadlines, invalid processor identifiers,
negative ceilings, and noncanonical currency text. Processor identifiers use the existing manifest
identifier grammar: lowercase ASCII segments separated by `.`, `_`, or `-`, up to 120 characters.
An empty allowlist is valid and fail-closed: it authorizes no processor.
