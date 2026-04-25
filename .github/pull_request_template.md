## Summary

<!-- What changed and why -->

## Verification

- [ ] `dotnet test backend/Taskdeck.sln -c Release`
- [ ] `Push-Location frontend/taskdeck-web; npm run typecheck; if ($LASTEXITCODE -ne 0) { Pop-Location; exit $LASTEXITCODE }; npm run build; if ($LASTEXITCODE -ne 0) { Pop-Location; exit $LASTEXITCODE }; npx vitest --run; $code = $LASTEXITCODE; Pop-Location; if ($code -ne 0) { exit $code }`
- [ ] Playwright smoke/E2E executed (if UI or cross-surface behavior changed)

## Documentation

- [ ] `docs/STATUS.md` updated (if shipped behavior changed)
- [ ] `docs/IMPLEMENTATION_MASTERPLAN.md` updated (if roadmap or priorities changed)
- [ ] `docs/TESTING_GUIDE.md` / `docs/MANUAL_TEST_CHECKLIST.md` updated (if verification flow changed)

## Tracking

- [ ] Linked issue included (e.g., `Closes #123`)
- [ ] Linked issue’s project item status reviewed (`Review` while this PR is open, `Done` when this PR is merged)

## CI Workflow Validation

- [ ] If this PR touches `.github/workflows/`, `deploy/`, `scripts/`, or `*.csproj` files: confirm CI Extended passed (auto-triggered on these paths)

## Risk Notes

- Security impact:
- Behavior/regression risk:
- Follow-up tasks:
