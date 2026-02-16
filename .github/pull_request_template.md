## Summary

<!-- What changed and why -->

## Verification

- [ ] `dotnet test backend/Taskdeck.sln -c Release`
- [ ] `cd frontend/taskdeck-web && npm run typecheck && npm run build && npx vitest --run`
- [ ] Playwright smoke/E2E executed (if UI or cross-surface behavior changed)

## Documentation

- [ ] `docs/STATUS.md` updated (if shipped behavior changed)
- [ ] `docs/IMPLEMENTATION_MASTERPLAN.md` updated (if roadmap or priorities changed)
- [ ] `docs/TESTING_GUIDE.md` / `docs/MANUAL_TEST_CHECKLIST.md` updated (if verification flow changed)

## Tracking

- [ ] Linked issue included (e.g., `Closes #123`)
- [ ] Linked issue’s project item status reviewed (`Review` while this PR is open, `Done` when this PR is merged)

## Risk Notes

- Security impact:
- Behavior/regression risk:
- Follow-up tasks:
