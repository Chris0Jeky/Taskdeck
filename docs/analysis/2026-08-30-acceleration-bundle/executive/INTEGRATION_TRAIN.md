# Shared-file integration train

## Inputs from vertical agents

Each vertical submits:

- source files owned by the vertical;
- migration model intent in a small machine/human-readable fragment;
- DI/operation/export registrations as patch snippets or checklist;
- tests that compile once integration is applied;
- receipt stating expected shared changes.

## Integration owner sequence

1. Freeze/rebase all source branches against the same contract SHA.
2. Apply domain/entity changes.
3. Update DbContext/configuration and generate one ordered migration.
4. Review generated migration and model snapshot manually.
5. Apply central DI/operation/export registration.
6. Update generated architecture/status docs.
7. Run fresh DB, upgrade fixtures, all focused tests and mandatory suite.
8. Publish one integration receipt mapping each included vertical/issue.

## Failure isolation

If the integration PR fails, revert/remove one vertical registration/migration fragment at a time. Do not ask every feature agent to edit the model snapshot independently.

## Naming

Suggested branches:

- `integration/v04-context-fabric-schema-1`
- `integration/v04-work-model-contract-1`
- `integration/v04-smart-ci-contract-1`

The integration PR closes no feature issue by itself unless it also contains the feature's observable behavior and tests.
