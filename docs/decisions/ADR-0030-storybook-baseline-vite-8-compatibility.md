# ADR-0030: Storybook Baseline with Vite 8 Compatibility

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Taskdeck maintainers

## Context

Taskdeck uses Vite 8 in the frontend workspace. The Storybook baseline issue requested Storybook 8.x, but Storybook 8 does not support the Vite 8 toolchain used by the app.

The team still needs a stable, reviewable component catalogue for the 17 `Td*` primitives, plus a place to validate visual variants without touching the production app.

## Decision

Use Storybook 10.3.x for the frontend Storybook baseline.

Reasons:

- Storybook 10.3.x supports `vite@^8.0.0`.
- The CSF3 story format remains the same, so the story authoring model stays familiar.
- The preview should import the app stylesheet so Storybook reflects the production component look and spacing as closely as practical.
- Story files live in `src/stories/` to keep the component directory focused on runtime code.

## Alternatives Considered

- Storybook 8.x: rejected because it is not compatible with Vite 8.
- Deferring Storybook entirely: rejected because the component library needs a reviewable baseline now.
- Co-locating stories with components: workable, but rejected for this baseline because a centralized story directory keeps the setup simpler.

## Consequences

- Storybook can be built and maintained without downgrading Vite.
- Visual review of `Td*` primitives is available through `npm run storybook` and `npm run storybook:build`.
- Story files are excluded from the app typecheck, so production compilation stays focused on runtime code.
- Future contributors have a recorded rationale for the Storybook version choice.

## References

- PR #807
- Issue #251
- `frontend/taskdeck-web/.storybook/main.ts`
- `frontend/taskdeck-web/.storybook/preview.ts`
- `frontend/taskdeck-web/src/stories/`
