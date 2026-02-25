# Deployment Hardening Verification Matrix

Last Updated: 2026-02-25  
Issue: `#142` OPS-16 deployment/container hardening verification matrix

This matrix defines the baseline hardening checks for the container deployment profile and the pass/fail contract for each slice.

Primary automated command (from repo root):

```powershell
powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080
```

If proxy port differs, use the configured value for `-Port`.

## Matrix

| Slice | Check Type | Command | Pass Criteria | Failure Signal |
| --- | --- | --- | --- | --- |
| Required secret enforcement | Automated | `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1` | `docker compose config` fails when `TASKDECK_JWT_SECRET` is missing, with explicit missing-secret message | Secretless compose config succeeds or expected error text is absent |
| Reverse proxy header posture | Automated | `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1` | Proxy response includes required hardening headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Content-Security-Policy`) | Any required header missing or mismatched |
| Unauthorized path behavior | Automated | `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1` | `/api/boards` and `/hubs/boards/negotiate` return deterministic `401` responses through proxy | Expected unauthorized responses do not match |
| Startup, shutdown, restart reliability | Automated | `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1` | Stack starts and reaches readiness; proxy restart preserves readiness; shutdown leaves no running compose services | Ready check timeout, smoke failure after restart, or services still running after stop |
| Edge TLS and backend-exposure posture | Manual | See manual checklist section `C0` | Backend remains non-public while forwarded headers are trusted, and HTTPS terminates at edge/proxy tier for non-local environments | Backend directly internet-accessible or TLS/proxy trust chain is misconfigured |

## Supporting Commands

Secret-gated compose render check:

```bash
TASKDECK_JWT_SECRET=local-test-secret docker compose -f deploy/docker-compose.yml --profile baseline config
```

Manual start/smoke/stop path:

```powershell
powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1
powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1 -Port 8080
powershell -File ./scripts/deploy/Stop-TaskdeckStack.ps1
```
