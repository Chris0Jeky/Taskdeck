# Relation UI admission and producer risk parity

Last Updated: 2026-09-12

Issues: #3070 and #3079. Status: implementation candidate; not merged.

## Design boundary

The shared CardRelations component serves both workspace presentations. Its
proposal handler already refused operations during graph refresh, but its visible
controls did not express that refusal. Add/remove buttons and the two selectors
now include `loading` in their disabled state. Do not put `loading` into
`canRemove`: that predicate controls mounting, so doing so would remove the
button rather than retain the visible graph and explain temporary unavailability.
Existing loading status text remains the explanation. No extra requests, polling,
optimistic graph writes or navigation policy are introduced.

The state sequence is loaded -> refreshing (old graph and choices visible,
actions disabled) -> loaded at the new revision. A failed refresh keeps the
existing fail-closed behavior: discard the graph and its permission evidence,
show the retry control, and retain selection values internally. A successful
retry restores valid selections; missing/archived targets are cleared. Refreshed
read-only or archive state never re-enables writes merely because loading ended.
The handler's loading guard remains defense in depth for synthetic form submits.

The web producer now sends numeric `RiskLevel.Medium` (1), matching the existing
chat/MCP classification for relation add/remove. `ProposalSourceType.Manual` (2)
remains the web source. Source classification is not authority: authenticated
provenance, authorization, revision validation, approval and Apply stay server
owned. Both UUID-compatible IDs keep using the shared LAN fallback. No general
risk classifier, shared enum migration or wider producer admission change is
included; #3061 remains separate.

## Regression contract

Eight deferred component cases cover visible-but-disabled refresh, refreshed
revision on add/remove, failed refresh and recovery, refreshed permission/board
archive/card archive, Viewer behavior and a disappearing selected target.
The original component suite remains unchanged. The four API tests retain the
UUID fallback checks and now assert Medium on both operations, exact endpoint
and revision payloads, one POST and absence of client-authored actor identity.

From `frontend/taskdeck-web`, run:

```sh
npx vitest --run --maxWorkers=2 src/tests/components/CardRelations.spec.ts src/tests/components/CardRelations.refresh.spec.ts src/tests/api/cardRelationsApi.spec.ts
npm run lint
npm run typecheck
npm run build
npx vitest --run --maxWorkers=2
```

Tests were authored but not run locally: the environment has Node 22.16.0 rather
than the repository's Node 24.x, no installed frontend dependencies, and registry
DNS fails. Exact-head hosted results and review must qualify this candidate.
No local red/green, live browser, physical-device or screen-reader result is
claimed. The tests exercise the shared component, not separate full-shell journeys.

## Integration and limitations

Base: `96f4b7cc259487b3bcdd97c2d4da057a1f78c44f`. Original runtime/API-test
files were read from that commit. Five files change; no archive/permission editor,
backend, schema, shared HTTP, CI controls or canonical STATUS/MASTERPLAN edits.
Update shipped capability/progress records only when this candidate is integrated.
Existing human device/keyboard, screen-reader, translation, provider, hosting,
release and control-plane decisions in OUTSTANDING_TASKS.md remain unchanged.

This reduces misleading clicks and producer-dependent review guidance while
preserving the explicit proposal -> review -> Apply boundary. Cancellation and
late proposal receipts are separate request-lifecycle concerns, not claimed fixed
by disabled markup. A successful refresh also does not reserve the graph: server
revision checks still decide whether a later proposal can be approved/applied.
