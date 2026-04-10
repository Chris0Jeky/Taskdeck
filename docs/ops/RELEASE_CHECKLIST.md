# Release Checklist — Smoke Verification

Last Updated: 2026-04-09
Issue: `#101` OPS-09 staged deployment with blue/green and canary release policy
Workflow: `docs/ops/DEPLOYMENT_WORKFLOW.md`

This checklist defines the smoke verification steps tied to each release transition. All automated checks are implemented in `scripts/deploy/smoke-test.sh`. Manual checks are performed by the release owner during staging validation.

## Pre-Deployment Checks

Complete before starting any deployment phase.

- [ ] Release tag exists and matches expected commit: `git log --oneline -1 v<tag>`
- [ ] CI release pipeline passed: `gh run list --workflow=ci-release.yml --branch=v<tag>`
- [ ] SBOM artifact exists in CI artifacts
- [ ] Container images built successfully in CI
- [ ] No open P0/P1 blockers against the release milestone
- [ ] Release owner identified and available for the deployment window
- [ ] Rollback plan reviewed (see `docs/ops/DEPLOYMENT_WORKFLOW.md`, Rollback Procedure)

## Staging Smoke (Phase 2 Gate)

Automated checks (run via `scripts/deploy/smoke-test.sh`):

- [ ] **S1**: Health endpoint returns 200: `GET /health/ready`
- [ ] **S2**: API root responds: `GET /api/` returns non-error response
- [ ] **S3**: Authentication flow works: `POST /api/auth/register` or `POST /api/auth/login` returns 200/201
- [ ] **S4**: Board endpoint auth gate: `GET /api/boards` returns 401 when unauthenticated
- [ ] **S5**: Frontend loads: `GET /` returns HTML with expected `<title>` or root element
- [ ] **S6**: SignalR negotiation: `POST /hubs/boards/negotiate` returns connection info (may return 401 for unauthenticated; non-5xx is pass)
- [ ] **S7**: Static assets served: Frontend CSS/JS bundles return 200
- [ ] **S8**: Reverse proxy headers present: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`
- [ ] **S9**: No container restarts: All containers show `Up` status with zero restart count

Manual checks (release owner):

- [ ] **M1**: Navigate to the staging URL in a browser; confirm the Home page renders correctly
- [ ] **M2**: Register a new user or log in with test credentials
- [ ] **M3**: Create a board, add a column, create a card — verify the core capture loop
- [ ] **M4**: Open the Review view — verify proposals render (may be empty if no automation ran)
- [ ] **M5**: Check browser console for JavaScript errors — zero errors expected
- [ ] **M6**: Verify dark mode toggle works if design-token theme is active
- [ ] **M7**: Check response times are subjectively acceptable (< 2 seconds for page loads)

## Canary Verification (Phase 3 Gate)

Automated (continuous during canary window):

- [ ] **C1**: Health endpoint polled every 30 seconds — zero failures over 15-minute window
- [ ] **C2**: Smoke test suite passes against canary endpoint at start, midpoint, and end of window
- [ ] **C3**: Application logs show no unhandled exceptions or panic-level entries
- [ ] **C4**: Container memory and CPU usage remain within expected bounds (no runaway growth)

Observational (release owner monitors):

- [ ] **C5**: No spike in error rates compared to the live environment
- [ ] **C6**: No user-reported issues during the canary window (if applicable)
- [ ] **C7**: Database migrations completed successfully (check API startup logs)

## Post-Promotion Verification (Phase 4 Gate)

Run immediately after traffic switch to confirm production health.

- [ ] **P1**: Full smoke test suite passes against production URL
- [ ] **P2**: Health endpoint returns 200 on production URL
- [ ] **P3**: Sample authenticated API call succeeds (login + board list)
- [ ] **P4**: WebSocket/SignalR connection establishes successfully
- [ ] **P5**: Previous (rollback) slot is still running and healthy
- [ ] **P6**: Release log entry recorded with timestamp, version, slot, and owner

## Post-Release (within 24 hours)

- [ ] **R1**: Monitor production error logs for 24 hours — no new error patterns
- [ ] **R2**: Verify automated backups ran successfully (check backup bucket)
- [ ] **R3**: Update release notes / changelog if not already done
- [ ] **R4**: Close the release milestone or issue
- [ ] **R5**: Previous slot may be stopped after 24-hour stability window

## Failure Response Matrix

| Check Failed | Severity | Action |
|---|---|---|
| Any S* check | **Blocks staging** | Fix and re-deploy to staging |
| Any M* check | **Blocks staging** | Investigate; fix or document known issue with release owner approval |
| Any C* check | **Triggers rollback** | Execute canary rollback immediately |
| Any P* check | **Triggers rollback** | Execute production rollback immediately |
| Any R* check | **Follow-up** | File issue; does not trigger rollback |

## Using the Smoke Test Script

The portable smoke test script automates all S* checks:

```bash
# Against staging
bash scripts/deploy/smoke-test.sh http://staging.example.com:8080

# Against production
bash scripts/deploy/smoke-test.sh https://taskdeck.example.com

# With verbose output
SMOKE_VERBOSE=1 bash scripts/deploy/smoke-test.sh http://localhost:8080
```

Exit codes:
- `0`: All checks passed
- `1`: One or more checks failed (details printed to stderr)

## References

- `docs/ops/DEPLOYMENT_WORKFLOW.md` — canonical deployment workflow
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md` — container hardening checks
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — DR procedures
- `scripts/deploy/smoke-test.sh` — automated smoke test script
- `.github/workflows/cd-staging-gate.yml` — staging gate workflow
