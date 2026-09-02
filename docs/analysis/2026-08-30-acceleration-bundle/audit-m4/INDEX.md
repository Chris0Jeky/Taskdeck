# Milestone 4 high-leverage audit

This is not a second full milestone plan. It isolates work that can still accelerate v0.3 or remove blockers for v0.4 without colliding with active implementation.

| Issue | Area | Reconciled state | Finding |
|---|---|---|---|
| [#2238](https://github.com/Chris0Jeky/Taskdeck/issues/2238) | Production backup/restore proof | `implementation-ready-critical` | This is a direct prerequisite for the private shared instance and hosted beta. The production image needs operator commands, manifest/checksum evidence, encrypted/off-host custody and a timed restore drill. |
| [#2239](https://github.com/Chris0Jeky/Taskdeck/issues/2239) | Verify connector credential decryptability in production | `implementation-ready-critical` | ConnectorCredentialService can store/get/delete, but there is no bounded operator verification command that enumerates ciphertext and proves the configured key can decrypt every row without printing plaintext. |
| [#1309](https://github.com/Chris0Jeky/Taskdeck/issues/1309) | MCP packaging, scopes, identity and tool-definition governance | `substantially-landed-residual` | Persisted scope enforcement and strict stdio identity selection are present; tool-definition hashing exists. Comments indicate major ACs landed incrementally. Residual work should be narrowed to distribution/live-demo proof and any explicitly approved hash-pin enforcement, not reimplementation. |
| [#2240](https://github.com/Chris0Jeky/Taskdeck/issues/2240) | Multiple assignments v0.3 slice | `active-shared-contract` | This is the narrow v0.3 assignment precursor to v0.4 #2093. It should own the assignment schema/command contract; #2093 must extend it rather than parallel it. |
| [#1772](https://github.com/Chris0Jeky/Taskdeck/issues/1772) | Private trusted shared instance | `gated-by-ops-proof` | This is Stage 1 of the hosted path: one trusted instance, closed registration and one SQLite volume. It is not the public beta and should finish before the threat model is widened. |
| [#1131](https://github.com/Chris0Jeky/Taskdeck/issues/1131) | CLI hardening and operator commands | `partially-landed-residual` | Program.cs already handles version-before-boot, connector-key bootstrap and serialized migrations with protected pre-migration backup. CardsCommandHandler still calls services without an actor/authorization context, and the dispatcher has no ops command surface. |
| [#2235](https://github.com/Chris0Jeky/Taskdeck/issues/2235) | v0.3 spring clean | `after-cutover-measurement` | Useful cleanup should be evidence-led and must not create broad merge conflicts during the release cutover. |
| [#2242](https://github.com/Chris0Jeky/Taskdeck/issues/2242) | Downloadable beta launch kit | `docs-and-evidence-ready` | This remains distinct from v0.4 hosted launch #1310. It should package installation, upgrade, backup, known gaps, feedback and signatures/checksums for the downloadable beta. |
| [#2185](https://github.com/Chris0Jeky/Taskdeck/issues/2185) | Archive operation silently no-ops | `primary-fix-landed-residual` | PR #2222 fixed the handler and added handler-level tests. The issue comments leave an integration test, explicit remediation prose and generated tracker/dashboard refresh outstanding. |
| [#2193](https://github.com/Chris0Jeky/Taskdeck/issues/2193) | Partial-date triage resolves to implausible dates | `primary-fix-landed-residual` | PR #2214 added a reference date, future-date resolution and plausibility filtering. The open residual is deterministic local/CI coverage around year boundaries and metadata-like false positives. |

## Immediate order

1. #2238 production backup/restore proof.
2. #2239 connector-key decryptability verification.
3. #1131 CLI authorization and ops-command residual.
4. #1309 packaging/live-smoke reconciliation.
5. #2185 and #2193 residual tests/tracker closure.
6. #2240 assignment contract coordination with v0.4 #2093.
7. #1772 private instance only after operations proofs.
8. #2242 downloadable launch kit.
9. #2235 cleanup after release-critical paths settle.
