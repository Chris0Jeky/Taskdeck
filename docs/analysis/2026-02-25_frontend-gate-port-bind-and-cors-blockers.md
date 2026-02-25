# Frontend Gate Blockers (Local Windows Investigation)

Date: 2026-02-25  
Scope: local execution of the frontend full gate (`lint`, `coverage`, `typecheck`, `build`, `E2E`) on branch `chore/frontend-gate-restamp-doc-cleanup`.

## Summary

Frontend lint, coverage, typecheck, and production build passed.
Frontend E2E could not execute due two sequential local-environment blockers:

1. frontend dev server bind failure on `localhost:5173` (`listen EACCES`)
2. CORS preflight rejection when temporarily moving frontend to `localhost:5001`

## What Passed

Commands run from `frontend/taskdeck-web`:

```powershell
npm run lint
npm run test:coverage
npm run typecheck
npm run build
```

Observed outcome:
- lint: pass
- coverage: pass (`378` tests passing)
- typecheck: pass
- build: pass

## Blocker 1: Port 5173 Bind Denied

Reproduction command:

```powershell
npm run test:e2e -- --reporter=line
```

Observed failure signature:
- Playwright web server startup failed
- Vite startup error: `listen EACCES: permission denied ::1:5173`
- repeated repro also surfaced `listen EACCES: permission denied 127.0.0.1:5173`

Additional probes:

```powershell
npm run dev -- --host localhost --port 5174    # fails (EACCES)
npm run dev -- --host 127.0.0.1 --port 5174    # fails (EACCES)
```

Port-scope sanity checks:
- no active listener found on `5173`
- no matching entry found in IPv4/IPv6 excluded port range output
- user-space listeners can bind other ports locally (for example `5001`)

Implication:
- failure appears local and port-specific, not a general inability to run Node servers.

## Blocker 2: Alternate Port CORS Mismatch

To bypass port `5173`, E2E was retried with a local Playwright config copy using port `5001`.
Frontend server started, but API calls were blocked by CORS.

Observed backend logs:
- `Request origin http://localhost:5001 does not have permission to access the resource.`
- OPTIONS preflight returned without allowing the alternate origin

Implication:
- temporary frontend port changes require matching backend allowed-origin configuration.

## Current Verification Posture

- Frontend unit/build posture: re-verified on 2026-02-25.
- Frontend E2E posture: latest successful full run remains 2026-02-24 (`23/23`), with 2026-02-25 local rerun blocked before executing tests.

## Follow-up Options

1. make Playwright frontend port configurable (env-driven) and document supported local override
2. align backend development CORS policy with configurable frontend origin for local E2E runs
3. keep `5173` default but add an explicit fallback workflow in testing docs for machines where `5173` is restricted
