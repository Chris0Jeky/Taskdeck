# Tracker drift findings

## #2185 archive operation

Primary behavior is fixed in merged PR #2222 and handler-level tests exist. Remaining scope is narrower: real-pipeline persisted archive integration, truthful blocked/no-op remediation text and generated tracker/dashboard refresh. A new broad archive implementation PR would duplicate work.

## #2193 partial dates

The reference-date and plausibility logic is present from PR #2214. Remaining scope is deterministic clock/culture/timezone coverage around Dec/Jan and metadata-like false positives. Do not redesign date extraction unless the residual tests expose a defect.

## #1131 CLI

Current startup already handles `--version` before full boot, connector-key bootstrap and serialized migrations with a protected pre-migration backup. The live risk is that CLI card commands call application services without explicit actor/authorization parity and that operator commands are absent.

## #1309 MCP

Persisted scope enforcement and strict stdio user selection are present. Tool-definition hashing is implemented as a recorder/service but should not be mistaken for accepted runtime pin enforcement. Reconcile checkboxes and focus on package/configuration/live-smoke proof.

## Work-model version drift

Several issue bodies still say v0.3 or “blocked on ADR” even though ADR-0060/0062 are Accepted and the wider work moved to v0.4. #2240 is the only v0.3 assignment substrate; #2093 must consume it.

## Launch drift

The downloadable v0.3 launch kit (#2242) and hosted v0.4 launch kit (#1310) are now separate products/claims. Do not blend installation claims, availability promises or threat models.
